using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pigeon;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Free next backlog page — hot-reloadable.
/// Does NOT mutate HoverInfoHold.cost. Vanilla cost data stays intact so the
/// setting can flip either direction instantly. Behavior is gated only by
/// EnableFreePages at call time, on a tracked next-page button reference.
/// </summary>
public static class FreePages
{
    private static readonly FieldInfo NextPageButtonField =
        AccessTools.Field(typeof(DirectiveWindow), "nextPageButton");
    private static readonly FieldInfo HoldDurationField =
        AccessTools.Field(typeof(HoverInfoHold), "holdDuration");
    private static readonly FieldInfo OnCompleteEventField =
        AccessTools.Field(typeof(HoverInfoHold), "onComplete");
    private static readonly MethodInfo OnCompleteMethod =
        AccessTools.Method(typeof(HoverInfoHold), "OnComplete");

    /// <summary>Live next-page buttons we have seen (never guess by name).</summary>
    private static readonly List<HoverInfoHold> KnownNextPageButtons = new List<HoverInfoHold>(4);

    public static bool IsEnabled =>
        BacklogImprovementsPlugin.EnableFreePages != null
        && BacklogImprovementsPlugin.EnableFreePages.Value;

    /// <summary>
    /// Remember this window's next-page button. Called from open/refresh paths.
    /// No longer mutates cost arrays.
    /// </summary>
    public static void ApplyFreeNextPage(DirectiveWindow window)
    {
        RegisterWindow(window);
    }

    public static void RegisterWindow(DirectiveWindow window)
    {
        if (window == null || NextPageButtonField == null)
            return;

        try
        {
            var button = NextPageButtonField.GetValue(window) as HoverInfoHold;
            if (button == null)
                return;

            PruneKnown();
            for (int i = 0; i < KnownNextPageButtons.Count; i++)
            {
                if (ReferenceEquals(KnownNextPageButtons[i], button))
                    return;
            }

            KnownNextPageButtons.Add(button);
            BacklogImprovementsPlugin.Log?.LogInfo(
                $"[FreePages] Tracked next-page button id={button.GetInstanceID()} (known={KnownNextPageButtons.Count}). Free={IsEnabled}");
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogWarning($"Free pages register failed: {ex.Message}");
        }
    }

    public static void RefreshOpenWindows()
    {
        try
        {
            var windows = UnityEngine.Object.FindObjectsOfType<DirectiveWindow>(true);
            if (windows == null)
                return;

            for (int i = 0; i < windows.Length; i++)
                RegisterWindow(windows[i]);

            BacklogImprovementsPlugin.Log?.LogInfo(
                $"[FreePages] SettingChanged → Free={IsEnabled}, trackedButtons={KnownNextPageButtons.Count}");
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogWarning($"Free pages refresh failed: {ex.Message}");
        }
    }

    public static bool IsNextPageButton(HoverInfoHold hold)
    {
        if (hold == null)
            return false;

        PruneKnown();
        for (int i = 0; i < KnownNextPageButtons.Count; i++)
        {
            if (ReferenceEquals(KnownNextPageButtons[i], hold))
                return true;
        }

        // Late registration: if this hold is a window's nextPageButton, track it now.
        try
        {
            if (NextPageButtonField == null)
                return false;

            var window = hold.GetComponentInParent<DirectiveWindow>();
            if (window != null)
            {
                var button = NextPageButtonField.GetValue(window) as HoverInfoHold;
                if (ReferenceEquals(button, hold))
                {
                    KnownNextPageButtons.Add(hold);
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static void PruneKnown()
    {
        for (int i = KnownNextPageButtons.Count - 1; i >= 0; i--)
        {
            if (KnownNextPageButtons[i] == null)
                KnownNextPageButtons.RemoveAt(i);
        }
    }

    internal static float GetHoldDuration(HoverInfoHold hold)
    {
        if (hold == null || HoldDurationField == null)
            return 1f;
        try
        {
            return (float)HoldDurationField.GetValue(hold);
        }
        catch
        {
            return 1f;
        }
    }

    internal static Action GetOnCompleteDelegate(HoverInfoHold hold)
    {
        if (hold == null || OnCompleteMethod == null)
            return null;
        return (Action)Delegate.CreateDelegate(typeof(Action), hold, OnCompleteMethod);
    }

    internal static void InvokeOnCompleteEvent(HoverInfoHold hold)
    {
        try
        {
            var evt = OnCompleteEventField?.GetValue(hold) as UnityEvent;
            evt?.Invoke();
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogWarning($"Free pages onComplete invoke: {ex.Message}");
        }
    }
}

/// <summary>
/// When free pages is on, strip cost from the next-page unlock action only.
/// Vanilla cost array is left untouched.
/// </summary>
[HarmonyPatch(typeof(HoverInfoHold), nameof(HoverInfoHold.HasUnlockAction))]
static class FreePagesHasUnlockActionPatch
{
    /// <summary>
    /// Full replace when free+next-page so we never depend on postfix/out quirks.
    /// Otherwise run vanilla.
    /// </summary>
    static bool Prefix(HoverInfoHold __instance, out HoverInfo.UnlockActionParams data, ref bool __result)
    {
        data = default;

        try
        {
            if (!FreePages.IsEnabled || !FreePages.IsNextPageButton(__instance))
                return true; // vanilla

            data.Duration = FreePages.GetHoldDuration(__instance);
            data.OnComplete = FreePages.GetOnCompleteDelegate(__instance);
            data.Cost = null; // free
            if (Global.Instance != null)
                data.UnlockLoop = Global.Instance.ScrapLoop;

            __result = true;
            return false;
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogWarning($"Free pages HasUnlockAction: {ex.Message}");
            data = default;
            return true;
        }
    }
}

/// <summary>
/// Skip resource charge on next-page complete while free is enabled.
/// Invokes the UnityEvent directly (GoNextPage) without TryRemoveResources.
/// </summary>
[HarmonyPatch(typeof(HoverInfoHold), "OnComplete")]
static class FreePagesOnCompletePatch
{
    static bool Prefix(HoverInfoHold __instance)
    {
        try
        {
            if (!FreePages.IsEnabled || !FreePages.IsNextPageButton(__instance))
                return true;

            FreePages.InvokeOnCompleteEvent(__instance);
            return false;
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogWarning($"Free pages OnComplete: {ex.Message}");
            return true;
        }
    }
}

/// <summary>
/// Show "activate" instead of "unlock" while free is enabled on next-page.
/// </summary>
[HarmonyPatch(typeof(HoverInfoHold), nameof(HoverInfoHold.GetPrimaryBinding))]
static class FreePagesGetPrimaryBindingPatch
{
    static bool Prefix(
        HoverInfoHold __instance,
        out InputAction binding,
        out string label,
        ref bool __result)
    {
        binding = null;
        label = null;

        try
        {
            if (!FreePages.IsEnabled || !FreePages.IsNextPageButton(__instance))
                return true;

            binding = PlayerInput.Controls.Menu.Click;
            label = TextBlocks.GetString("activate");
            __result = true;
            return false;
        }
        catch
        {
            binding = null;
            label = null;
            return true;
        }
    }
}

/// <summary>
/// Ensure the next-page button is tracked as soon as the directive window opens,
/// before hover queries run.
/// </summary>
[HarmonyPatch(typeof(DirectiveWindow), "OnOpen")]
static class FreePagesDirectiveWindowOnOpenPatch
{
    static void Prefix(DirectiveWindow __instance)
    {
        FreePages.RegisterWindow(__instance);
    }

    static void Postfix(DirectiveWindow __instance)
    {
        FreePages.RegisterWindow(__instance);
    }
}

[HarmonyPatch(typeof(DirectiveWindow), nameof(DirectiveWindow.SetupDirectives))]
static class FreePagesSetupDirectivesPatch
{
    static void Postfix(DirectiveWindow __instance)
    {
        FreePages.RegisterWindow(__instance);
    }
}
