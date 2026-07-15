using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pigeon;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[HarmonyPatch(typeof(DirectiveWindow))]
static class DirectiveWindowPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("OnOpen")]
    static void OnOpenPostfix(DirectiveWindow __instance)
    {
        BacklogUI.OnWindowOpened(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(DirectiveWindow.SetupDirectives))]
    static void SetupDirectivesPostfix(DirectiveWindow __instance)
    {
        BacklogUI.OnDirectivesRefreshed(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnDisable")]
    static void OnDisablePostfix(DirectiveWindow __instance)
    {
        BacklogUI.OnWindowClosed(__instance);
    }
}

[HarmonyPatch(typeof(DirectiveWindow), "Update")]
static class DirectiveWindowUpdatePatch
{
    static void Postfix(DirectiveWindow __instance)
    {
        // Selection-mode click intercept (path edit or reroll select).
        if (BacklogUI.IsAnySelectionMode)
        {
            try
            {
                if (PlayerInput.Controls == null)
                    return;
                if (!PlayerInput.Controls.Menu.Click.WasPressedThisFrame())
                    return;
                if (EventSystem.current == null)
                    return;

                Vector2 pos;
                if (Mouse.current != null)
                    pos = Mouse.current.position.ReadValue();
                else
                    pos = Input.mousePosition;

                var ped = new PointerEventData(EventSystem.current) { position = pos };
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(ped, results);

                for (int i = 0; i < results.Count; i++)
                {
                    var button = results[i].gameObject.GetComponentInParent<DirectiveButton>();
                    if (button != null)
                    {
                        BacklogUI.TryHandleDirectiveClick(button);
                        break;
                    }
                }
            }
            catch
            {
                // ignore input edge cases
            }
            return;
        }

        // Light auto-claim poll while the window is open.
        if (BacklogImprovementsPlugin.EnablePreselect?.Value != false)
            PathLogic.TryAutoClaim(__instance);
    }
}

[HarmonyPatch(typeof(DirectiveButton))]
static class DirectiveButtonPatches
{
    private static readonly FieldInfo DirectiveField =
        AccessTools.Field(typeof(DirectiveButton), "directive");
    private static readonly MethodInfo SetupAvailableMethod =
        AccessTools.Method(typeof(DirectiveButton), "SetupAvailable");

    /// <summary>
    /// Block hold-to-activate / claim while editing path or choosing a reroll target.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(DirectiveButton.HasUnlockAction))]
    static bool HasUnlockActionPrefix(DirectiveButton __instance, out HoverInfo.UnlockActionParams data, ref bool __result)
    {
        data = default;
        if (!BacklogUI.IsAnySelectionMode)
            return true;

        __result = false;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(DirectiveButton.GetPrimaryBinding))]
    static bool GetPrimaryBindingPrefix(
        DirectiveButton __instance,
        out InputAction binding,
        out string label,
        ref bool __result)
    {
        if (!BacklogUI.IsAnySelectionMode)
        {
            binding = null;
            label = null;
            return true;
        }

        binding = PlayerInput.Controls.Menu.Click;
        label = BacklogUI.IsRerollSelecting ? "reroll" : "select";
        __result = true;
        return false;
    }

    /// <summary>
    /// While editing path, force available look so every non-finished node is clickable.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(DirectiveButton.Setup))]
    static void SetupPostfix(DirectiveButton __instance, DirectiveInstance directive)
    {
        if (!PathLogic.IsEditing || directive == null)
            return;

        try
        {
            if (directive.IsComplete && directive.HasClaimedRewards)
                return;

            SetupAvailableMethod?.Invoke(__instance, null);
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogWarning($"SetupAvailable force failed: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(DirectiveInstance), nameof(DirectiveInstance.CanBeActivated))]
static class CanBeActivatedPatch
{
    static bool Prefix(DirectiveInstance __instance, ref bool __result)
    {
        try
        {
            if (BacklogImprovementsPlugin.EnablePreselect?.Value == false)
                return true;

            if (PathLogic.IsEditing)
            {
                // Never activate while editing; selection is handled by BacklogUI.
                __result = false;
                return false;
            }

            if (!PathLogic.ShouldAllowActivation(__instance, out bool forceTrue))
            {
                if (forceTrue)
                {
                    __result = true;
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogError($"CanBeActivated patch: {ex.Message}");
            return true;
        }
    }
}

[HarmonyPatch(typeof(PlayerData), "GetCurrrentDirectiveTier")]
static class DirectiveTierPatch
{
    static void Postfix(ref int __result)
    {
        if (PathLogic.IsEditing)
            __result = 99;
    }
}

[HarmonyPatch(typeof(DirectiveInstance), nameof(DirectiveInstance.ClaimRewards))]
static class DirectiveInstanceClaimPatch
{
    static void Postfix(DirectiveInstance __instance)
    {
        try
        {
            if (BacklogImprovementsPlugin.EnablePreselect?.Value == false)
                return;

            if (PathLogic.IsEditing)
                return;

            var window = UnityEngine.Object.FindObjectOfType<DirectiveWindow>();
            PathLogic.OnDirectiveClaimed(__instance, window);
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogError($"DirectiveInstance.ClaimRewards postfix: {ex.Message}");
        }
    }
}
