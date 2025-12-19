using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using Pigeon.UI;

public class PathLineDrawer : MonoBehaviour {
    public List<Vector3[]> Lines = new List<Vector3[]>();

    void OnGUI() {
        if (!SparrohPlugin.DirectiveWindowActive) return;
        var canvas = GetComponent<Canvas>();
        if (Lines.Count == 0 || canvas == null || !canvas.isActiveAndEnabled || canvas.worldCamera == null) return;

        var material = new Material(Shader.Find("Sprites/Default"));
        material.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix();
        GL.Begin(GL.LINES);
        GL.Color(Color.yellow);

        var camera = canvas.worldCamera;
        foreach (var line in Lines) {
            if (line.Length >= 2) {
                Vector3 screenPos1 = camera.WorldToScreenPoint(line[0]);
                Vector3 screenPos2 = camera.WorldToScreenPoint(line[1]);
                screenPos1.y = Screen.height - screenPos1.y;
                screenPos2.y = Screen.height - screenPos2.y;
                GL.Vertex3(screenPos1.x, screenPos1.y, 0);
                GL.Vertex3(screenPos2.x, screenPos2.y, 0);
            }
        }
        GL.End();
        GL.PopMatrix();
    }
}

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[MycoMod(null, ModFlags.IsClientSide)]
public class SparrohPlugin : BaseUnityPlugin
{
    public const string PluginGUID = "sparroh.preselectbacklog";
    public const string PluginName = "PreselectBacklog";
    public const string PluginVersion = "1.0.0";

    internal static new ManualLogSource Logger;

    public static Dictionary<int, List<int>> PreselectedPaths = new Dictionary<int, List<int>>();

    public static bool PreselectMode = false;
    public static bool DirectiveWindowActive = false;
    private InputAction completeCurrentAction;

    private void Awake()
    {
        Logger = base.Logger;

        LoadPreselectedPaths();

        var harmony = new Harmony(PluginGUID);

        harmony.PatchAll(typeof(DirectivePatches));
        Logger.LogInfo("Patched DirectivePatches");

        harmony.PatchAll(typeof(DirectiveWindowPatches));
        Logger.LogInfo("Patched DirectiveWindowPatches");

        harmony.PatchAll(typeof(PlayerDataPatches));
        Logger.LogInfo("Patched PlayerDataPatches");
        
        /*
         * completeCurrentAction = new InputAction("CompleteCurrentDirective", binding: "<Keyboard>/f2");
         * completeCurrentAction.performed += _ => CompleteCurrentDirective();
         * completeCurrentAction.Enable();
         */

        Logger.LogInfo($"{PluginName} loaded successfully. Use OnGUI button in directive window to toggle preselect mode, F2 to complete current directive.");
    }

    private void LoadPreselectedPaths()
    {
        var filePath = Path.Combine(Paths.ConfigPath, $"{PluginGUID}.txt");
        try
        {
            if (File.Exists(filePath))
            {
                var lines = File.ReadAllLines(filePath);
                PreselectedPaths.Clear();
                foreach (var line in lines)
                {
                    var parts = line.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int page) && int.TryParse(parts[1], out int idx))
                    {
                        if (!PreselectedPaths.ContainsKey(page))
                            PreselectedPaths[page] = new List<int>();
                        PreselectedPaths[page].Add(idx);
                    }
                }
                Logger.LogInfo("Loaded preselected paths from file");
            }
        }
        catch (System.Exception e)
        {
            Logger.LogError($"Error loading preselected paths: {e.Message}");
        }
    }

    private void SavePreselectedPaths()
    {
        var filePath = Path.Combine(Paths.ConfigPath, $"{PluginGUID}.txt");
        try
        {
            Directory.CreateDirectory(Paths.ConfigPath);
            var lines = new List<string>();
            foreach (var kvp in PreselectedPaths)
            {
                foreach (var idx in kvp.Value)
                {
                    lines.Add($"{kvp.Key},{idx}");
                }
            }
            File.WriteAllLines(filePath, lines.ToArray());
            Logger.LogInfo("Saved preselected paths to file");
        }
        catch (System.Exception e)
        {
            Logger.LogError($"Error saving preselected paths: {e.Message}");
        }
    }

    private static bool CanBeActivatedPrefix(object __instance, ref bool __result)
    {
        if (SparrohPlugin.PreselectMode)
        {
            __result = true;
            return false;
        }

        int page = PlayerData.Instance.currentDirectivePage;
        if (SparrohPlugin.PreselectedPaths.TryGetValue(page, out var path))
        {
            var directives = PlayerData.Instance.defaultDirectives;
            int idx = -1;
            for (int i = 0; i < directives.Length; i++)
            {
                if (directives[i] == __instance)
                {
                    idx = i;
                    break;
                }
            }
            if (idx != -1 && path.Contains(idx))
            {
                SparrohPlugin.Logger.LogInfo($"Allowing activation for preselected directive {idx} outside preselect mode");
                __result = true;
                return false;
            }
        }

        return true;
    }


    private void Update()
    {
        DirectiveWindowActive = FindObjectOfType<DirectiveWindow>() != null;

        int currentIdx = PlayerData.Instance.currentDefaultDirective;
        if (currentIdx >= 0 && currentIdx < PlayerData.Instance.defaultDirectives.Length)
        {
            var directive = PlayerData.Instance.defaultDirectives[currentIdx];
            if (directive.IsComplete && !directive.HasClaimedRewards)
            {
                int page = PlayerData.Instance.currentDirectivePage;
                if (SparrohPlugin.PreselectedPaths.TryGetValue(page, out var path) && path.Contains(currentIdx))
                {
                    Logger.LogInfo("Auto-claiming completed preselected directive");
                    var claimMethod = directive.GetType().GetMethod("ClaimRewards");
                    if (claimMethod != null)
                    {
                        claimMethod.Invoke(directive, null);
                    }
                }
            }
        }
    }

    private void OnGUI()
    {
        if (DirectiveWindowActive)
        {
            float buttonX = 10f;
            float buttonY = Screen.height - 60f;
            float buttonWidth = 150f;
            float buttonHeight = 50f;
            string buttonText = PreselectMode ? "Disable Preselect" : "Enable Preselect";
            if (GUI.Button(new UnityEngine.Rect(buttonX, buttonY, buttonWidth, buttonHeight), buttonText))
            {
                PreselectMode = !PreselectMode;
                Logger.LogInfo($"Preselect mode: {PreselectMode}");

                var window = GameObject.FindObjectOfType<DirectiveWindow>();
                if (window != null)
                {
                    window.SetupDirectives(false);
                }

                if (!PreselectMode)
                {
                    var page = PlayerData.Instance.currentDirectivePage;
                    if (PreselectedPaths.TryGetValue(page, out var path) && path.Count > 0)
                    {
                        var directiveIdx = path[0];
                        if (directiveIdx < PlayerData.Instance.defaultDirectives.Length)
                        {
                            var directive = PlayerData.Instance.defaultDirectives[directiveIdx];
                            if (directive.CanBeActivated())
                            {
                                directive.Activate();
                                PlayerData.Instance.currentDefaultDirective = directiveIdx;
                                Logger.LogInfo($"Auto-activated first preselected directive {directiveIdx}");
                            }
                        }
                    }
                    SavePreselectedPaths();
                }
            }
        }
    }

    private void CompleteCurrentDirective()
    {
        try
        {
            Logger.LogInfo("F2 pressed, attempting to complete current directive");
            var currentIdx = PlayerData.Instance.currentDefaultDirective;
            Logger.LogInfo($"Current directive index: {currentIdx}");
            if (currentIdx == -1)
            {
                var allDirectives = PlayerData.Instance.defaultDirectives;
                for (int i = 0; i < allDirectives.Length; i++)
                {
                    if (allDirectives[i] != null && allDirectives[i].IsActive)
                    {
                        currentIdx = i;
                        Logger.LogInfo($"Found active directive at index {i}");
                        break;
                    }
                }
                if (currentIdx == -1)
                {
                    Logger.LogError("No active directive found");
                    return;
                }
            }
            var window = GameObject.FindObjectOfType<DirectiveWindow>();
            if (window == null)
            {
                Logger.LogError("DirectiveWindow not found");
                return;
            }
            var directives = (DirectiveButton[])typeof(DirectiveWindow).GetField("directives", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(window);
            if (directives == null)
            {
                Logger.LogError("Directives field not found or null");
                return;
            }
            if (currentIdx < 0 || currentIdx >= directives.Length)
            {
                Logger.LogError($"CurrentIdx {currentIdx} out of range for directives length {directives.Length}");
                return;
            }
            var button = directives[currentIdx];
            var directive = PlayerData.Instance.defaultDirectives[currentIdx];

            try
            {
                Logger.LogInfo($"Directive type: {directive.GetType().FullName}");

            Logger.LogInfo("F2: Calling CompleteAllProperties to force completion");
            var completeAllMethod = directive.GetType().GetMethod("CompleteAllProperties", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (completeAllMethod != null)
            {
                completeAllMethod.Invoke(directive, new object[] { });
                Logger.LogInfo("F2: CompleteAllProperties called successfully");
            }
            else
            {
                Logger.LogError("CompleteAllProperties method not found");
            }

                var fields = directive.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                foreach (var f in fields)
                {
                    Logger.LogInfo($"Directive field: {f.Name} {f.FieldType.Name}");
                }

                var properties = directive.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                foreach (var p in properties)
                {
                    Logger.LogInfo($"Directive property: {p.Name} {p.PropertyType.Name}");
                }

                var directiveMethods = directive.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                foreach (var m in directiveMethods.Where(m => m.Name.Contains("Complete") || m.Name.Contains("Progress") || m.Name.Contains("Is") || m.Name.Contains("Update") || m.IsPublic))
                {
                    Logger.LogInfo($"Directive method: {m.Name} {m.ReturnType.Name}");
                }

                var allFields = directive.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                List<FieldInfo> allEnumFields = new List<FieldInfo>();
                foreach (var f in allFields)
                {
                    if (f.FieldType.IsEnum)
                    {
                        allEnumFields.Add(f);
                    }
                }
                foreach (var f in allEnumFields)
                {
                    Logger.LogInfo($"Enum field: {f.Name} {f.FieldType.Name}");
                    try
                    {
                        var values = f.FieldType.GetEnumValues();
                        foreach (var v in values)
                        {
                            if (v.ToString() == "Completed")
                            {
                                f.SetValue(directive, v);
                                Logger.LogInfo($"Set {f.Name} to Completed");
                                break;
                            }
                            else if (v.ToString() == "Claimed")
                            {
                                f.SetValue(directive, v);
                                Logger.LogInfo($"Set {f.Name} to Claimed");
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to set enum field {f.Name}: {ex.Message}");
                    }
                }

                var isCompleteBackingField = directive.GetType().GetField("<IsComplete>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                if (isCompleteBackingField != null)
                {
                    isCompleteBackingField.SetValue(directive, true);
                    Logger.LogInfo("Set <IsComplete>k__BackingField to true");
                }
                else
                {
                    var isCompleteProperty = directive.GetType().GetProperty("IsComplete", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                    if (isCompleteProperty != null && isCompleteProperty.CanWrite)
                    {
                        isCompleteProperty.SetValue(directive, true);
                        Logger.LogInfo("Set IsComplete to true");
                    }
                    else
                    {
                        Logger.LogInfo("IsComplete cannot be set");
                    }
                }
                var isActiveField = directive.GetType().GetField("isActive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                if (isActiveField != null)
                {
                    isActiveField.SetValue(directive, true);
                    Logger.LogInfo("Set directive isActive to True");
                }

                var hasClaimedField = directive.GetType().GetField("hasClaimedRewards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                if (hasClaimedField != null)
                {
                    hasClaimedField.SetValue(directive, false);
                    Logger.LogInfo("Set directive hasClaimedRewards to False");
                }

                var propertiesField = directive.GetType().GetField("properties", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                if (propertiesField != null)
                {
                    var propertiesList = propertiesField.GetValue(directive) as System.Collections.IList;
                    if (propertiesList != null)
                    {
                        for (int i = 0; i < propertiesList.Count; i++)
                        {
                            var prop = propertiesList[i];
                            if (prop != null)
                            {
                                Logger.LogInfo($"Property {i} type: {prop.GetType().FullName}");
                                var propFields = prop.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                                foreach (var f in propFields)
                                {
                                    Logger.LogInfo($"Property {i} field: {f.Name} {f.FieldType.Name}");
                                }
                                var propProps = prop.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                                foreach (var p in propProps)
                                {
                                    Logger.LogInfo($"Property {i} property: {p.Name} {p.PropertyType.Name}");
                                }
                                var propMethods = prop.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                                foreach (var m in propMethods.Where(m => m.Name.Contains("Complete") || m.Name.Contains("Progress") || m.Name.Contains("Value") || m.Name.Contains("Update") || m.Name.Contains("Is") || m.IsPublic))
                                {
                                    Logger.LogInfo($"Property {i} method: {m.Name} {m.ReturnType.Name}");
                                }
                            }

                            var progressField = prop.GetType().GetField("progress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                            if (progressField != null)
                            {
                                if (progressField.FieldType == typeof(float))
                                    progressField.SetValue(prop, 1.0f);
                                else if (progressField.FieldType == typeof(int))
                                    progressField.SetValue(prop, 100);
                                Logger.LogInfo($"Set progress to max for objective {i}");
                            }
                            else
                            {
                                var currentProgress = prop.GetType().GetField("currentProgress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
                                if (currentProgress != null)
                                {
                                    if (currentProgress.FieldType == typeof(float))
                                        currentProgress.SetValue(prop, 1.0f);
                                    else if (currentProgress.FieldType == typeof(int))
                                        currentProgress.SetValue(prop, 100);
                                    Logger.LogInfo($"Set currentProgress to max for objective {i}");
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Logger.LogError($"Error setting directive status: {e.Message}");
            }

            var claimRewardsMethod = typeof(DirectiveButton).GetMethod("ClaimRewards", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (claimRewardsMethod == null)
            {
                Logger.LogError("ClaimRewards method not found on DirectiveButton");
                return;
            }
            Logger.LogInfo("F2: Deactivating directive before claiming");
            var deactivateMethod = directive.GetType().GetMethod("Deactivate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (deactivateMethod != null)
            {
                deactivateMethod.Invoke(directive, new object[] { });
                Logger.LogInfo("F2: Directive deactivated");
            }

            Logger.LogInfo("F2: Invoking ClaimRewards");
            claimRewardsMethod.Invoke(button, null);
            Logger.LogInfo("F2: ClaimRewards invoked");

            Logger.LogInfo("F2: Re-activating directive");
            var activateMethod = directive.GetType().GetMethod("Activate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (activateMethod != null)
            {
                activateMethod.Invoke(directive, new object[] { });
                Logger.LogInfo("F2: Directive re-activated");
            }

            Logger.LogInfo("F2: Final deactivate after claim");
            if (deactivateMethod != null)
            {
                deactivateMethod.Invoke(directive, new object[] { });
                Logger.LogInfo("F2: Directive final deactivated");
            }

            Logger.LogInfo("F2: Completed current directive");
        }
        catch (System.Exception e)
        {
            Logger.LogError($"Error completing current directive: {e.Message}");
        }
    }
}

[HarmonyPatch]
public static class PlayerDataPatches
{
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerData), "GetCurrrentDirectiveTier")]
        public static void GetCurrrentDirectiveTierPostfix(ref int __result)
        {
            SparrohPlugin.Logger.LogInfo($"GetCurrrentDirectiveTier called, original result: {__result}, PreselectMode: {SparrohPlugin.PreselectMode}");
            if (SparrohPlugin.PreselectMode)
            {
                __result = 99;
                SparrohPlugin.Logger.LogInfo("Forced return to 99");
            }
        }
}

[HarmonyPatch]
public static class DirectiveWindowPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DirectiveWindow), "SetupDirectives")]
    public static void SetupDirectivesPostfix(DirectiveWindow __instance, bool animate)
    {
        SparrohPlugin.Logger.LogInfo("SetupDirectivesPostfix running");
        int page = PlayerData.Instance.currentDirectivePage;

        var canvas = __instance.GetComponentInParent<Canvas>();
        var drawer = canvas.GetComponent<PathLineDrawer>();
        if (drawer == null) drawer = canvas.gameObject.AddComponent<PathLineDrawer>();
        drawer.Lines.Clear();

        if (SparrohPlugin.PreselectedPaths.TryGetValue(page, out var path))
        {
            var sortedPath = path.OrderBy(idx => PlayerData.Instance.defaultDirectives[idx].Tier).ToList();

            var directives = (DirectiveButton[])typeof(DirectiveWindow).GetField("directives", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(__instance);
            if (directives != null)
            {
                if (SparrohPlugin.PreselectMode)
                {
                    foreach (int idx in sortedPath)
                    {
                        if (idx < directives.Length)
                        {
                            var outline = directives[idx].GetComponent<PolygonOutline>();
                            if (outline != null)
                            {
                                outline.SetValue(12f);
                                outline.color = UnityEngine.Color.yellow;
                            }
                        }
                    }
                }

                for (int i = 0; i < sortedPath.Count - 1; i++)
                {
                    int idx1 = sortedPath[i];
                    int idx2 = sortedPath[i + 1];
                    if (idx1 < directives.Length && idx2 < directives.Length)
                    {
                        var pos1 = directives[idx1].transform.position;
                        var pos2 = directives[idx2].transform.position;
                        SparrohPlugin.Logger.LogInfo($"Canvas scale: {canvas.transform.lossyScale}, renderMode: {canvas.renderMode}, Creating line from {pos1} to {pos2}");
                        drawer.Lines.Add(new Vector3[]{ pos1, pos2 });
                    }
                }
            }
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(DirectiveWindow), "OnOpen")]
    public static void OnOpenPostfix(DirectiveWindow __instance)
    {
        try
        {
            var directives = PlayerData.Instance.defaultDirectives;
            if (directives.Length > 0)
            {
                var directiveType = directives[0].GetType();
                var canBeActivatedMethod = directiveType.GetMethod("CanBeActivated");
                if (canBeActivatedMethod != null)
                {
                    var harmony = new Harmony("sparroh.preselectbacklog.dynamic");
                    harmony.Patch(canBeActivatedMethod, new HarmonyMethod(typeof(SparrohPlugin).GetMethod("CanBeActivatedPrefix", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));
                    SparrohPlugin.Logger.LogInfo("Dynamically patched CanBeActivated");
                }
            }
        }
        catch (System.Exception e)
        {
            SparrohPlugin.Logger.LogError($"Failed to patch CanBeActivated: {e.Message}");
        }
    }


}



[HarmonyPatch]
public static class DirectivePatches
{
        [HarmonyPostfix]
        [HarmonyPatch(typeof(DirectiveButton), "ClaimRewards")]
        public static void OnClaimRewards(DirectiveButton __instance)
        {
            SparrohPlugin.Logger.LogInfo("OnClaimRewards called");

            var directiveField = typeof(DirectiveButton).GetField("directive",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (directiveField == null) return;
            var directive = directiveField.GetValue(__instance);
            if (directive == null) return;

            int page = PlayerData.Instance.currentDirectivePage;
            SparrohPlugin.Logger.LogInfo($"OnClaimRewards for page {page}");

            if (SparrohPlugin.PreselectedPaths.TryGetValue(page, out var path))
            {
                var directives = PlayerData.Instance.defaultDirectives;
                int currentIdx = -1;
                for (int i = 0; i < directives.Length; i++)
                {
                    if (directives[i] == directive)
                    {
                        currentIdx = i;
                        break;
                    }
                }

                SparrohPlugin.Logger.LogInfo($"Current directive idx: {currentIdx}, in path: {(path.Contains(currentIdx) ? "yes" : "no")}");

                if (currentIdx != -1 && path.Contains(currentIdx))
                {
                    int nextIdx = path.IndexOf(currentIdx) + 1;
                    if (nextIdx < path.Count)
                    {
                        int directiveIdx = path[nextIdx];
                        SparrohPlugin.Logger.LogInfo($"Next directive idx: {directiveIdx}");

                        if (directiveIdx < directives.Length)
                        {
                            var nextDirective = directives[directiveIdx];
                            SparrohPlugin.Logger.LogInfo($"Calling activate on next directive");

                            if (nextDirective.CanBeActivated())
                            {
                                nextDirective.Activate();
                                if (nextDirective.IsDefault())
                                {
                                    PlayerData.Instance.currentDefaultDirective = directiveIdx;
                                }

                                __instance.GetComponentInParent<DirectiveWindow>().SetupDirectives();
                                SparrohPlugin.Logger.LogInfo($"Auto-activated directive {directiveIdx}");
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DirectiveButton), "ActivateDirective")]
        public static bool ActivateDirectivePrefix(DirectiveButton __instance)
        {
            if (SparrohPlugin.PreselectMode)
            {
                SparrohPlugin.Logger.LogInfo("ActivateDirectivePrefix called in preselect mode");
                var directiveField = typeof(DirectiveButton).GetField("directive",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (directiveField == null) return true;
                var directive = directiveField.GetValue(__instance);
                if (directive == null) return true;

                SparrohPlugin.Logger.LogInfo($"Directive type in activate: {directive.GetType().FullName}");

                var tierProperty = directive.GetType().GetProperty("Tier");
                if (tierProperty == null)
                {
                    SparrohPlugin.Logger.LogError("Tier property not found on directive");
                    return false;
                }
                int directiveTier = (int)tierProperty.GetValue(directive);

                int page = PlayerData.Instance.currentDirectivePage;
                if (!SparrohPlugin.PreselectedPaths.ContainsKey(page))
                {
                    SparrohPlugin.PreselectedPaths[page] = new List<int>();
                }

                var path = SparrohPlugin.PreselectedPaths[page];

                List<int> toRemove = new List<int>();
                foreach (int existingIdx in path)
                {
                    var existingDir = PlayerData.Instance.defaultDirectives[existingIdx];
                    if (existingDir == null) continue;
                    var existingTierProperty = existingDir.GetType().GetProperty("Tier");
                    if (existingTierProperty == null)
                    {
                        SparrohPlugin.Logger.LogWarning($"Tier property not found on directive {existingIdx}");
                        continue;
                    }
                    int existingTier = (int)existingTierProperty.GetValue(existingDir);
                    if (existingTier == directiveTier)
                    {
                        toRemove.Add(existingIdx);
                    }
                }
                foreach (int idx in toRemove)
                {
                    path.Remove(idx);
                    SparrohPlugin.Logger.LogInfo($"Removed directive {idx} from path (same tier {directiveTier})");
                }

                var directives = PlayerData.Instance.defaultDirectives;
                for (int i = 0; i < directives.Length; i++)
                {
                    if (directives[i] == directive)
                    {
                        if (!path.Contains(i))
                        {
                            path.Add(i);
                            path = path.OrderBy(idx => {
                                var dir = PlayerData.Instance.defaultDirectives[idx];
                                if (dir == null) return 999;
                                var tierProp = dir.GetType().GetProperty("Tier");
                                if (tierProp == null) return 999;
                                return (int)tierProp.GetValue(dir);
                            }).ToList();
                            __instance.GetComponentInParent<DirectiveWindow>().SetupDirectives();
                            SparrohPlugin.Logger.LogInfo($"Added directive {i} (tier {directiveTier}) to preselected path for page {page}");
                        }

                        break;
                    }
                }

                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DirectiveButton), "Setup")]
        public static void DirectiveButtonSetupPostfix(DirectiveButton __instance, object directive, bool isAnyActive,
            bool isWaitingToClaim)
        {
            SparrohPlugin.Logger.LogInfo("DirectiveButtonSetupPostfix running");
            if (SparrohPlugin.PreselectMode)
            {
                var setupAvailableMethod = typeof(DirectiveButton).GetMethod("SetupAvailable",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (setupAvailableMethod != null)
                {
                    setupAvailableMethod.Invoke(__instance, null);
                }

                SparrohPlugin.Logger.LogInfo("Forced directive to available state");
            }
        }
    }
