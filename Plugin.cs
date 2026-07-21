using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine.InputSystem;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[BepInDependency("sparroh.uilibrary")]
[MycoMod(null, ModFlags.IsClientSide)]
public class BacklogImprovementsPlugin : BaseUnityPlugin
{
    public const string PluginGUID = "sparroh.backlogimprovements";
    public const string PluginName = "BacklogImprovements";
    public const string PluginVersion = "2.1.1";

    /// <summary>Legacy PreselectBacklog GUID — used only to migrate saved paths.</summary>
    public const string LegacyPreselectGuid = "sparroh.preselectbacklog";

    internal static ManualLogSource Log;
    public static BacklogImprovementsPlugin Instance;

    public static ConfigEntry<bool> EnablePreselect;
    public static ConfigEntry<bool> EnableReroll;
    public static ConfigEntry<bool> EnableFreePages;
    public static ConfigEntry<int> CostPerDirective;

    /// <summary>Dev-only. Must edit source and rebuild to enable force-complete (F2).</summary>
    private const bool sparrohmode = true;

    private Harmony _harmony;
    private InputAction _forceCompleteAction;

    private void Awake()
    {
        Log = Logger;
        Instance = this;

        EnablePreselect = Config.Bind(
            "Features",
            "EnablePreselect",
            true,
            "Enable path preselect, auto-claim, and auto-activate.");

        EnableReroll = Config.Bind(
            "Features",
            "EnableReroll",
            true,
            "Enable reroll page / reroll one controls.");

        EnableFreePages = Config.Bind(
            "Features",
            "EnableFreePages",
            true,
            "Remove the resource cost for generating the next backlog page.");

        CostPerDirective = Config.Bind(
            "Reroll",
            "CostPerDirective",
            50,
            "Gats cost to reroll a single backlog directive. Page reroll costs this amount times the number of eligible directives.");

        PathStore.Load();

        try
        {
            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll();
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to initialize {PluginName}: {ex}");
        }

        SetupForceCompleteAction();
    }

    private void SetupForceCompleteAction()
    {
        try
        {
            _forceCompleteAction?.Dispose();
            _forceCompleteAction = null;

            // Hardcoded dev flag — not a config binding; edit source + rebuild to enable.
            if (!sparrohmode)
                return;

            if (EnablePreselect?.Value == false)
                return;

            _forceCompleteAction = new InputAction(
                "BacklogForceComplete",
                InputActionType.Button,
                binding: "<Keyboard>/f2");
            _forceCompleteAction.performed += _ =>
            {
                try
                {
                    if (!sparrohmode || EnablePreselect?.Value == false)
                        return;
                    PathLogic.TryForceCompleteCurrent();
                }
                catch (Exception ex)
                {
                    Log?.LogError($"Force-complete action: {ex.Message}");
                }
            };
            _forceCompleteAction.Enable();
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"Could not bind force-complete key: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        try
        {
            _forceCompleteAction?.Disable();
            _forceCompleteAction?.Dispose();
            _forceCompleteAction = null;

            BacklogUI.CleanupAll();
            PathStore.Save();
            _harmony?.UnpatchSelf();
        }
        catch (Exception ex)
        {
            Log?.LogError($"Cleanup failed: {ex.Message}");
        }
    }
}
