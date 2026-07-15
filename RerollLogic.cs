using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Core backlog (directive) reroll rules and mutations.
/// Eligible = not active and not complete.
/// </summary>
public static class RerollLogic
{
    public static int CostPerDirective =>
        Mathf.Max(0, BacklogImprovementsPlugin.CostPerDirective?.Value ?? 50);

    public static bool IsEligible(DirectiveInstance directive)
    {
        if (directive == null)
            return false;
        return !directive.IsActive && !directive.IsComplete;
    }

    public static int CountEligible()
    {
        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null)
            return 0;

        int count = 0;
        for (int i = 0; i < data.defaultDirectives.Length; i++)
        {
            if (IsEligible(data.defaultDirectives[i]))
                count++;
        }
        return count;
    }

    public static int GetPageRerollCost() => CountEligible() * CostPerDirective;

    public static int GetSingleRerollCost() => CostPerDirective;

    public static bool CanAfford(int cost)
    {
        if (cost <= 0)
            return true;
        var data = PlayerData.Instance;
        var scrip = Global.Instance?.ScripResource;
        if (data == null || scrip == null)
            return false;
        return data.CanAffordCost(scrip, cost);
    }

    public static bool TryCharge(int cost)
    {
        if (cost <= 0)
            return true;
        var data = PlayerData.Instance;
        var scrip = Global.Instance?.ScripResource;
        if (data == null || scrip == null)
            return false;
        return data.TryRemoveResource(scrip, cost);
    }

    /// <summary>
    /// Reroll a single slot by index. Charges cost first.
    /// </summary>
    public static bool TryRerollSingle(int index, out string error)
    {
        error = null;
        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null)
        {
            error = "Player data not ready.";
            return false;
        }

        if (index < 0 || index >= data.defaultDirectives.Length)
        {
            error = "Invalid directive.";
            return false;
        }

        var current = data.defaultDirectives[index];
        if (!IsEligible(current))
        {
            error = "That directive cannot be rerolled (active or already complete).";
            return false;
        }

        int cost = GetSingleRerollCost();
        if (!CanAfford(cost))
        {
            error = $"Not enough gats (need {cost}).";
            return false;
        }

        if (!TryCharge(cost))
        {
            error = $"Not enough gats (need {cost}).";
            return false;
        }

        ReplaceAt(data, index);
        return true;
    }

    /// <summary>
    /// Reroll all eligible slots on the current page. Charges cost first.
    /// </summary>
    public static bool TryRerollPage(out int rerolledCount, out string error)
    {
        rerolledCount = 0;
        error = null;
        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null)
        {
            error = "Player data not ready.";
            return false;
        }

        var indices = new List<int>();
        for (int i = 0; i < data.defaultDirectives.Length; i++)
        {
            if (IsEligible(data.defaultDirectives[i]))
                indices.Add(i);
        }

        if (indices.Count == 0)
        {
            error = "No eligible directives to reroll.";
            return false;
        }

        int cost = indices.Count * CostPerDirective;
        if (!CanAfford(cost))
        {
            error = $"Not enough gats (need {cost}).";
            return false;
        }

        if (!TryCharge(cost))
        {
            error = $"Not enough gats (need {cost}).";
            return false;
        }

        for (int i = 0; i < indices.Count; i++)
            ReplaceAt(data, indices[i]);

        rerolledCount = indices.Count;
        return true;
    }

    private static void ReplaceAt(PlayerData data, int index)
    {
        int tier = data.GetTierFromDirectiveIndex(index);
        try
        {
            data.defaultDirectives[index]?.Destroy();
        }
        catch (System.Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogWarning($"Destroy directive {index}: {ex.Message}");
        }

        data.defaultDirectives[index] = data.CreateDirective(tier);
    }

    public static int IndexOf(DirectiveInstance directive)
    {
        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null || directive == null)
            return -1;
        return System.Array.IndexOf(data.defaultDirectives, directive);
    }
}
