using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

/// <summary>
/// Persistent storage for preselected directive paths, keyed by backlog page.
/// Format: one "page,index" line per selected directive (legacy-compatible).
/// Migrates from sparroh.preselectbacklog.txt on first load if needed.
/// </summary>
public static class PathStore
{
    private static readonly Dictionary<int, List<int>> PagePaths = new Dictionary<int, List<int>>();

    private static string FilePath =>
        Path.Combine(BepInEx.Paths.ConfigPath, $"{BacklogImprovementsPlugin.PluginGUID}.txt");

    private static string LegacyFilePath =>
        Path.Combine(BepInEx.Paths.ConfigPath, $"{BacklogImprovementsPlugin.LegacyPreselectGuid}.txt");

    public static IReadOnlyDictionary<int, List<int>> All => PagePaths;

    public static void Load()
    {
        PagePaths.Clear();
        try
        {
            string path = FilePath;
            if (!File.Exists(path) && File.Exists(LegacyFilePath))
            {
                path = LegacyFilePath;
                BacklogImprovementsPlugin.Log?.LogInfo(
                    "Migrating preselected paths from legacy PreselectBacklog config.");
            }

            if (!File.Exists(path))
                return;

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length != 2)
                    continue;
                if (!int.TryParse(parts[0].Trim(), out int page))
                    continue;
                if (!int.TryParse(parts[1].Trim(), out int idx))
                    continue;

                if (!PagePaths.TryGetValue(page, out var list))
                {
                    list = new List<int>();
                    PagePaths[page] = list;
                }

                if (!list.Contains(idx))
                    list.Add(idx);
            }

            foreach (var kvp in PagePaths.ToList())
                PagePaths[kvp.Key] = SortByTier(kvp.Value);

            // If we loaded from legacy, write the new file immediately.
            if (path == LegacyFilePath && PagePaths.Count > 0)
                Save();
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogError($"Failed to load preselected paths: {ex.Message}");
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(BepInEx.Paths.ConfigPath);
            var lines = new List<string>();
            foreach (var kvp in PagePaths.OrderBy(k => k.Key))
            {
                foreach (var idx in kvp.Value)
                    lines.Add($"{kvp.Key},{idx}");
            }
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogError($"Failed to save preselected paths: {ex.Message}");
        }
    }

    public static List<int> GetPath(int page)
    {
        if (!PagePaths.TryGetValue(page, out var list))
        {
            list = new List<int>();
            PagePaths[page] = list;
        }
        return list;
    }

    public static List<int> GetPathCopy(int page)
    {
        return new List<int>(GetPath(page));
    }

    public static bool HasPath(int page)
    {
        return PagePaths.TryGetValue(page, out var list) && list.Count > 0;
    }

    public static bool Contains(int page, int index)
    {
        return PagePaths.TryGetValue(page, out var list) && list.Contains(index);
    }

    public static void SetPath(int page, List<int> indices)
    {
        PagePaths[page] = SortByTier(indices ?? new List<int>());
        Save();
    }

    public static void ClearPage(int page)
    {
        if (PagePaths.ContainsKey(page))
        {
            PagePaths[page] = new List<int>();
            Save();
        }
    }

    /// <summary>
    /// Toggle membership for a directive index. Same-tier selections replace each other.
    /// Re-clicking the same index removes it.
    /// </summary>
    public static void ToggleSelection(int page, int index)
    {
        var path = GetPath(page);
        if (path.Contains(index))
        {
            path.Remove(index);
            Save();
            return;
        }

        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null || index < 0 || index >= data.defaultDirectives.Length)
            return;

        var selected = data.defaultDirectives[index];
        if (selected == null)
            return;

        int selectedTier = selected.Tier;
        path.RemoveAll(i =>
        {
            if (i < 0 || i >= data.defaultDirectives.Length)
                return true;
            var d = data.defaultDirectives[i];
            return d == null || d.Tier == selectedTier;
        });

        path.Add(index);
        PagePaths[page] = SortByTier(path);
        Save();
    }

    public static List<int> SortByTier(List<int> indices)
    {
        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null || indices == null)
            return indices ?? new List<int>();

        return indices
            .Where(i => i >= 0 && i < data.defaultDirectives.Length && data.defaultDirectives[i] != null)
            .Distinct()
            .OrderBy(i => data.defaultDirectives[i].Tier)
            .ThenBy(i => i)
            .ToList();
    }

    public static int CurrentPage
    {
        get
        {
            try
            {
                return PlayerData.Instance?.currentDirectivePage ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
