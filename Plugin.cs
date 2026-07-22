using System;
using System.IO;
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
    public const string PluginVersion = "2.1.2";

    /// <summary>Legacy PreselectBacklog GUID — used only to migrate saved paths.</summary>
    public const string LegacyPreselectGuid = "sparroh.preselectbacklog";

    internal static ManualLogSource Log;
    public static BacklogImprovementsPlugin Instance;

    public static ConfigEntry<bool> EnablePreselect;
    public static ConfigEntry<bool> EnableReroll;
    public static ConfigEntry<bool> EnableFreePages;
    public static ConfigEntry<int> CostPerDirective;

    /// <summary>Dev-only. Must edit source and rebuild to enable force-complete (F2).</summary>
    private const bool sparrohmode = false;

    private Harmony _harmony;
    private InputAction _forceCompleteAction;
    private FileSystemWatcher _configWatcher;
    private DateTime _lastConfigReloadUtc = DateTime.MinValue;
    private bool _configReloadQueued;

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
            "Remove the resource cost for generating the next backlog page. Takes effect immediately (no restart).");
        EnableFreePages.SettingChanged += (_, __) =>
        {
            Log?.LogInfo($"[FreePages] SettingChanged → Free={EnableFreePages.Value}");
            FreePages.RefreshOpenWindows();
        };

        CostPerDirective = Config.Bind(
            "Reroll",
            "CostPerDirective",
            50,
            "Gats cost to reroll a single backlog directive. Page reroll costs this amount times the number of eligible directives.");

        PathStore.Load();
        SetupConfigWatcher();

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

    /// <summary>
    /// Fallback when a settings UI writes the .cfg without touching our live ConfigEntry
    /// (older ModSettingsMenu). Reload from disk and fire FreePages refresh if needed.
    /// </summary>
    private void SetupConfigWatcher()
    {
        try
        {
            string path = Config.ConfigFilePath;
            if (string.IsNullOrEmpty(path))
                return;

            string dir = Path.GetDirectoryName(path);
            string file = Path.GetFileName(path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file) || !Directory.Exists(dir))
                return;

            _configWatcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _configWatcher.Changed += OnConfigFileChanged;
            _configWatcher.Created += OnConfigFileChanged;
            _configWatcher.Renamed += OnConfigFileChanged;
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"Config watcher not available: {ex.Message}");
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        // FileSystemWatcher is off the main thread — queue for Update.
        _configReloadQueued = true;
    }

    private void Update()
    {
        if (!_configReloadQueued)
            return;

        // Debounce bursty write notifications.
        var now = DateTime.UtcNow;
        if ((now - _lastConfigReloadUtc).TotalMilliseconds < 200)
            return;

        _configReloadQueued = false;
        _lastConfigReloadUtc = now;

        try
        {
            bool before = EnableFreePages?.Value ?? false;
            Config.Reload();
            bool after = EnableFreePages?.Value ?? false;
            Log?.LogInfo($"[FreePages] Config.Reload() → Free={after} (was {before})");
            FreePages.RefreshOpenWindows();
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"Config reload failed: {ex.Message}");
        }
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
            if (_configWatcher != null)
            {
                _configWatcher.EnableRaisingEvents = false;
                _configWatcher.Changed -= OnConfigFileChanged;
                _configWatcher.Created -= OnConfigFileChanged;
                _configWatcher.Renamed -= OnConfigFileChanged;
                _configWatcher.Dispose();
                _configWatcher = null;
            }

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
