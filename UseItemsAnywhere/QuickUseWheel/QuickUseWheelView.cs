using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UseItemsAnywhere.UI;

namespace UseItemsAnywhere.QuickUseWheel;

internal sealed class QuickUseWheelView
{
    private const string PrefabPath = "assets/mods/useitemsanywhere.assets/ui/quickusewheel.prefab";
    private const float MaximumVisibleSegmentDegrees = 42f;

    private static readonly Color NormalSegmentColor = new(0.045f, 0.048f, 0.048f, 0.86f);
    private static readonly Color SelectedSegmentColor = new(0.22f, 0.25f, 0.26f, 0.96f);
    private static readonly Color UnavailableSegmentColor = new(0.13f, 0.055f, 0.045f, 0.9f);
    private static readonly Color QueuedSegmentColor = new(0.16f, 0.135f, 0.075f, 0.94f);
    private static readonly Color NormalNameColor = new(0.82f, 0.83f, 0.8f, 1f);
    private static readonly Color UnavailableNameColor = new(0.43f, 0.44f, 0.42f, 1f);
    private static readonly Color QueuedNameColor = new(0.73f, 0.66f, 0.43f, 1f);

    private readonly List<SegmentView> _views = [];
    private RuntimeUiDocument? _document;
    private RuntimeUiTransition? _transition;
    private Image? _segmentTemplate;
    private RectTransform? _itemTemplate;
    private Image? _centerBorder;
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
        _transition!.BeginEntrance(0.92f);
    }

    internal void Hide()
    {
        _document?.Hide();
    }

    internal void Refresh(
        IReadOnlyList<QuickUseWheelItem> items,
        int pageStartIndex,
        int pageItemCount,
        int page,
        int pageCount,
        bool openedFromPendingRequest)
    {
        EnsureViewCount(pageItemCount);
        ClearViews();

        if (pageItemCount == 0)
        {
            _selectedName!.text = "NO ITEMS\nAVAILABLE";
            _cancelHint!.text = "CHECK YOUR LOADOUT";
            _centerBorder!.color = new Color(0.34f, 0.36f, 0.36f, 0.96f);
            _pageHint!.gameObject.SetActive(false);
            _controls!.text = "RELEASE / ESC / RIGHT CLICK TO CLOSE";
            _presentedSelectedIndex = -1;
            return;
        }

        var slice = 360f / pageItemCount;
        var gap = Mathf.Min(2f, slice * 0.08f);
        var visibleSegmentDegrees = Mathf.Min(slice - gap, MaximumVisibleSegmentDegrees);
        const float labelRadius = 188f;
        var labelWidth = pageItemCount == 1
            ? 132f
            : Mathf.Clamp(2f * labelRadius * Mathf.Sin((slice - gap) * Mathf.Deg2Rad * 0.5f) * 0.76f, 66f, 132f);

        for (var index = 0; index < pageItemCount; index++)
        {
            var view = _views[index];
            var wheelItem = items[pageStartIndex + index];
            view.Segment.gameObject.SetActive(true);
            view.Segment.fillAmount = visibleSegmentDegrees / 360f;
            view.Segment.rectTransform.localEulerAngles = new Vector3(0f, 0f, visibleSegmentDegrees * 0.5f - index * slice);

            var angle = index * slice * Mathf.Deg2Rad;
            view.ItemRoot.gameObject.SetActive(true);
            view.ItemRoot.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * labelRadius;
            view.ItemRoot.sizeDelta = new Vector2(labelWidth, 140f);
            view.Name.rectTransform.sizeDelta = new Vector2(labelWidth, 28f);
            view.State.rectTransform.sizeDelta = new Vector2(labelWidth, 20f);
            view.Source.rectTransform.sizeDelta = new Vector2(labelWidth, 22f);
            view.Icon.sprite = wheelItem.Icon?.Sprite;
            // Unity's implicit bool conversion avoids its more expensive null comparison.
            view.Icon.enabled = view.Icon.sprite;
            view.Icon.color = wheelItem.IsQueued
                ? QueuedNameColor
                : wheelItem.IsUsable ? Color.white : UnavailableNameColor;
            view.Name.text = wheelItem.DisplayName;
            view.State.text = wheelItem.State;
            view.State.color = wheelItem.IsQueued
                ? QueuedNameColor
                : new Color(0.72f, 0.74f, 0.72f, 1f);
            view.State.gameObject.SetActive(
                wheelItem.IsQueued
                || Configuration.QuickUseShowItemState.Value
                && !string.IsNullOrEmpty(wheelItem.State));
            view.Source.text = wheelItem.SourceName;
            view.Source.color = wheelItem.IsQueued
                ? new Color(0.54f, 0.49f, 0.34f, 1f)
                : new Color(0.45f, 0.47f, 0.46f, 1f);
            view.Source.gameObject.SetActive(Configuration.QuickUseShowSourceSlot.Value);
            view.FavoriteBadge.gameObject.SetActive(wheelItem.IsFavorite);
        }

        _cancelHint!.text = "CENTER TO CANCEL";
        _pageHint!.gameObject.SetActive(pageCount > 1);
        _pageHint.text = pageCount > 1 ? $"PAGE {page + 1} / {pageCount}   •   MOUSE WHEEL" : string.Empty;
        _controls!.text = openedFromPendingRequest
            ? "LMB CONFIRM   •   MMB FAVORITE   •   ESC / RIGHT CLICK TO CLOSE"
            : "RELEASE TO USE   •   MMB FAVORITE   •   ESC / RIGHT CLICK TO CANCEL";
        _presentedSelectedIndex = int.MinValue;
    }

    internal void UpdatePresentation(
        IReadOnlyList<QuickUseWheelItem> items,
        int pageStartIndex,
        int pageItemCount,
        int selectedIndex,
        QuickUseWheelItem? selectedItem,
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
            view.ItemRoot.localScale = Vector3.Lerp(
                view.ItemRoot.localScale,
                selected ? Vector3.one * 1.08f : Vector3.one,
                Time.unscaledDeltaTime * 20f);
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
        _segmentTemplate = document.Require<Image>("WheelRoot/SegmentLayer/SegmentTemplate");
        _itemTemplate = document.Require<RectTransform>("WheelRoot/ItemLayer/ItemTemplate");
        _centerBorder = document.Require<Image>("WheelRoot/CenterBorder");
        _selectedName = document.Require<TMP_Text>("WheelRoot/Center/SelectedName");
        _cancelHint = document.Require<TMP_Text>("WheelRoot/Center/CancelHint");
        _pageHint = document.Require<TMP_Text>("PageHint");
        _controls = document.Require<TMP_Text>("Controls");
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
            view.FavoriteBadge.gameObject.SetActive(false);
        }
    }

    private void EnsureViewCount(int count)
    {
        while (_views.Count < count)
        {
            var segment = UnityEngine.Object.Instantiate(_segmentTemplate!, _segmentTemplate!.transform.parent);
            segment.name = $"Segment_{_views.Count}";
            var itemRoot = UnityEngine.Object.Instantiate(_itemTemplate!, _itemTemplate!.parent);
            itemRoot.name = $"Item_{_views.Count}";
            _views.Add(new SegmentView(
                segment,
                itemRoot,
                RuntimeUiDocument.Require<Image>(itemRoot, "Icon"),
                RuntimeUiDocument.Require<TMP_Text>(itemRoot, "Name"),
                RuntimeUiDocument.Require<TMP_Text>(itemRoot, "State"),
                RuntimeUiDocument.Require<TMP_Text>(itemRoot, "Source"),
                RuntimeUiDocument.Require<RectTransform>(itemRoot, "FavoriteBadge")));
        }
    }

    private sealed class SegmentView(
        Image segment,
        RectTransform itemRoot,
        Image icon,
        TMP_Text name,
        TMP_Text state,
        TMP_Text source,
        RectTransform favoriteBadge)
    {
        internal Image Segment { get; } = segment;
        internal RectTransform ItemRoot { get; } = itemRoot;
        internal Image Icon { get; } = icon;
        internal TMP_Text Name { get; } = name;
        internal TMP_Text State { get; } = state;
        internal TMP_Text Source { get; } = source;
        internal RectTransform FavoriteBadge { get; } = favoriteBadge;
    }
}
