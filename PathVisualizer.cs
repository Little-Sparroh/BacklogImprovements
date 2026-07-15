using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pigeon.UI;
using Sparroh.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws planned-path lines and selection badges in UI space
/// (same coordinate model as vanilla DirectiveWindow.AddLine).
/// </summary>
public static class PathVisualizer
{
    private static readonly FieldInfo DirectivesField =
        AccessTools.Field(typeof(DirectiveWindow), "directives");
    private static readonly FieldInfo LinesField =
        AccessTools.Field(typeof(DirectiveWindow), "lines");
    private static readonly FieldInfo OutlineField =
        AccessTools.Field(typeof(DirectiveButton), "outline");

    private static readonly Dictionary<int, VisualState> States = new Dictionary<int, VisualState>();

    private const float LineWidth = 4f;
    private const float BadgeSize = 22f;

    private sealed class VisualState
    {
        public DirectiveWindow Window;
        public Transform LineParent;
        public readonly List<GameObject> Lines = new List<GameObject>();
        public readonly List<GameObject> Badges = new List<GameObject>();
        public readonly List<GameObject> Overlays = new List<GameObject>();
    }

    public static void Refresh(DirectiveWindow window)
    {
        if (window == null)
            return;
        if (BacklogImprovementsPlugin.EnablePreselect?.Value == false)
        {
            Clear(window);
            return;
        }

        int id = window.GetInstanceID();
        if (!States.TryGetValue(id, out var state))
        {
            state = new VisualState { Window = window };
            States[id] = state;
        }
        else
        {
            state.Window = window;
        }

        EnsureLineParent(state);
        ClearVisuals(state);

        var buttons = DirectivesField?.GetValue(window) as DirectiveButton[];
        if (buttons == null)
            return;

        var path = PathStore.GetPath(PathStore.CurrentPage);
        if (path == null || path.Count == 0)
            return;

        // Sorted path indices that still exist.
        var ordered = PathStore.SortByTier(new List<int>(path));

        for (int i = 0; i < ordered.Count; i++)
        {
            int idx = ordered[i];
            if (idx < 0 || idx >= buttons.Length || buttons[idx] == null)
                continue;

            CreateBadge(state, buttons[idx], i + 1);
            CreateOverlay(state, buttons[idx]);
            ApplyOutline(buttons[idx], selected: true);
        }

        for (int i = 0; i < ordered.Count - 1; i++)
        {
            int a = ordered[i];
            int b = ordered[i + 1];
            if (a < 0 || b < 0 || a >= buttons.Length || b >= buttons.Length)
                continue;
            if (buttons[a] == null || buttons[b] == null)
                continue;
            CreateLine(state, buttons[a], buttons[b]);
        }
    }

    public static void Clear(DirectiveWindow window)
    {
        if (window == null)
            return;
        int id = window.GetInstanceID();
        if (!States.TryGetValue(id, out var state))
            return;

        ClearVisuals(state);
        UIHelpers.DestroySafe(state.LineParent != null && state.LineParent.name == "PreselectPathLines"
            ? state.LineParent.gameObject
            : null);
        States.Remove(id);
    }

    public static void CleanupAll()
    {
        foreach (var kv in States)
            ClearVisuals(kv.Value);
        States.Clear();
    }

    private static void EnsureLineParent(VisualState state)
    {
        if (state.LineParent != null)
            return;

        // Prefer the same parent as vanilla RectGradient lines.
        try
        {
            var lines = LinesField?.GetValue(state.Window) as System.Collections.IList;
            if (lines != null && lines.Count > 0)
            {
                var first = lines[0] as Component;
                if (first != null)
                {
                    state.LineParent = first.transform.parent;
                    return;
                }
            }
        }
        catch
        {
            // fall through
        }

        // Fallback: create a dedicated container under the window.
        var parent = state.Window.transform as RectTransform;
        var root = UIFactory.CreateRect("PreselectPathLines", parent);
        UIHelpers.SetFillParent(root);
        root.SetAsFirstSibling();
        var img = root.gameObject.GetComponent<Image>();
        if (img != null)
            UnityEngine.Object.Destroy(img);
        // Ensure no raycast blocker
        var graphic = root.gameObject.GetComponent<Graphic>();
        if (graphic != null)
            graphic.raycastTarget = false;
        state.LineParent = root;
    }

    private static void CreateLine(VisualState state, DirectiveButton from, DirectiveButton to)
    {
        if (state.LineParent == null)
            return;

        var a = from.transform as RectTransform;
        var b = to.transform as RectTransform;
        var parent = state.LineParent as RectTransform;
        if (a == null || b == null || parent == null)
            return;

        // Convert button centers into the line parent's local space so we match
        // vanilla AddLine regardless of hierarchy quirks.
        Vector2 posA = parent.InverseTransformPoint(a.position);
        Vector2 posB = parent.InverseTransformPoint(b.position);
        Vector2 delta = posB - posA;
        float magnitude = delta.magnitude;
        if (magnitude < 1f)
            return;

        Vector2 dir = delta / magnitude;
        float halfH = a.rect.height * 0.5f;
        Vector2 start = posA + dir * halfH;
        float length = Mathf.Max(0f, magnitude - halfH * 2f);

        var img = UIFactory.CreateImage(
            "PreselectPathLine",
            state.LineParent,
            UIColors.WithAlpha(UIColors.Macaroon, 0.95f),
            raycast: false);
        UIFactory.ApplyWhiteSprite(img);

        var rt = img.rectTransform;
        // Match vanilla: pivot at bottom center, rotate from up toward direction.
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(LineWidth, length);
        rt.anchoredPosition = start;
        rt.localRotation = Quaternion.FromToRotation(Vector2.up, dir);
        rt.localScale = Vector3.one;
        rt.SetAsFirstSibling();

        state.Lines.Add(img.gameObject);
    }

    private static void CreateBadge(VisualState state, DirectiveButton button, int order)
    {
        var target = button.transform as RectTransform;
        if (target == null)
            return;

        var badgeBg = UIFactory.CreateImage(
            "PreselectBadge",
            target,
            UIColors.Macaroon,
            raycast: false);
        UIFactory.ApplyWhiteSprite(badgeBg);
        var rt = badgeBg.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(BadgeSize, BadgeSize);
        rt.anchoredPosition = new Vector2(-2f, -2f);
        rt.SetAsLastSibling();

        var label = UIFactory.CreateTmp(
            "Num",
            rt,
            order.ToString(),
            14f,
            UIColors.PanelBg,
            TextAlignmentOptions.Center);
        UIHelpers.SetFillParent(label.rectTransform, 1f);
        label.raycastTarget = false;
        label.fontStyle = FontStyles.Bold;

        state.Badges.Add(badgeBg.gameObject);
    }

    private static void CreateOverlay(VisualState state, DirectiveButton button)
    {
        var target = button.transform as RectTransform;
        if (target == null)
            return;

        var img = UIFactory.CreateImage(
            "PreselectOverlay",
            target,
            UIColors.WithAlpha(UIColors.Macaroon, PathLogic.IsEditing ? 0.30f : 0.18f),
            raycast: false);
        UIFactory.ApplyWhiteSprite(img);
        UIHelpers.SetFillParent(img.rectTransform, 2f);
        img.rectTransform.SetAsLastSibling();
        state.Overlays.Add(img.gameObject);
    }

    private static void ApplyOutline(DirectiveButton button, bool selected)
    {
        try
        {
            var outline = OutlineField?.GetValue(button) as PolygonOutline;
            if (outline == null)
                return;

            if (selected)
            {
                outline.gameObject.SetActive(true);
                outline.color = UIColors.Macaroon;
                outline.SetValue(PathLogic.IsEditing ? 10f : 7f);
            }
        }
        catch
        {
            // outline is optional visual polish
        }
    }

    private static void ClearVisuals(VisualState state)
    {
        if (state == null)
            return;

        for (int i = 0; i < state.Lines.Count; i++)
            UIHelpers.DestroySafe(state.Lines[i]);
        state.Lines.Clear();

        for (int i = 0; i < state.Badges.Count; i++)
            UIHelpers.DestroySafe(state.Badges[i]);
        state.Badges.Clear();

        for (int i = 0; i < state.Overlays.Count; i++)
            UIHelpers.DestroySafe(state.Overlays[i]);
        state.Overlays.Clear();
    }
}
