using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UseItemsAnywhere.UI;

namespace UseItemsAnywhere.QuickUseWheel;

internal sealed class QuickUseWheelView
{
    private const string PrefabPath = "assets/mods/useitemsanywhere.assets/ui/quickusewheel.prefab";
    private const float IconRadius = 222f;
    private const float LabelWidthRadius = 188f;

    private static readonly Color NormalSegmentColor = new(0.045f, 0.048f, 0.048f, 0.86f);
    private static readonly Color SelectedSegmentColor = new(0.22f, 0.25f, 0.26f, 0.96f);
    private static readonly Color UnavailableSegmentColor = new(0.13f, 0.055f, 0.045f, 0.9f);
    private static readonly Color QueuedSegmentColor = new(0.16f, 0.135f, 0.075f, 0.94f);
    private static readonly Color NormalNameColor = new(0.82f, 0.83f, 0.8f, 1f);
    private static readonly Color UnavailableNameColor = new(0.43f, 0.44f, 0.42f, 1f);
    private static readonly Color QueuedNameColor = new(0.73f, 0.66f, 0.43f, 1f);
    private static readonly Color NormalIconFrameColor = new(0.32f, 0.34f, 0.34f, 0.96f);
    private static readonly Color SelectedIconFrameColor = new(0.68f, 0.71f, 0.7f, 1f);
    private static readonly Color UnavailableIconFrameColor = new(0.3f, 0.18f, 0.16f, 0.92f);
    private static readonly Color QueuedIconFrameColor = new(0.55f, 0.48f, 0.29f, 0.96f);

    private readonly List<SegmentView> _views = [];
    private RuntimeUiDocument? _document;
    private RuntimeUiTransition? _transition;
    private RectTransform? _segmentTemplate;
    private RectTransform? _itemTemplate;
    private Image? _centerBorder;
    private TMP_Text? _centerHeader;
    private TMP_Text? _selectedName;
    private TMP_Text? _cancelHint;
    private TMP_Text? _pageHint;
    private TMP_Text? _controls;
    private int _presentedSelectedIndex = int.MinValue;

    internal bool IsAvailable => _document?.IsAvailable == true;

    internal void Initialize(RuntimeUiService ui)
    {
        _document = ui.CreateDocument(
            "Quick-use wheel",
            "quickusewheel",
            PrefabPath,
            "UseItemsAnywhere_QuickUseWheel",
            BindPrefab);
    }

    internal void Show()
    {
        if (_document?.IsAvailable != true)
        {
            return;
        }

        _document.Show();
        _transition!.BeginEntrance(1f);
    }

    internal void Hide()
    {
        _document?.Hide();
    }

    internal void Refresh(
        IReadOnlyList<QuickUseWheelEntry> items,
        int pageStartIndex,
        int pageItemCount,
        int page,
        int pageCount,
        QuickUseWheelViewState state)
    {
        EnsureViewCount(pageItemCount);
        ClearViews();
        _centerHeader!.text = state.Header;

        if (pageItemCount == 0)
        {
            _selectedName!.text = state.EmptyTitle;
            _cancelHint!.text = state.EmptyHint;
            _centerBorder!.color = new Color(0.34f, 0.36f, 0.36f, 0.96f);
            UpdateModeStatus(page, pageCount, state.Status);
            _controls!.text = state.Controls;
            _presentedSelectedIndex = -1;
            return;
        }

        var slice = QuickUseWheelGeometry.GetSliceDegrees(pageItemCount);
        var visibleSegmentDegrees = QuickUseWheelGeometry.GetVisibleSegmentDegrees(pageItemCount);
        var showRingNames = pageItemCount < 12;
        var showStateDetails = pageItemCount <= 8;
        var showSourceDetails = pageItemCount <= 7;
        var labelWidth = pageItemCount == 1
            ? 132f
            : Mathf.Clamp(2f * LabelWidthRadius * Mathf.Sin(visibleSegmentDegrees * Mathf.Deg2Rad * 0.5f) * 0.76f, 66f, 132f);

        for (var index = 0; index < pageItemCount; index++)
        {
            var view = _views[index];
            var wheelItem = items[pageStartIndex + index];
            view.Segment.gameObject.SetActive(true);
            view.Segment.Configure(index * slice, visibleSegmentDegrees);

            var angle = index * slice * Mathf.Deg2Rad;
            var radialDirection = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            var textBelowIcon = radialDirection.y > 0.001f
                || Mathf.Abs(radialDirection.y) <= 0.001f && radialDirection.x >= 0f;
            var verticalDirection = textBelowIcon ? 1f : -1f;
            var iconOffset = new Vector2(0f, 31f * verticalDirection);
            view.ItemRoot.gameObject.SetActive(true);
            view.ItemRoot.localScale = Vector3.one;
            view.ItemRoot.anchoredPosition = radialDirection * IconRadius - iconOffset;
            view.ItemRoot.sizeDelta = new Vector2(labelWidth, 140f);
            view.IconFrame.rectTransform.anchoredPosition = iconOffset;
            view.Name.rectTransform.sizeDelta = new Vector2(labelWidth, 24f);
            view.Name.rectTransform.anchoredPosition = new Vector2(0f, -14f * verticalDirection);
            view.State.rectTransform.sizeDelta = new Vector2(labelWidth, 18f);
            view.State.rectTransform.anchoredPosition = new Vector2(0f, -35f * verticalDirection);
            view.Source.rectTransform.sizeDelta = new Vector2(labelWidth, 16f);
            view.Source.rectTransform.anchoredPosition = new Vector2(0f, -52f * verticalDirection);
            view.Icon.sprite = wheelItem.Icon?.Sprite;
            // Unity's implicit bool conversion avoids its more expensive null comparison.
            view.Icon.enabled = view.Icon.sprite;
            view.Icon.color = wheelItem.IsQueued
                ? QueuedNameColor
                : wheelItem.IsUsable ? Color.white : UnavailableNameColor;
            view.Name.text = wheelItem.DisplayName;
            view.Name.gameObject.SetActive(showRingNames);
            view.State.text = wheelItem.State;
            view.State.color = wheelItem.IsQueued
                ? QueuedNameColor
                : new Color(0.72f, 0.74f, 0.72f, 1f);
            view.State.gameObject.SetActive(showStateDetails && wheelItem.ShowState && !string.IsNullOrEmpty(wheelItem.State));
            view.Source.text = wheelItem.SourceName;
            view.Source.color = wheelItem.IsQueued
                ? new Color(0.54f, 0.49f, 0.34f, 1f)
                : new Color(0.45f, 0.47f, 0.46f, 1f);
            view.Source.gameObject.SetActive(showSourceDetails && wheelItem.ShowSource);
            view.FavoriteBadge.gameObject.SetActive(wheelItem.IsFavorite);
        }

        _cancelHint!.text = "CENTER TO CANCEL";
        UpdateModeStatus(page, pageCount, state.Status);
        _controls!.text = state.Controls;
        _presentedSelectedIndex = int.MinValue;
    }

    internal void UpdatePresentation(
        IReadOnlyList<QuickUseWheelEntry> items,
        int pageStartIndex,
        int pageItemCount,
        int selectedIndex,
        QuickUseWheelEntry? selectedItem,
        string selectionHint)
    {
        if (_document?.IsAvailable != true)
        {
            return;
        }

        _transition!.UpdateEntrance();

        for (var index = 0; index < pageItemCount; index++)
        {
            var selected = index == selectedIndex;
            var view = _views[index];
            var wheelItem = items[pageStartIndex + index];
            view.Segment.color = wheelItem.IsQueued
                ? QueuedSegmentColor
                : !wheelItem.IsUsable
                    ? UnavailableSegmentColor
                    : selected ? SelectedSegmentColor : NormalSegmentColor;
            view.Name.color = wheelItem.IsQueued
                ? QueuedNameColor
                : !wheelItem.IsUsable
                    ? UnavailableNameColor
                    : selected ? Color.white : NormalNameColor;
            view.IconFrame.color = wheelItem.IsQueued
                ? QueuedIconFrameColor
                : !wheelItem.IsUsable
                    ? UnavailableIconFrameColor
                    : selected ? SelectedIconFrameColor : NormalIconFrameColor;
            if (!view.Icon.sprite && wheelItem.Icon?.Sprite)
            {
                view.Icon.sprite = wheelItem.Icon!.Sprite;
                view.Icon.enabled = true;
            }
        }

        if (_presentedSelectedIndex == selectedIndex)
        {
            return;
        }

        _presentedSelectedIndex = selectedIndex;
        _selectedName!.text = selectedItem?.FullName ?? "CANCEL";
        _cancelHint!.text = selectionHint;
        _centerBorder!.color = selectedItem is { IsQueued: true }
            ? QueuedSegmentColor
            : selectedItem is { IsUsable: false }
                ? UnavailableSegmentColor
                : selectedItem.HasValue
                    ? new Color(0.58f, 0.61f, 0.61f, 0.96f)
                    : new Color(0.34f, 0.36f, 0.36f, 0.96f);
    }

    internal void Destroy()
    {
        _document?.Destroy();
        _document = null;
        _transition = null;
        _views.Clear();
    }

    private void BindPrefab(RuntimeUiDocument document)
    {
        var canvasGroup = document.Require<CanvasGroup>(string.Empty);
        var wheelRoot = document.Require<RectTransform>("WheelRoot");
        _transition = new RuntimeUiTransition(canvasGroup, wheelRoot);
        _segmentTemplate = document.Require<RectTransform>("WheelRoot/SegmentLayer/SegmentTemplate");
        _itemTemplate = document.Require<RectTransform>("WheelRoot/ItemLayer/ItemTemplate");
        _centerBorder = document.Require<Image>("WheelRoot/CenterBorder");
        _centerHeader = document.Require<TMP_Text>("WheelRoot/Center/CenterHeader/CenterHeaderText");
        _selectedName = document.Require<TMP_Text>("WheelRoot/Center/SelectedName");
        _cancelHint = document.Require<TMP_Text>("WheelRoot/Center/CancelHint");
        _pageHint = document.Require<TMP_Text>("PageHint");
        _pageHint.rectTransform.sizeDelta = new Vector2(760f, 30f);
        _controls = document.Require<TMP_Text>("Controls");
    }

    private void UpdateModeStatus(int page, int pageCount, string status)
    {
        var pageStatus = pageCount > 1
            ? $"PAGE {page + 1} / {pageCount}   •   MOUSE WHEEL     "
            : string.Empty;
        _pageHint!.text = $"{pageStatus}{status}";
        _pageHint.gameObject.SetActive(!string.IsNullOrEmpty(_pageHint.text));
    }

    private void ClearViews()
    {
        foreach (var view in _views)
        {
            view.Segment.gameObject.SetActive(false);
            view.ItemRoot.gameObject.SetActive(false);
            view.Icon.sprite = null;
            view.Name.text = string.Empty;
            view.State.text = string.Empty;
            view.Source.text = string.Empty;
            view.IconFrame.color = NormalIconFrameColor;
            view.ItemRoot.localScale = Vector3.one;
            view.FavoriteBadge.gameObject.SetActive(false);
        }
    }

    private void EnsureViewCount(int count)
    {
        while (_views.Count < count)
        {
            var segmentRoot = UnityEngine.Object.Instantiate(_segmentTemplate!, _segmentTemplate!.parent);
            segmentRoot.name = $"Segment_{_views.Count}";
            var segment = segmentRoot.gameObject.AddComponent<QuickUseWheelSegmentGraphic>();
            segment.raycastTarget = false;
            segment.color = NormalSegmentColor;
            var itemRoot = UnityEngine.Object.Instantiate(_itemTemplate!, _itemTemplate!.parent);
            itemRoot.name = $"Item_{_views.Count}";
            _views.Add(new SegmentView(
                segment,
                itemRoot,
                RuntimeUiDocument.Require<Image>(itemRoot, "IconFrame"),
                RuntimeUiDocument.Require<Image>(itemRoot, "IconFrame/Icon"),
                RuntimeUiDocument.Require<TMP_Text>(itemRoot, "Name"),
                RuntimeUiDocument.Require<TMP_Text>(itemRoot, "State"),
                RuntimeUiDocument.Require<TMP_Text>(itemRoot, "Source"),
                RuntimeUiDocument.Require<RectTransform>(itemRoot, "IconFrame/FavoriteBadge")));
        }
    }

    private sealed class SegmentView(
        QuickUseWheelSegmentGraphic segment,
        RectTransform itemRoot,
        Image iconFrame,
        Image icon,
        TMP_Text name,
        TMP_Text state,
        TMP_Text source,
        RectTransform favoriteBadge)
    {
        internal QuickUseWheelSegmentGraphic Segment { get; } = segment;
        internal RectTransform ItemRoot { get; } = itemRoot;
        internal Image IconFrame { get; } = iconFrame;
        internal Image Icon { get; } = icon;
        internal TMP_Text Name { get; } = name;
        internal TMP_Text State { get; } = state;
        internal TMP_Text Source { get; } = source;
        internal RectTransform FavoriteBadge { get; } = favoriteBadge;
    }
}
