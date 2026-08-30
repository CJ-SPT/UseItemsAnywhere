using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UseItemsAnywhere.Patches;

namespace UseItemsAnywhere;

internal sealed class QuickUseWheel
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private const string PrefabPath = "assets/mods/useitemsanywhere.assets/ui/quickusewheel.prefab";
    private const float MaximumVisibleSegmentDegrees = 42f;
    private const float CenterCancelRadius = 104f;
    private const float EmptyWheelRefreshInterval = 0.15f;
    private const float MouseSelectionSpeed = 24f;
    private const float MaximumSelectionRadius = 280f;

    private static Player? _pendingOpenRequest;

    private static readonly Callback<IHandsController> IgnoreHandsResult = _ => { };
    private static readonly Color NormalSegmentColor = new(0.045f, 0.048f, 0.048f, 0.86f);
    private static readonly Color SelectedSegmentColor = new(0.22f, 0.25f, 0.26f, 0.96f);
    private static readonly Color UnavailableSegmentColor = new(0.13f, 0.055f, 0.045f, 0.9f);
    private static readonly Color QueuedSegmentColor = new(0.16f, 0.135f, 0.075f, 0.94f);
    private static readonly Color NormalNameColor = new(0.82f, 0.83f, 0.8f, 1f);
    private static readonly Color UnavailableNameColor = new(0.43f, 0.44f, 0.42f, 1f);
    private static readonly Color QueuedNameColor = new(0.73f, 0.66f, 0.43f, 1f);
    private static readonly EquipmentSlot[] SlotPriority =
    [
        EquipmentSlot.FirstPrimaryWeapon,
        EquipmentSlot.SecondPrimaryWeapon,
        EquipmentSlot.Holster,
        EquipmentSlot.Scabbard,
        EquipmentSlot.Pockets,
        EquipmentSlot.TacticalVest,
        EquipmentSlot.ArmBand,
        EquipmentSlot.Backpack,
        EquipmentSlot.SecuredContainer,
    ];
    private static readonly EquipmentSlot[][] SourceSlotQueries =
    [
        [EquipmentSlot.FirstPrimaryWeapon],
        [EquipmentSlot.SecondPrimaryWeapon],
        [EquipmentSlot.Holster],
        [EquipmentSlot.Scabbard],
        [EquipmentSlot.Pockets],
        [EquipmentSlot.TacticalVest],
        [EquipmentSlot.ArmBand],
        [EquipmentSlot.Backpack],
        [EquipmentSlot.SecuredContainer],
    ];

    private readonly List<WheelItem> _items = [];
    private readonly List<SegmentView> _views = [];
    private readonly List<Item> _candidateItems = [];
    private readonly HashSet<Item> _seenItems = [];
    private readonly Dictionary<Item, EquipmentSlot> _sourceSlots = [];
    private readonly Dictionary<Item, ItemIcon?> _iconCache = [];
    private readonly HashSet<string> _favoriteTemplateIds = new(StringComparer.Ordinal);

    private AssetBundle? _bundle;
    private GameObject? _uiRoot;
    private CanvasGroup? _canvasGroup;
    private RectTransform? _wheelRoot;
    private Image? _segmentTemplate;
    private RectTransform? _itemTemplate;
    private Image? _centerBorder;
    private TMP_Text? _selectedName;
    private TMP_Text? _cancelHint;
    private TMP_Text? _pageHint;
    private TMP_Text? _controls;
    private TMP_FontAsset? _runtimeFont;
    private bool _ownsRuntimeFont;
    private ManualLogSource? _logger;
    private bool _fontWarningLogged;
    private bool _fontAssigned;
    private bool _inputDetectionLogged;
    private bool _canvasWarningLogged;

    private bool _shortcutHeld;
    private bool _isOpen;
    private bool _cancelled;
    private Vector2 _selectionVector;
    private int _page;
    private int _pageStartIndex;
    private int _pageItemCount;
    private int _selectedIndex = -1;
    private int _presentedSelectedIndex = int.MinValue;
    private float _nextEmptyWheelRefreshTime;
    private Player? _player;
    private Item? _pendingItemOnOpen;
    private bool _openedFromPendingRequest;
    private bool _pendingClickArmed;
    private Player? _iconCachePlayer;
    private GamePlayerOwner? _playerOwner;
    private bool _previousBlockFirearms;
    private Action<Player>? _previousRotationAction;

    internal static bool InputBlocked { get; private set; }

    internal static void RequestPendingOpen(Player player)
    {
        _pendingOpenRequest = player;
    }

    internal void Initialize(string pluginDirectory, ManualLogSource logger, Transform persistentParent)
    {
        _logger = logger;
        var bundlePath = Path.Combine(pluginDirectory, "quickusewheel");
        if (!File.Exists(bundlePath))
        {
            logger.LogError($"Quick-use wheel bundle was not found: {bundlePath}");
            return;
        }

        _bundle = AssetBundle.LoadFromFile(bundlePath);
        if (!_bundle)
        {
            logger.LogError($"Quick-use wheel bundle could not be loaded: {bundlePath}");
            return;
        }

        var prefab = _bundle.LoadAsset<GameObject>(PrefabPath);
        if (!prefab)
        {
            logger.LogError($"Quick-use wheel prefab was not found in {bundlePath}");
            _bundle.Unload(false);
            _bundle = null;
            return;
        }

        _uiRoot = UnityEngine.Object.Instantiate(prefab, persistentParent, false);
        _uiRoot.name = "UseItemsAnywhere_QuickUseWheel";
        _uiRoot.SetActive(false);
        LoadFavorites();

        try
        {
            _canvasGroup = RequireComponent<CanvasGroup>(_uiRoot.transform, string.Empty);
            _wheelRoot = RequireComponent<RectTransform>(_uiRoot.transform, "WheelRoot");
            _segmentTemplate = RequireComponent<Image>(_uiRoot.transform, "WheelRoot/SegmentLayer/SegmentTemplate");
            _itemTemplate = RequireComponent<RectTransform>(_uiRoot.transform, "WheelRoot/ItemLayer/ItemTemplate");
            _centerBorder = RequireComponent<Image>(_uiRoot.transform, "WheelRoot/CenterBorder");
            _selectedName = RequireComponent<TMP_Text>(_uiRoot.transform, "WheelRoot/Center/SelectedName");
            _cancelHint = RequireComponent<TMP_Text>(_uiRoot.transform, "WheelRoot/Center/CancelHint");
            _pageHint = RequireComponent<TMP_Text>(_uiRoot.transform, "PageHint");
            _controls = RequireComponent<TMP_Text>(_uiRoot.transform, "Controls");
        }
        catch (Exception exception)
        {
            logger.LogError($"Quick-use wheel prefab binding failed:\n{exception}");
            UnityEngine.Object.Destroy(_uiRoot);
            _uiRoot = null;
            _bundle.Unload(false);
            _bundle = null;
            return;
        }

        TryAssignRuntimeFont();
    }

    internal void Update()
    {
        if (!Configuration.EnableQuickUseWheel.Value)
        {
            _pendingOpenRequest = null;
            Close(false);
            return;
        }

        if (!Application.isFocused && !_isOpen)
        {
            return;
        }

        if (_pendingOpenRequest is not null)
        {
            var requestedPlayer = _pendingOpenRequest;
            _pendingOpenRequest = null;
            if (!_isOpen
                && Configuration.PendingItemUseBehavior.Value == Configuration.PendingUseMode.OpenWheel
                && TryGetLocalPlayer(out var localPlayer, out _)
                && ReferenceEquals(localPlayer, requestedPlayer))
            {
                Open(true);
            }
        }

        var shortcut = Configuration.QuickUseWheelKey.Value;
        if (shortcut.IsDown())
        {
            _shortcutHeld = true;
            InputBlocked = true;
#if DEBUG
            if (!_inputDetectionLogged)
            {

                _logger?.LogInfo("Quick-use wheel input detected; attempting to open the Canvas.");
                _inputDetectionLogged = true;
            }
#endif

            if (!_isOpen)
            {
                Open();
            }
        }

        if (_isOpen)
        {
            if (!IsGameplayValid())
            {
                Close(false);
                return;
            }

            RefreshUnavailableWheel();
            UpdateSelection();
            if (Input.GetMouseButtonDown(2))
            {
                ToggleSelectedFavorite();
            }
            UpdatePresentation();

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                _cancelled = true;
                Close(false);
                return;
            }


            if (_openedFromPendingRequest && !_pendingClickArmed)
            {
                _pendingClickArmed = !Input.GetMouseButton(0);
            }
            else if (_openedFromPendingRequest && Input.GetMouseButtonDown(0))
            {
                var useSelection = _selectedIndex >= 0 && GetSelectedItem()?.IsUsable == true;
                Close(useSelection);
                return;
            }

            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && PageCount > 1)
            {
                _page = Mod(_page + (scroll < 0f ? 1 : -1), PageCount);
                RefreshWheel();
            }
        }

        if (_shortcutHeld && !IsShortcutMainKeyPressed(shortcut))
        {
            var useSelection = _isOpen
                && !_cancelled
                && _selectedIndex >= 0
                && GetSelectedItem()?.IsUsable == true;
            Close(useSelection);
        }
    }

    internal void OnDestroy()
    {
        Close(false);
        if (_uiRoot)
        {
            UnityEngine.Object.Destroy(_uiRoot);
            _uiRoot = null;
        }
        
        _bundle?.Unload(false);
        _bundle = null;
        _iconCache.Clear();
        _iconCachePlayer = null;
        _pendingOpenRequest = null;
        DestroyRuntimeFont();
    }

    private static int ItemsPerPage => Configuration.QuickUseItemsPerPage.Value;

    private int PageCount => Mathf.Max(1, Mathf.CeilToInt((float)_items.Count / ItemsPerPage));

    private void Open(bool openedFromPendingRequest = false)
    {
        if (!_uiRoot)
        {
#if DEBUG
            if (!_canvasWarningLogged)
            {

                _logger?.LogWarning("Quick-use wheel cannot open because its Canvas is unavailable.");
                _canvasWarningLogged = true;
            }
#endif
            return;
        }
        
        if (!TryGetLocalPlayer(out var player, out var playerOwner))
        {
#if DEBUG
            _logger?.LogWarning("Quick-use wheel input was detected, but no local raid player was available.");
#endif
            return;
        }
        
        if (player.IsInventoryOpened || !player.HealthController.IsAlive)
        {
#if DEBUG
            _logger?.LogDebug("Quick-use wheel was suppressed by the current player state.");
#endif
            return;
        }
        
        var hasPendingAccess = ItemAccessDelayPatch.TryGetPendingItem(player, out var pendingItem);
        var pendingMode = Configuration.PendingItemUseBehavior.Value;
        if (hasPendingAccess && pendingMode == Configuration.PendingUseMode.Ignore)
        {
#if DEBUG
            _logger?.LogDebug("Quick-use wheel was suppressed while an item-access delay is pending.");
#endif
            return;
        }

        _player = player;
        _playerOwner = playerOwner;
        _pendingItemOnOpen = hasPendingAccess ? pendingItem : null;
        _openedFromPendingRequest = openedFromPendingRequest && hasPendingAccess;
        _pendingClickArmed = false;
        _items.Clear();
        PopulateUsableItems(player);
#if DEBUG
        _logger?.LogDebug($"Opening quick-use wheel with {_items.Count} usable item(s).");
#endif
        _selectionVector = Vector2.zero;
        _selectedIndex = -1;
        _page = 0;
        _pageStartIndex = 0;
        _pageItemCount = 0;
        _nextEmptyWheelRefreshTime = Time.unscaledTime + EmptyWheelRefreshInterval;
        _cancelled = false;
        _isOpen = true;
        InputBlocked = true;

        _previousBlockFirearms = player.MovementContext.BlockFirearms;
        _previousRotationAction = player.MovementContext.RotationAction;
        player.MovementContext.BlockFirearms = true;
        player.MovementContext.RotationAction = null;

        TryAssignRuntimeFont();
        _uiRoot!.SetActive(true);
        _canvasGroup!.alpha = 0f;
        _wheelRoot!.localScale = Vector3.one * 0.92f;
        RefreshWheel();
    }

    private void RefreshUnavailableWheel()
    {
        var hasQueuedItems = _items.Exists(static item => item.IsQueued);
        var hadUsableItem = _items.Exists(static item => item.IsUsable);
        if ((!hasQueuedItems && hadUsableItem)
            || _player is null
            || !_player
            || Time.unscaledTime < _nextEmptyWheelRefreshTime)
        {
            return;
        }

        _nextEmptyWheelRefreshTime = Time.unscaledTime + EmptyWheelRefreshInterval;
        _items.Clear();
        PopulateUsableItems(_player);
        _page = Mathf.Clamp(_page, 0, PageCount - 1);
        RefreshWheel();

#if DEBUG
        if (!hadUsableItem && _items.Exists(static item => item.IsUsable))
        {
            _logger?.LogDebug("Quick-use wheel recovered an available item after a transient refresh.");
        }
#endif
    }

    private void Close(bool useSelection)
    {
        if (!_isOpen)
        {
            _shortcutHeld = false;
            InputBlocked = false;
            return;
        }

        var player = _player;
        var selectedItem = useSelection ? GetSelectedItem()?.Item : null;
        var pendingItem = player is not null
            && player
            && ItemAccessDelayPatch.TryGetPendingItem(player, out var currentPendingItem)
                ? currentPendingItem
                : null;

        RestoreInput();
        _shortcutHeld = false;
        InputBlocked = false;
        _isOpen = false;
        _cancelled = false;
        _selectedIndex = -1;
        _items.Clear();
        _player = null;
        _playerOwner = null;
        _pendingItemOnOpen = null;
        _openedFromPendingRequest = false;
        _pendingClickArmed = false;
        _uiRoot?.SetActive(false);

        if (player is not null && player && selectedItem != null && IsItemStillUsable(player, selectedItem))
        {
            if (pendingItem is not null && !ReferenceEquals(pendingItem, selectedItem))
            {
                if (Configuration.PendingItemUseBehavior.Value == Configuration.PendingUseMode.QueueOne)
                {
                    ItemAccessDelayPatch.QueuePendingItemAccess(
                        player,
                        selectedItem,
                        IgnoreHandsResult,
                        false);
                }
                else
                {
                    ItemAccessDelayPatch.ReplacePendingItemAccess(
                        player,
                        selectedItem,
                        IgnoreHandsResult,
                        false);
                }
            }
            else if (pendingItem is null)
            {
                player.SetItemInHands(selectedItem, IgnoreHandsResult);
            }
        }
    }

    private void RestoreInput()
    {
        if (_player is not null && _player)
        {
            _player.MovementContext.BlockFirearms = _previousBlockFirearms;
            if (_player.MovementContext.RotationAction == null)
            {
                _player.MovementContext.RotationAction = _previousRotationAction;
            }
        }
    }

    private void RefreshWheel()
    {
        UpdatePageRange();
        UpdateSelectedIndex();
        EnsureViewCount(_pageItemCount);
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

        if (_pageItemCount == 0)
        {
            _selectedName!.text = "NO ITEMS\nAVAILABLE";
            _cancelHint!.text = "CHECK YOUR LOADOUT";
            _centerBorder!.color = new Color(0.34f, 0.36f, 0.36f, 0.96f);
            _pageHint!.gameObject.SetActive(false);
            _controls!.text = "RELEASE / ESC / RIGHT CLICK TO CLOSE";
            _presentedSelectedIndex = -1;
            return;
        }

        var slice = 360f / _pageItemCount;
        var gap = Mathf.Min(2f, slice * 0.08f);
        var visibleSegmentDegrees = Mathf.Min(slice - gap, MaximumVisibleSegmentDegrees);
        const float labelRadius = 188f;
        var labelWidth = _pageItemCount == 1
            ? 132f
            : Mathf.Clamp(2f * labelRadius * Mathf.Sin((slice - gap) * Mathf.Deg2Rad * 0.5f) * 0.76f, 66f, 132f);

        for (var index = 0; index < _pageItemCount; index++)
        {
            var view = _views[index];
            var wheelItem = GetPageItem(index);
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
            // Implicit conversion to bool, I know, it looks weird, comparing to null is more expensive
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
        _pageHint!.gameObject.SetActive(PageCount > 1);
        _pageHint.text = PageCount > 1 ? $"PAGE {_page + 1} / {PageCount}   •   MOUSE WHEEL" : string.Empty;
        _controls!.text = _openedFromPendingRequest
            ? "LMB CONFIRM   •   MMB FAVORITE   •   ESC / RIGHT CLICK TO CLOSE"
            : "RELEASE TO USE   •   MMB FAVORITE   •   ESC / RIGHT CLICK TO CANCEL";
        _presentedSelectedIndex = int.MinValue;
        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        if (!_isOpen || !_uiRoot)
        {
            return;
        }

        _canvasGroup!.alpha = Mathf.MoveTowards(_canvasGroup.alpha, 1f, Time.unscaledDeltaTime * 12f);
        _wheelRoot!.localScale = Vector3.Lerp(_wheelRoot.localScale, Vector3.one, Time.unscaledDeltaTime * 18f);

        for (var index = 0; index < _pageItemCount; index++)
        {
            var selected = index == _selectedIndex;
            var view = _views[index];
            var wheelItem = GetPageItem(index);
            view.IsSelected = selected;
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

        if (_presentedSelectedIndex != _selectedIndex)
        {
            _presentedSelectedIndex = _selectedIndex;
            var selectedItem = GetSelectedItem();
            _selectedName!.text = selectedItem?.FullName ?? "CANCEL";
            _cancelHint!.text = GetSelectionHint(selectedItem);
            _centerBorder!.color = selectedItem is { IsQueued: true }
                ? QueuedSegmentColor
                : selectedItem is { IsUsable: false }
                    ? UnavailableSegmentColor
                : selectedItem.HasValue
                    ? new Color(0.58f, 0.61f, 0.61f, 0.96f)
                    : new Color(0.34f, 0.36f, 0.36f, 0.96f);
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
                RequireComponent<Image>(itemRoot, "Icon"),
                RequireComponent<TMP_Text>(itemRoot, "Name"),
                RequireComponent<TMP_Text>(itemRoot, "State"),
                RequireComponent<TMP_Text>(itemRoot, "Source"),
                RequireComponent<RectTransform>(itemRoot, "FavoriteBadge")));
        }
    }

    private string GetSelectionHint(WheelItem? selectedItem)
    {
        if (selectedItem is { IsQueued: true })
        {
            return "QUEUED  •  WAITING FOR ACCESS";
        }

        if (_openedFromPendingRequest)
        {
            if (!selectedItem.HasValue)
            {
                return "LMB: CLOSE";
            }
            if (ReferenceEquals(selectedItem.Value.Item, _pendingItemOnOpen))
            {
                return "CURRENTLY PENDING  •  LMB: KEEP";
            }
            return selectedItem.Value.IsUsable ? "LMB: REPLACE" : "ITEM UNAVAILABLE";
        }

        if (selectedItem is { IsUsable: false })
        {
            return "ITEM UNAVAILABLE";
        }
        if (!selectedItem.HasValue)
        {
            return "CENTER TO CANCEL";
        }
        if (_pendingItemOnOpen is not null)
        {
            if (ReferenceEquals(selectedItem.Value.Item, _pendingItemOnOpen))
            {
                return "CURRENTLY PENDING";
            }
            return Configuration.PendingItemUseBehavior.Value == Configuration.PendingUseMode.QueueOne
                ? "RELEASE TO QUEUE"
                : "RELEASE TO REPLACE";
        }
        return selectedItem.Value.IsFavorite ? "MMB: UNFAVORITE" : "MMB: FAVORITE";
    }

    private void LoadFavorites()
    {
        _favoriteTemplateIds.Clear();
        foreach (var templateId in Configuration.QuickUseFavoriteTemplateIds.Value.Split(','))
        {
            var normalized = templateId.Trim();
            if (!string.IsNullOrEmpty(normalized))
            {
                _favoriteTemplateIds.Add(normalized);
            }
        }
    }

    private void ToggleSelectedFavorite()
    {
        var selectedItem = GetSelectedItem();
        if (!selectedItem.HasValue)
        {
            return;
        }

        var templateId = selectedItem.Value.Item.TemplateId.ToString();
        var isFavorite = !_favoriteTemplateIds.Remove(templateId);
        if (isFavorite)
        {
            _favoriteTemplateIds.Add(templateId);
        }

        Configuration.QuickUseFavoriteTemplateIds.Value = string.Join(",", _favoriteTemplateIds);
        for (var index = 0; index < _items.Count; index++)
        {
            if (string.Equals(_items[index].Item.TemplateId.ToString(), templateId, StringComparison.Ordinal))
            {
                _items[index] = _items[index].WithFavorite(isFavorite);
            }
        }

        _presentedSelectedIndex = int.MinValue;
        RefreshWheel();
    }

    private void UpdateSelection()
    {
        var mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        if (mouseDelta.sqrMagnitude > 100f)
        {
            mouseDelta = mouseDelta.normalized * 10f;
        }

        _selectionVector = Vector2.ClampMagnitude(
            _selectionVector + mouseDelta * MouseSelectionSpeed,
            MaximumSelectionRadius);
        UpdateSelectedIndex();
    }

    private void UpdateSelectedIndex()
    {
        var count = _pageItemCount;
        if (count == 0)
        {
            _selectedIndex = -1;
            return;
        }

        if (_selectionVector.magnitude <= CenterCancelRadius)
        {
            _selectedIndex = -1;
            return;
        }

        var degrees = Mathf.Atan2(_selectionVector.x, _selectionVector.y) * Mathf.Rad2Deg;
        if (degrees < 0f)
        {
            degrees += 360f;
        }
        var slice = 360f / count;
        var candidateIndex = Mathf.FloorToInt((degrees + slice * 0.5f) / slice) % count;
        var visibleSegmentDegrees = Mathf.Min(slice - Mathf.Min(2f, slice * 0.08f), MaximumVisibleSegmentDegrees);
        var degreesFromCandidateCenter = Mathf.Abs(Mathf.DeltaAngle(degrees, candidateIndex * slice));
        _selectedIndex = degreesFromCandidateCenter <= visibleSegmentDegrees * 0.5f
            && !GetPageItem(candidateIndex).IsQueued
            ? candidateIndex
            : -1;
    }

    private WheelItem? GetSelectedItem()
    {
        return _selectedIndex >= 0 && _selectedIndex < _pageItemCount
            ? GetPageItem(_selectedIndex)
            : null;
    }

    private void UpdatePageRange()
    {
        _pageStartIndex = _page * ItemsPerPage;
        _pageItemCount = Mathf.Min(ItemsPerPage, Mathf.Max(0, _items.Count - _pageStartIndex));
    }

    private WheelItem GetPageItem(int pageIndex) => _items[_pageStartIndex + pageIndex];

    private bool IsGameplayValid()
    {
        return _player is not null
            && _player
            && _playerOwner is not null
            && _playerOwner
            && _player.HealthController.IsAlive
            && !_player.IsInventoryOpened;
    }

    private static bool TryGetLocalPlayer(out Player player, out GamePlayerOwner playerOwner)
    {
        player = null!;
        playerOwner = null!;
        if (Singleton<IBotGame>.Instance is not LocalGame localGame || !localGame.PlayerOwner)
        {
            return false;
        }
        playerOwner = localGame.PlayerOwner;
        player = playerOwner.Player;
        return player is not null && player;
    }

    private void PopulateUsableItems(Player player)
    {
        var controller = player.InventoryController;
        var inventory = controller.Inventory;
        if (!ReferenceEquals(_iconCachePlayer, player))
        {
            _iconCache.Clear();
            _iconCachePlayer = player;
        }
        _candidateItems.Clear();
        _seenItems.Clear();
        _sourceSlots.Clear();

        for (var slotIndex = 0; slotIndex < SlotPriority.Length; slotIndex++)
        {
            foreach (var item in inventory.GetItemsInSlots(SourceSlotQueries[slotIndex]))
            {
                if (item is not null && !_sourceSlots.ContainsKey(item))
                {
                    _sourceSlots.Add(item, SlotPriority[slotIndex]);
                }
            }
        }
        
        // Seperate iterators here because we want search to respect the slots chosen for the provided item
        if (Configuration.QuickUseShowPrimAndSecWeapons.Value)
        {
            foreach (var item in inventory.GetItemsInSlots(Configuration.DefaultWeaponSlots))
            {
                if (item is Weapon && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }
        
        if (Configuration.QuickUseShowMelee.Value)
        {
            foreach (var item in inventory.GetItemsInSlots(Configuration.AllAllowedMeleeSlots))
            {
                if (item.GetItemComponent<KnifeComponent>() != null && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }
        
        if (Configuration.QuickUseShowMeds.Value)
        {
            foreach (var item in inventory.GetItemsInSlots(Configuration.MedsSlots.Value))
            {
                if (item is Meds && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }
        
        if (Configuration.QuickUseShowFoodDrink.Value)
        {
            foreach (var item in inventory.GetItemsInSlots(Configuration.FoodDrinkSlots.Value))
            {
                if (item is FoodDrink && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }
        
        if (Configuration.QuickUseShowFlares.Value)
        {
            foreach (var item in inventory.GetItemsInSlots(Configuration.FlareSlots.Value))
            {
                if (item is not null
                    && Configuration.FlareIds.Contains(item.TemplateId)
                    && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }
        
        foreach (var item in _candidateItems)
        {
            if (!controller.Examined(item))
            {
                continue;
            }

            var isQueued = ItemAccessDelayPatch.IsQueuedForAccess(player, item);
            var hasResource = HasResource(item);
            var isUsable = !isQueued
                && hasResource
                && controller.IsAtReachablePlace(item)
                && item.CheckAction(null).Succeeded;
            var state = isQueued
                ? "QUEUED"
                : !hasResource
                ? "EMPTY"
                : !isUsable ? "UNAVAILABLE" : GetItemState(item);
            var sourceSlot = _sourceSlots.GetValueOrDefault(item, EquipmentSlot.Pockets);
            _items.Add(new WheelItem(
                item,
                GetDisplayName(item),
                GetFullName(item),
                state,
                isUsable,
                isQueued,
                _favoriteTemplateIds.Contains(item.TemplateId.ToString()),
                sourceSlot,
                GetOrLoadIcon(item)));
        }

        _items.Sort(CompareWheelItems);
        _candidateItems.Clear();
        _seenItems.Clear();
        _sourceSlots.Clear();
    }

    private static int CompareWheelItems(WheelItem left, WheelItem right)
    {
        var comparison = (left.IsFavorite ? 0 : 1).CompareTo(right.IsFavorite ? 0 : 1);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Array.IndexOf(SlotPriority, left.SourceSlot)
            .CompareTo(Array.IndexOf(SlotPriority, right.SourceSlot));
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = (left.Item is Meds ? 0 : 1).CompareTo(right.Item is Meds ? 0 : 1);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.OrdinalIgnoreCase.Compare(left.FullName, right.FullName);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.Item.Id, right.Item.Id);
    }

    private static bool IsItemStillUsable(Player player, Item item)
    {
        return player.HealthController.IsAlive
            && PlayerOwnsItem(player, item)
            && player.InventoryController.Examined(item)
            && player.InventoryController.IsAtReachablePlace(item)
            && item.CheckAction(null).Succeeded
            && HasResource(item);
    }

    private static bool PlayerOwnsItem(Player player, Item item)
    {
        foreach (var ownedItem in player.InventoryController.Inventory.AllRealPlayerItems)
        {
            if (ReferenceEquals(ownedItem, item))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasResource(Item item)
    {
        var medKit = item.GetItemComponent<MedKitComponent>();
        if (medKit != null)
        {
            return medKit.HpResource > 0f;
        }
        var foodDrink = item.GetItemComponent<FoodDrinkComponent>();
        if (foodDrink != null)
        {
            return foodDrink.HpPercent > 0f;
        }
        var resource = item.GetItemComponent<ResourceComponent>();
        return resource == null || resource.Value > 0f;
    }

    private static string GetItemState(Item item)
    {
        if (item is Weapon weapon)
        {
            var magazineMaximum = weapon.GetMaxMagazineCount();
            var ammunition = magazineMaximum > 0
                ? $"{weapon.GetCurrentMagazineCount()}/{magazineMaximum}"
                : string.Empty;
            var repairable = weapon.Repairable;
            var durability = repairable != null && repairable.MaxDurability > 0f
                ? $"DUR {Mathf.RoundToInt(repairable.Durability / repairable.MaxDurability * 100f)}%"
                : string.Empty;
            return JoinItemState(ammunition, durability);
        }

        if (item is Magazine magazine)
        {
            return $"{magazine.Count}/{magazine.MaxCount} RDS";
        }

        var medKit = item.GetItemComponent<MedKitComponent>();
        if (medKit != null)
        {
            return $"{Mathf.CeilToInt(medKit.HpResource)}/{medKit.MaxHpResource} HP";
        }

        var foodDrink = item.GetItemComponent<FoodDrinkComponent>();
        if (foodDrink != null)
        {
            return FormatResource(foodDrink.HpPercent, foodDrink.MaxResource);
        }

        var resource = item.GetItemComponent<ResourceComponent>();
        if (resource != null)
        {
            return FormatResource(resource.Value, resource.MaxResource);
        }

        return item.StackMaxSize > 1 ? $"×{item.StackObjectsCount}" : string.Empty;
    }

    private static string FormatResource(float current, float maximum)
    {
        return maximum > 0f
            ? $"{Mathf.CeilToInt(current)}/{Mathf.CeilToInt(maximum)}"
            : Mathf.CeilToInt(current).ToString();
    }

    private static string JoinItemState(string first, string second)
    {
        if (string.IsNullOrEmpty(first))
        {
            return second;
        }
        return string.IsNullOrEmpty(second) ? first : $"{first} • {second}";
    }

    private static string GetDisplayName(Item item)
    {
        var name = item.LocalizedShortName();
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, item.ShortName, StringComparison.Ordinal))
        {
            name = GetFullName(item);
        }
        return name.Length > 18 ? $"{name[..16]}…" : name;
    }

    private static string GetFullName(Item item)
    {
        var name = item.LocalizedName();
        if (!string.IsNullOrWhiteSpace(name)
            && !string.Equals(name, item.Template.NameLocalizationKey, StringComparison.Ordinal))
        {
            return name;
        }

        name = item.LocalizedShortName();
        return string.IsNullOrWhiteSpace(name) || string.Equals(name, item.ShortName, StringComparison.Ordinal)
            ? item.TemplateId.ToString()
            : name;
    }

    private ItemIcon? GetOrLoadIcon(Item item)
    {
        if (_iconCache.TryGetValue(item, out var cachedIcon) && cachedIcon?.Sprite)
        {
            return cachedIcon;
        }

        try
        {
            var icon = ItemViewFactory.LoadItemIcon(item, 1, false);
            _iconCache[item] = icon;
            return icon;
        }
        catch
        {
            return null;
        }
    }

    private static string GetSlotName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Pockets => "POCKETS",
        EquipmentSlot.TacticalVest => "TACTICAL VEST",
        EquipmentSlot.ArmBand => "ARM BAND",
        EquipmentSlot.Backpack => "BACKPACK",
        EquipmentSlot.SecuredContainer => "SECURE CONTAINER",
        _ => slot.ToString(),
    };

    private bool TryAssignRuntimeFont()
    {
        if (!_uiRoot)
        {
            return false;
        }

        if (_fontAssigned && _runtimeFont)
        {
            return true;
        }

        if (!_runtimeFont)
        {
            Font? legacyFont = null;
            try
            {
                legacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (!legacyFont)
                {
                    legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
            }
            catch (Exception exception)
            {
                LogFontWarning(exception);
            }

            if (legacyFont)
            {
                try
                {
                    var createdFont = TMP_FontAsset.CreateFontAsset(legacyFont);
                    if (createdFont)
                    {
                        _runtimeFont = createdFont;
                        _runtimeFont.name = "UseItemsAnywhere_RuntimeFont";
                        UnityEngine.Object.DontDestroyOnLoad(_runtimeFont);
                        _ownsRuntimeFont = true;
                    }
                }
                catch (Exception exception)
                {
                    LogFontWarning(exception);
                }
            }

            if (!_runtimeFont)
            {
                var loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                TMP_FontAsset? fallbackFont = null;
                foreach (var candidate in loadedFonts)
                {
                    if (!candidate || !candidate.material)
                    {
                        continue;
                    }

                    fallbackFont ??= candidate;
                    if (candidate.name.IndexOf("Bender", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _runtimeFont = candidate;
                        break;
                    }
                }
                _runtimeFont ??= fallbackFont;
            }
        }

        if (!_runtimeFont)
        {
#if DEBUG
            if (!_fontWarningLogged)
            {
                _logger?.LogWarning("Quick-use wheel text font is not available yet; it will be retried when the wheel opens.");
                _fontWarningLogged = true;
            }
#endif
            return false;
        }

        foreach (var text in _uiRoot!.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = _runtimeFont;
        }
        _fontAssigned = true;
        return true;
    }

    private void LogFontWarning(Exception exception)
    {
        if (_fontWarningLogged)
        {
            return;
        }
#if DEBUG
        _logger?.LogWarning($"Quick-use wheel could not create its preferred font and will use an EFT font when available: {exception.Message}");
#endif
        _fontWarningLogged = true;
    }

    private void DestroyRuntimeFont()
    {
        if (!_runtimeFont || !_ownsRuntimeFont)
        {
            _runtimeFont = null;
            _fontAssigned = false;
            return;
        }
        if (_runtimeFont!.material)
        {
            UnityEngine.Object.Destroy(_runtimeFont.material);
        }
        foreach (var atlas in _runtimeFont.atlasTextures ?? [])
        {
            if (atlas)
            {
                UnityEngine.Object.Destroy(atlas);
            }
        }
        UnityEngine.Object.Destroy(_runtimeFont);
        _runtimeFont = null;
        _ownsRuntimeFont = false;
        _fontAssigned = false;
    }

    private static T RequireComponent<T>(Transform root, string path) where T : Component
    {
        var target = string.IsNullOrEmpty(path) ? root : root.Find(path);
        if (!target || !target.TryGetComponent<T>(out var component))
        {
            throw new InvalidOperationException($"Missing {typeof(T).Name} at '{path}'.");
        }
        return component;
    }

    private static int Mod(int value, int modulo) => (value % modulo + modulo) % modulo;

    private static bool IsShortcutMainKeyPressed(BepInEx.Configuration.KeyboardShortcut shortcut)
    {
        if (shortcut.IsPressed())
        {
            return true;
        }

        var virtualKey = GetVirtualKey(shortcut.MainKey);
        return virtualKey != 0 && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static int GetVirtualKey(KeyCode key)
    {
        var value = (int)key;
        if (value >= (int)KeyCode.A && value <= (int)KeyCode.Z)
        {
            return value - 32;
        }
        if (value >= (int)KeyCode.Alpha0 && value <= (int)KeyCode.Alpha9)
        {
            return value;
        }
        if (value >= (int)KeyCode.Keypad0 && value <= (int)KeyCode.Keypad9)
        {
            return 0x60 + value - (int)KeyCode.Keypad0;
        }
        if (value >= (int)KeyCode.F1 && value <= (int)KeyCode.F15)
        {
            return 0x70 + value - (int)KeyCode.F1;
        }

        return key switch
        {
            KeyCode.Backspace => 0x08,
            KeyCode.Tab => 0x09,
            KeyCode.Return or KeyCode.KeypadEnter => 0x0D,
            KeyCode.Pause => 0x13,
            KeyCode.CapsLock => 0x14,
            KeyCode.Escape => 0x1B,
            KeyCode.Space => 0x20,
            KeyCode.PageUp => 0x21,
            KeyCode.PageDown => 0x22,
            KeyCode.End => 0x23,
            KeyCode.Home => 0x24,
            KeyCode.LeftArrow => 0x25,
            KeyCode.UpArrow => 0x26,
            KeyCode.RightArrow => 0x27,
            KeyCode.DownArrow => 0x28,
            KeyCode.Insert => 0x2D,
            KeyCode.Delete => 0x2E,
            KeyCode.KeypadMultiply => 0x6A,
            KeyCode.KeypadPlus => 0x6B,
            KeyCode.KeypadMinus => 0x6D,
            KeyCode.KeypadPeriod => 0x6E,
            KeyCode.KeypadDivide => 0x6F,
            KeyCode.Numlock => 0x90,
            KeyCode.ScrollLock => 0x91,
            KeyCode.LeftShift => 0xA0,
            KeyCode.RightShift => 0xA1,
            KeyCode.LeftControl => 0xA2,
            KeyCode.RightControl => 0xA3,
            KeyCode.LeftAlt => 0xA4,
            KeyCode.RightAlt => 0xA5,
            KeyCode.Mouse0 => 0x01,
            KeyCode.Mouse1 => 0x02,
            KeyCode.Mouse2 => 0x04,
            _ => 0,
        };
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
        internal bool? IsSelected { get; set; }
    }

    private readonly struct WheelItem(
        Item item,
        string displayName,
        string fullName,
        string state,
        bool isUsable,
        bool isQueued,
        bool isFavorite,
        EquipmentSlot sourceSlot,
        ItemIcon? icon)
    {
        internal Item Item { get; } = item;
        internal string DisplayName { get; } = displayName;
        internal string FullName { get; } = fullName;
        internal string State { get; } = state;
        internal bool IsUsable { get; } = isUsable;
        internal bool IsQueued { get; } = isQueued;
        internal bool IsFavorite { get; } = isFavorite;
        internal EquipmentSlot SourceSlot { get; } = sourceSlot;
        internal ItemIcon? Icon { get; } = icon;
        internal string SourceName { get; } = GetSlotName(sourceSlot);

        internal WheelItem WithFavorite(bool value) => new(
            Item,
            DisplayName,
            FullName,
            State,
            IsUsable,
            IsQueued,
            value,
            SourceSlot,
            Icon);
    }
}
