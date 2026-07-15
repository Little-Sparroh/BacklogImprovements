using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pigeon.Movement;
using Sparroh.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Unified SparrohUILib toolbar for preselect path + reroll controls.
/// Single bottom bar; path-edit and reroll-select modes are mutually exclusive.
/// </summary>
public static class BacklogUI
{
    private static readonly FieldInfo DirectivesField =
        AccessTools.Field(typeof(DirectiveWindow), "directives");
    private static readonly FieldInfo IsAnimatingField =
        AccessTools.Field(typeof(DirectiveWindow), "isAnimatingPage");
    private static readonly FieldInfo DirectiveField =
        AccessTools.Field(typeof(DirectiveButton), "directive");

    private static readonly Dictionary<int, WindowState> States = new Dictionary<int, WindowState>();

    // Parent is DirectiveWindow (game canvas already scales). Use raw reference pixels.
    private const float BarLeftAnchor = 0.12f;
    private const float CompactFontSize = 13f;
    private const float MainButtonMinWidth = 120f;
    private const float ButtonHeight = 32f;
    private const float BarWidth = 720f;
    private const float BarHeight = 40f;
    private const float BarBottomPad = 18f;
    private const float ButtonGap = 4f;
    private const float LabelPad = 8f;
    private const float TextWidthPad = 24f;

    private sealed class WindowState
    {
        public DirectiveWindow Window;
        public GameObject Root;

        // Preselect
        public UIButton EditButton;
        public UIButton ClearButton;

        // Reroll
        public UIButton PageButton;
        public UIButton SingleButton;
        public UIButton CancelButton;

        public bool RerollSelecting;
        public readonly List<GameObject> RerollOverlays = new List<GameObject>();
    }

    public static bool IsPathEditing => PathLogic.IsEditing;

    public static bool IsRerollSelecting
    {
        get
        {
            foreach (var kv in States)
            {
                if (kv.Value.RerollSelecting)
                    return true;
            }
            return false;
        }
    }

    public static bool IsAnySelectionMode => IsPathEditing || IsRerollSelecting;

    public static void OnWindowOpened(DirectiveWindow window)
    {
        if (window == null)
            return;

        FreePages.ApplyFreeNextPage(window);

        bool preselect = BacklogImprovementsPlugin.EnablePreselect?.Value != false;
        bool reroll = BacklogImprovementsPlugin.EnableReroll?.Value != false;
        if (!preselect && !reroll)
            return;

        int id = window.GetInstanceID();
        if (States.TryGetValue(id, out var existing) && existing.Root != null)
        {
            existing.Window = window;
            ExitPathEdit(existing, activateNext: false, refreshWindow: false);
            ExitRerollSelection(existing);
            RefreshAll(existing);
            if (preselect)
                PathVisualizer.Refresh(window);
            return;
        }

        var state = BuildUi(window, preselect, reroll);
        if (state == null)
            return;

        States[id] = state;
        RefreshAll(state);
        if (preselect)
            PathVisualizer.Refresh(window);
    }

    public static void OnWindowClosed(DirectiveWindow window)
    {
        if (window == null)
            return;

        int id = window.GetInstanceID();
        if (!States.TryGetValue(id, out var state))
            return;

        if (PathLogic.IsEditing)
            PathLogic.SetEditing(false);

        PathVisualizer.Clear(window);
        DestroyState(state);
        States.Remove(id);
    }

    public static void OnDirectivesRefreshed(DirectiveWindow window)
    {
        if (window == null)
            return;

        FreePages.ApplyFreeNextPage(window);

        if (!States.TryGetValue(window.GetInstanceID(), out var state))
            return;

        state.Window = window;
        RefreshAll(state);

        if (BacklogImprovementsPlugin.EnablePreselect?.Value != false)
        {
            PathVisualizer.Refresh(window);
            if (!PathLogic.IsEditing)
                PathLogic.TryAutoClaim(window);
        }

        if (state.RerollSelecting)
            RebuildRerollOverlays(state);
    }

    public static void CleanupAll()
    {
        PathLogic.SetEditing(false);
        foreach (var kv in States)
            DestroyState(kv.Value);
        States.Clear();
        PathVisualizer.CleanupAll();
    }

    /// <summary>
    /// Handle a directive click while in path-edit or reroll-select mode.
    /// Returns true if the click was consumed.
    /// </summary>
    public static bool TryHandleDirectiveClick(DirectiveButton button)
    {
        if (button == null)
            return false;

        if (PathLogic.IsEditing)
            return TryHandlePathClick(button);

        if (IsRerollSelecting)
            return TryHandleRerollClick(button);

        return false;
    }

    private static bool TryHandlePathClick(DirectiveButton button)
    {
        WindowState state = FindStateForButton(button);
        if (state == null)
            return false;

        if (IsAnimating(state.Window))
            return true;

        var directive = DirectiveField?.GetValue(button) as DirectiveInstance;
        int index = PathLogic.IndexOf(directive);
        if (index < 0)
            return true;

        if (directive != null && directive.IsComplete && directive.HasClaimedRewards)
        {
            UIDialog.Alert("Cannot Select", "That directive is already finished.");
            return true;
        }

        PathStore.ToggleSelection(PathStore.CurrentPage, index);
        RefreshAll(state);
        PathVisualizer.Refresh(state.Window);
        state.Window?.SetupDirectives(false);
        return true;
    }

    private static bool TryHandleRerollClick(DirectiveButton button)
    {
        WindowState state = FindStateForButton(button);
        if (state == null || !state.RerollSelecting)
            return false;

        if (IsAnimating(state.Window))
            return true;

        var directive = DirectiveField?.GetValue(button) as DirectiveInstance;
        int index = RerollLogic.IndexOf(directive);
        if (index < 0 || !RerollLogic.IsEligible(directive))
        {
            UIDialog.Alert("Cannot Reroll", "Only not-started directives can be rerolled.");
            return true;
        }

        int cost = RerollLogic.GetSingleRerollCost();
        string title = directive.GetTitle();
        if (string.IsNullOrEmpty(title))
            title = "this directive";

        UIDialog.Confirm(
            "Reroll Directive",
            $"Reroll {title} for {cost} gats?",
            () =>
            {
                if (RerollLogic.TryRerollSingle(index, out var error))
                {
                    ExitRerollSelection(state);
                    state.Window?.SetupDirectives();
                    RefreshAll(state);
                    PlayRerollSound();
                }
                else
                {
                    UIDialog.Alert("Reroll Failed", error ?? "Unknown error.");
                }
            },
            onCancel: null,
            confirmText: $"Pay {cost}",
            cancelText: "Cancel");

        return true;
    }

    private static WindowState FindStateForButton(DirectiveButton button)
    {
        var window = button.GetComponentInParent<DirectiveWindow>();
        if (window != null && States.TryGetValue(window.GetInstanceID(), out var byWindow))
            return byWindow;

        foreach (var kv in States)
            return kv.Value;
        return null;
    }

    private static WindowState BuildUi(DirectiveWindow window, bool preselect, bool reroll)
    {
        try
        {
            var parent = window.transform as RectTransform;
            if (parent == null)
                return null;

            UITheme.Initialize();

            var rootRt = UIFactory.CreateRect("BacklogImprovementsBar", parent);
            rootRt.anchorMin = new Vector2(BarLeftAnchor, 0f);
            rootRt.anchorMax = new Vector2(BarLeftAnchor, 0f);
            rootRt.pivot = new Vector2(0f, 0f);
            rootRt.sizeDelta = new Vector2(BarWidth, BarHeight);
            rootRt.anchoredPosition = new Vector2(0f, BarBottomPad);
            rootRt.SetAsLastSibling();

            UIFactory.AddHorizontalLayout(
                rootRt.gameObject,
                ButtonGap,
                new RectOffset(0, 0, 0, 0),
                TextAnchor.MiddleLeft,
                controlChildWidth: false,
                expandWidth: false,
                controlChildHeight: true,
                expandHeight: true);

            var state = new WindowState
            {
                Window = window,
                Root = rootRt.gameObject
            };

            if (preselect)
            {
                state.EditButton = UIButton.Create(
                    rootRt,
                    "Edit Path",
                    () => OnEditClicked(state),
                    UIButtonStyle.Primary,
                    preferredHeight: ButtonHeight);
                ApplyCompactLabel(state.EditButton);
                FitButtonWidth(state.EditButton, MainButtonMinWidth);

                state.ClearButton = UIButton.Create(
                    rootRt,
                    "Clear",
                    () => OnClearClicked(state),
                    UIButtonStyle.Danger,
                    preferredHeight: ButtonHeight);
                ApplyCompactLabel(state.ClearButton);
                FitButtonWidth(state.ClearButton, 80f);
            }

            if (reroll)
            {
                state.PageButton = UIButton.Create(
                    rootRt,
                    "Reroll Page",
                    () => OnRerollPageClicked(state),
                    UIButtonStyle.Primary,
                    preferredHeight: ButtonHeight);
                ApplyCompactLabel(state.PageButton);
                FitButtonWidth(state.PageButton, MainButtonMinWidth);

                state.SingleButton = UIButton.Create(
                    rootRt,
                    "Reroll One",
                    () => OnRerollOneClicked(state),
                    UIButtonStyle.Default,
                    preferredHeight: ButtonHeight);
                ApplyCompactLabel(state.SingleButton);
                FitButtonWidth(state.SingleButton, MainButtonMinWidth);

                state.CancelButton = UIButton.Create(
                    rootRt,
                    "Cancel",
                    () => ExitRerollSelection(state),
                    UIButtonStyle.Danger,
                    preferredHeight: ButtonHeight);
                ApplyCompactLabel(state.CancelButton);
                state.CancelButton.SetWidth(90f);
                state.CancelButton.SetActive(false);
            }

            return state;
        }
        catch (Exception ex)
        {
            BacklogImprovementsPlugin.Log?.LogError($"Failed to build backlog UI: {ex}");
            return null;
        }
    }

    // ── Preselect handlers ──────────────────────────────────────────────

    private static void OnEditClicked(WindowState state)
    {
        if (state?.Window == null)
            return;
        if (IsAnimating(state.Window))
            return;

        if (PathLogic.IsEditing)
        {
            ExitPathEdit(state, activateNext: true, refreshWindow: true);
        }
        else
        {
            // Mutual exclusion: leave reroll select first.
            ExitRerollSelection(state);
            EnterPathEdit(state);
        }
    }

    private static void OnClearClicked(WindowState state)
    {
        if (state?.Window == null)
            return;
        if (IsAnimating(state.Window))
            return;

        int page = PathStore.CurrentPage;
        if (!PathStore.HasPath(page))
        {
            UIDialog.Alert("Nothing to Clear", "No preselected path on this page.");
            return;
        }

        UIDialog.Confirm(
            "Clear Path",
            "Remove the preselected path for this backlog page?",
            () =>
            {
                PathStore.ClearPage(page);
                RefreshAll(state);
                PathVisualizer.Refresh(state.Window);
                state.Window?.SetupDirectives(false);
            },
            confirmText: "Clear",
            cancelText: "Cancel");
    }

    private static void EnterPathEdit(WindowState state)
    {
        PathLogic.SetEditing(true);
        RefreshAll(state);
        state.Window?.SetupDirectives(false);
        PathVisualizer.Refresh(state.Window);
    }

    private static void ExitPathEdit(WindowState state, bool activateNext, bool refreshWindow)
    {
        if (!PathLogic.IsEditing)
            return;

        PathLogic.SetEditing(false);
        PathStore.Save();
        RefreshAll(state);

        if (refreshWindow)
            state.Window?.SetupDirectives(false);

        PathVisualizer.Refresh(state.Window);

        if (activateNext && !PathLogic.HasBlockingDirective())
            PathLogic.TryActivateNext(state.Window);
    }

    // ── Reroll handlers ─────────────────────────────────────────────────

    private static void OnRerollPageClicked(WindowState state)
    {
        if (state?.Window == null)
            return;
        if (IsAnimating(state.Window))
            return;

        ExitRerollSelection(state);
        ExitPathEdit(state, activateNext: false, refreshWindow: true);

        int eligible = RerollLogic.CountEligible();
        if (eligible <= 0)
        {
            UIDialog.Alert("Nothing to Reroll", "There are no not-started directives on this page.");
            return;
        }

        int cost = RerollLogic.GetPageRerollCost();
        if (!RerollLogic.CanAfford(cost))
        {
            UIDialog.Alert("Not Enough Gats", $"Need {cost} gats to reroll {eligible} directive(s).");
            return;
        }

        UIDialog.Confirm(
            "Reroll Page",
            $"Reroll {eligible} not-started directive(s) for {cost} gats?\nActive and completed directives are kept.",
            () =>
            {
                if (RerollLogic.TryRerollPage(out var count, out var error))
                {
                    state.Window?.SetupDirectives();
                    RefreshAll(state);
                    PlayRerollSound();
                    BacklogImprovementsPlugin.Log?.LogInfo($"Rerolled {count} backlog directive(s) for {cost} gats.");
                }
                else
                {
                    UIDialog.Alert("Reroll Failed", error ?? "Unknown error.");
                }
            },
            confirmText: $"Pay {cost}",
            cancelText: "Cancel");
    }

    private static void OnRerollOneClicked(WindowState state)
    {
        if (state?.Window == null)
            return;
        if (IsAnimating(state.Window))
            return;

        if (state.RerollSelecting)
        {
            ExitRerollSelection(state);
            return;
        }

        if (RerollLogic.CountEligible() <= 0)
        {
            UIDialog.Alert("Nothing to Reroll", "There are no not-started directives on this page.");
            return;
        }

        int cost = RerollLogic.GetSingleRerollCost();
        if (!RerollLogic.CanAfford(cost))
        {
            UIDialog.Alert("Not Enough Gats", $"Need {cost} gats to reroll one directive.");
            return;
        }

        // Mutual exclusion: leave path edit first.
        ExitPathEdit(state, activateNext: false, refreshWindow: true);
        EnterRerollSelection(state);
    }

    private static void EnterRerollSelection(WindowState state)
    {
        state.RerollSelecting = true;
        if (state.CancelButton != null)
            state.CancelButton.SetActive(true);
        if (state.SingleButton != null)
        {
            state.SingleButton.SetText("Click a directive…");
            FitButtonWidth(state.SingleButton, MainButtonMinWidth);
        }
        if (state.PageButton != null)
            state.PageButton.SetInteractable(false);
        if (state.EditButton != null)
            state.EditButton.SetInteractable(false);
        if (state.ClearButton != null)
            state.ClearButton.SetInteractable(false);

        RebuildRerollOverlays(state);
    }

    private static void ExitRerollSelection(WindowState state)
    {
        if (state == null || !state.RerollSelecting)
            return;

        state.RerollSelecting = false;
        ClearRerollOverlays(state);

        if (state.CancelButton != null)
            state.CancelButton.SetActive(false);
        if (state.PageButton != null)
            state.PageButton.SetInteractable(true);
        if (state.EditButton != null)
            state.EditButton.SetInteractable(true);

        RefreshAll(state);
    }

    private static void RebuildRerollOverlays(WindowState state)
    {
        ClearRerollOverlays(state);
        if (state?.Window == null || !state.RerollSelecting)
            return;

        var buttons = DirectivesField?.GetValue(state.Window) as DirectiveButton[];
        if (buttons == null)
            return;

        for (int i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            if (btn == null)
                continue;

            var directive = DirectiveField?.GetValue(btn) as DirectiveInstance;
            if (!RerollLogic.IsEligible(directive))
                continue;

            var overlay = CreateEligibleOverlay(btn.transform as RectTransform);
            if (overlay != null)
                state.RerollOverlays.Add(overlay);
        }
    }

    private static GameObject CreateEligibleOverlay(RectTransform target)
    {
        if (target == null)
            return null;

        var img = UIFactory.CreateImage(
            "RerollEligibleOverlay",
            target,
            UIColors.WithAlpha(UIColors.ButtonPrimary, 0.35f),
            raycast: false);
        UIFactory.ApplyWhiteSprite(img);
        UIHelpers.SetFillParent(img.rectTransform, UITheme.S(2f));
        img.rectTransform.SetAsLastSibling();
        return img.gameObject;
    }

    private static void ClearRerollOverlays(WindowState state)
    {
        if (state == null)
            return;
        for (int i = 0; i < state.RerollOverlays.Count; i++)
            UIHelpers.DestroySafe(state.RerollOverlays[i]);
        state.RerollOverlays.Clear();
    }

    // ── Shared refresh / helpers ────────────────────────────────────────

    private static void RefreshAll(WindowState state)
    {
        if (state == null)
            return;

        RefreshPathLabels(state);
        RefreshRerollLabels(state);
    }

    private static void RefreshPathLabels(WindowState state)
    {
        if (state.EditButton == null && state.ClearButton == null)
            return;

        int count = PathStore.GetPath(PathStore.CurrentPage).Count;
        bool editing = PathLogic.IsEditing;

        if (state.EditButton != null)
        {
            state.EditButton.SetText(editing ? "Done" : "Edit Path");
            state.EditButton.SetStyle(editing ? UIButtonStyle.Active : UIButtonStyle.Primary);
            FitButtonWidth(state.EditButton, MainButtonMinWidth);
            state.EditButton.SetInteractable(!state.RerollSelecting);
        }

        if (state.ClearButton != null)
        {
            state.ClearButton.SetInteractable((count > 0 || editing) && !state.RerollSelecting);
        }
    }

    private static void RefreshRerollLabels(WindowState state)
    {
        if (state.PageButton == null && state.SingleButton == null)
            return;

        if (state.RerollSelecting)
            return;

        int eligible = RerollLogic.CountEligible();
        int pageCost = eligible * RerollLogic.CostPerDirective;
        int singleCost = RerollLogic.CostPerDirective;
        bool pathEditing = PathLogic.IsEditing;

        if (state.PageButton != null)
        {
            state.PageButton.SetText(eligible > 0 ? $"Reroll Page ({pageCost})" : "Reroll Page");
            FitButtonWidth(state.PageButton, MainButtonMinWidth);
            state.PageButton.SetInteractable(
                !pathEditing && eligible > 0 && RerollLogic.CanAfford(pageCost));
        }

        if (state.SingleButton != null)
        {
            state.SingleButton.SetText(eligible > 0 ? $"Reroll One ({singleCost})" : "Reroll One");
            FitButtonWidth(state.SingleButton, MainButtonMinWidth);
            state.SingleButton.SetInteractable(
                !pathEditing && eligible > 0 && RerollLogic.CanAfford(singleCost));
        }
    }

    private static void ApplyCompactLabel(UIButton button)
    {
        if (button?.Label == null)
            return;

        var label = button.Label;
        label.enableAutoSizing = false;
        label.fontSize = CompactFontSize;
        label.fontSizeMin = CompactFontSize;
        label.fontSizeMax = CompactFontSize;
        label.enableWordWrapping = false;
        label.overflowMode = TMPro.TextOverflowModes.Overflow;
        UIHelpers.SetFillParent(label.rectTransform, LabelPad);
    }

    private static void FitButtonWidth(UIButton button, float minWidth)
    {
        if (button == null)
            return;

        ApplyCompactLabel(button);

        float width = minWidth;
        if (button.Label != null)
        {
            button.Label.ForceMeshUpdate();
            float textW = button.Label.preferredWidth + TextWidthPad;
            if (textW > width)
                width = textW;
        }

        button.SetWidth(width);
    }

    private static bool IsAnimating(DirectiveWindow window)
    {
        if (window == null || IsAnimatingField == null)
            return false;
        try
        {
            return (bool)IsAnimatingField.GetValue(window);
        }
        catch
        {
            return false;
        }
    }

    private static void DestroyState(WindowState state)
    {
        if (state == null)
            return;
        ClearRerollOverlays(state);
        UIHelpers.DestroySafe(state.Root);
        state.Root = null;
        state.EditButton = null;
        state.ClearButton = null;
        state.PageButton = null;
        state.SingleButton = null;
        state.CancelButton = null;
        state.RerollSelecting = false;
    }

    private static void PlayRerollSound()
    {
        try
        {
            if (PlayerLook.Instance != null)
                AkUnitySoundEngine.PostEvent("UI_Directive_Page_Complete2", PlayerLook.Instance.gameObject);
        }
        catch
        {
            // optional feedback
        }
    }

    public static void PlayPathSound()
    {
        try
        {
            if (PlayerLook.Instance != null)
                AkUnitySoundEngine.PostEvent("UI_Directive_Connect", PlayerLook.Instance.gameObject);
        }
        catch
        {
            // optional
        }
    }
}
