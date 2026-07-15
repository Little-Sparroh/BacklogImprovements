using System;
using HarmonyLib;

/// <summary>
/// Removes the resource cost for generating the next backlog page.
/// </summary>
public static class FreePages
{
    public static void ApplyFreeNextPage(DirectiveWindow window)
    {
        if (window == null)
            return;
        if (BacklogImprovementsPlugin.EnableFreePages?.Value == false)
            return;

        try
        {
            var nextPageButton = AccessTools.Field(typeof(DirectiveWindow), "nextPageButton")
                .GetValue(window) as HoverInfoHold;
            if (nextPageButton == null)
                return;

            AccessTools.Field(typeof(HoverInfoHold), "cost")
                .SetValue(nextPageButton, Array.Empty<ResourceCost>());
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogWarning($"Free next page failed: {ex.Message}");
        }
    }
}
