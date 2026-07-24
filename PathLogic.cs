using System;
using System.Reflection;
using HarmonyLib;

/// <summary>
/// Core preselect rules: next-node activation, auto-claim, and debug complete.
/// </summary>
public static class PathLogic
{
    private static readonly FieldInfo IsActiveField =
        AccessTools.Field(typeof(DirectiveInstance), "isActive");
    private static readonly FieldInfo HasClaimedField =
        AccessTools.Field(typeof(DirectiveInstance), "hasClaimedRewards");
    private static readonly FieldInfo PropertiesField =
        AccessTools.Field(typeof(DirectiveInstance), "properties");

    private static bool _autoClaimBusy;
    private static bool _activateBusy;

    public static bool IsEditing { get; private set; }

    public static void SetEditing(bool editing)
    {
        IsEditing = editing;
    }

    public static int IndexOf(DirectiveInstance directive)
    {
        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null || directive == null)
            return -1;
        return Array.IndexOf(data.defaultDirectives, directive);
    }

    public static bool IsOnCurrentPath(int index)
    {
        return PathStore.Contains(PathStore.CurrentPage, index);
    }

    public static bool IsOnCurrentPath(DirectiveInstance directive)
    {
        int idx = IndexOf(directive);
        return idx >= 0 && IsOnCurrentPath(idx);
    }

    /// <summary>
    /// First path node that is not complete (or is complete but unclaimed).
    /// Used for auto-activate after claim / when finishing edit.
    /// </summary>
    public static int GetNextPathIndex()
    {
        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null)
            return -1;

        var path = PathStore.GetPath(PathStore.CurrentPage);
        for (int i = 0; i < path.Count; i++)
        {
            int idx = path[i];
            if (idx < 0 || idx >= data.defaultDirectives.Length)
                continue;

            var d = data.defaultDirectives[idx];
            if (d == null)
                continue;

            // Skip fully finished nodes.
            if (d.IsComplete && d.HasClaimedRewards)
                continue;

            return idx;
        }

        return -1;
    }

    public static bool TryActivateNext(DirectiveWindow window = null)
    {
        if (_activateBusy || IsEditing)
            return false;
        if (BacklogImprovementsPlugin.EnablePreselect?.Value == false)
            return false;

        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null)
            return false;

        // Don't interrupt an active or claimable directive.
        if (HasBlockingDirective(data))
            return false;

        int next = GetNextPathIndex();
        if (next < 0)
            return false;

        var directive = data.defaultDirectives[next];
        if (directive == null || !directive.CanBeActivated())
            return false;

        _activateBusy = true;
        try
        {
            directive.Activate();
            if (directive.IsDefault())
                data.currentDefaultDirective = next;

            window?.SetupDirectives();
            BacklogImprovementsPlugin.Log?.LogInfo($"Activated path node {next} (tier {directive.Tier}).");
            return true;
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogError($"Failed to activate path node {next}: {ex.Message}");
            return false;
        }
        finally
        {
            _activateBusy = false;
        }
    }

    public static bool HasBlockingDirective(PlayerData data = null)
    {
        data = data ?? PlayerData.Instance;
        if (data?.defaultDirectives == null)
            return false;

        for (int i = 0; i < data.defaultDirectives.Length; i++)
        {
            var d = data.defaultDirectives[i];
            if (d == null)
                continue;
            if (d.IsActive)
                return true;
            if (d.IsComplete && !d.HasClaimedRewards)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Auto-claim the current default directive if it is complete, unclaimed, and on the path.
    /// </summary>
    public static bool TryAutoClaim(DirectiveWindow window = null)
    {
        if (IsEditing || _autoClaimBusy)
            return false;
        if (BacklogImprovementsPlugin.EnablePreselect?.Value == false)
            return false;

        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null)
            return false;

        int current = data.currentDefaultDirective;
        if (current < 0 || current >= data.defaultDirectives.Length)
            return false;

        if (!IsOnCurrentPath(current))
            return false;

        var directive = data.defaultDirectives[current];
        if (directive == null || !directive.CanClaimRewards())
            return false;

        _autoClaimBusy = true;
        try
        {
            directive.ClaimRewards();
            // Claim postfix activates the next path node.
            window?.SetupDirectives();
            BacklogImprovementsPlugin.Log?.LogInfo($"Auto-claimed path node {current}.");
            return true;
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogError($"Auto-claim failed: {ex.Message}");
            return false;
        }
        finally
        {
            _autoClaimBusy = false;
        }
    }

    /// <summary>
    /// Claim any claimable path node, then activate the next path node if nothing is blocking.
    /// Safe to call with the backlog closed (window may be null).
    /// </summary>
    public static bool TryAutoProgress(DirectiveWindow window = null)
    {
        if (IsEditing)
            return false;
        if (BacklogImprovementsPlugin.EnablePreselect?.Value == false)
            return false;

        bool claimed = TryAutoClaim(window);
        // Claim postfix already tries activate; still recover if claim was a no-op
        // (e.g. already claimed earlier but next node never activated).
        bool activated = false;
        if (!HasBlockingDirective())
            activated = TryActivateNext(window);

        return claimed || activated;
    }

    /// <summary>
    /// When a path directive finishes (Complete → Deactivate), claim rewards and
    /// activate the next path node without requiring the backlog menu to be open.
    /// </summary>
    public static void OnDirectiveCompleted(DirectiveInstance completed, DirectiveWindow window = null)
    {
        if (IsEditing || completed == null)
            return;
        if (BacklogImprovementsPlugin.EnablePreselect?.Value == false)
            return;

        if (!IsOnCurrentPath(completed))
            return;

        if (window == null)
            window = UnityEngine.Object.FindObjectOfType<DirectiveWindow>();

        if (completed.CanClaimRewards())
        {
            if (_autoClaimBusy)
                return;

            _autoClaimBusy = true;
            try
            {
                int idx = IndexOf(completed);
                completed.ClaimRewards();
                // Claim postfix activates the next path node.
                window?.SetupDirectives();
                BacklogImprovementsPlugin.Log?.LogInfo(
                    $"Auto-claimed completed path node {(idx >= 0 ? idx.ToString() : "?")}.");
            }
            catch (Exception ex)
            {
                BacklogImprovementsPlugin.Log?.LogError($"Auto-claim on complete failed: {ex.Message}");
            }
            finally
            {
                _autoClaimBusy = false;
            }
            return;
        }

        if (!HasBlockingDirective())
            TryActivateNext(window);
    }

    /// <summary>
    /// After a claim, if the claimed directive was on the path, activate the next path node.
    /// </summary>
    public static void OnDirectiveClaimed(DirectiveInstance claimed, DirectiveWindow window = null)
    {
        if (IsEditing || claimed == null)
            return;
        if (BacklogImprovementsPlugin.EnablePreselect?.Value == false)
            return;

        if (!IsOnCurrentPath(claimed))
            return;

        TryActivateNext(window);
    }


    /// <summary>
    /// While editing, any non-active non-complete default directive can be selected.
    /// While running, only the next path node may bypass normal activation gates
    /// (still requires no other active/claimable directive and matching tier via vanilla).
    /// </summary>
    public static bool ShouldAllowActivation(DirectiveInstance directive, out bool forceTrue)
    {
        forceTrue = false;
        if (directive == null)
            return true; // let vanilla handle null

        if (IsEditing)
        {
            // Selection mode never actually activates.
            forceTrue = false;
            return false;
        }

        // Running mode: only force-allow the next path node when vanilla would block
        // solely due to tier / path planning. We still respect active/claimable blocks.
        int idx = IndexOf(directive);
        if (idx < 0 || !IsOnCurrentPath(idx))
            return true; // vanilla

        int next = GetNextPathIndex();
        if (idx != next)
            return true; // not the next node — vanilla

        // Next path node: allow if nothing else is active/claimable and not already done.
        if (directive.IsActive || directive.IsComplete)
            return true;

        var data = PlayerData.Instance;
        if (data == null)
            return true;

        for (int i = 0; i < data.defaultDirectives.Length; i++)
        {
            var d = data.defaultDirectives[i];
            if (d == null || d == directive)
                continue;
            if (d.IsActive || (d.IsComplete && !d.HasClaimedRewards))
                return true; // blocked by another — vanilla
        }

        // Force allow next path node even if tier gate would fail (path was planned ahead).
        forceTrue = true;
        return false; // skip vanilla, use forceTrue
    }

    /// <summary>
    /// Debug: force-complete the currently active default directive (for testing).
    /// </summary>
    public static bool TryForceCompleteCurrent()
    {
        var data = PlayerData.Instance;
        if (data?.defaultDirectives == null)
            return false;

        int current = data.currentDefaultDirective;
        if (current < 0 || current >= data.defaultDirectives.Length)
        {
            // Fall back to any active directive.
            current = -1;
            for (int i = 0; i < data.defaultDirectives.Length; i++)
            {
                if (data.defaultDirectives[i] != null && data.defaultDirectives[i].IsActive)
                {
                    current = i;
                    break;
                }
            }
        }

        if (current < 0)
        {
            BacklogImprovementsPlugin.Log?.LogWarning("Force-complete: no active directive.");
            return false;
        }

        var directive = data.defaultDirectives[current];
        if (directive == null)
            return false;

        try
        {
            // Prefer the public API when active.
            if (directive.IsActive)
            {
                directive.CompleteAllProperties();
            }
            else
            {
                // Manually complete properties if not active (edge case).
                ForceCompleteProperties(directive);
            }

            // Ensure claimable state: CompleteAllProperties deactivates when all props done.
            // If still not complete, force property currents.
            if (!directive.IsComplete)
                ForceCompleteProperties(directive);

            BacklogImprovementsPlugin.Log?.LogInfo($"Force-completed directive index {current}.");

            var window = UnityEngine.Object.FindObjectOfType<DirectiveWindow>();
            window?.SetupDirectives();

            // Auto-claim if on path (claim postfix activates next).
            if (IsOnCurrentPath(current) && directive.CanClaimRewards())
            {
                directive.ClaimRewards();
                window?.SetupDirectives();
            }

            return true;
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogError($"Force-complete failed: {ex.Message}");
            return false;
        }
    }

    private static void ForceCompleteProperties(DirectiveInstance directive)
    {
        if (directive == null || PropertiesField == null)
            return;

        var props = PropertiesField.GetValue(directive) as System.Collections.IList;
        if (props == null)
            return;

        for (int i = 0; i < props.Count; i++)
        {
            var prop = props[i] as DirectiveInstance.DirectivePropertyInstance;
            if (prop == null)
                continue;
            prop.Current = prop.Target;
        }

        // Deactivate so IsComplete can return true (IsComplete is false while active).
        if (directive.IsActive)
            directive.Deactivate();
    }
}
