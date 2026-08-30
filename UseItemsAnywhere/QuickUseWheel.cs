using System;
using System.Collections.Generic;
using System.IO;
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
    private const string PrefabPath = "assets/mods/useitemsanywhere.assets/ui/quickusewheel.prefab";
    private const float MaximumVisibleSegmentDegrees = 42f;
    private const float CenterCancelRadius = 104f;
    private const float EmptyWheelRefreshInterval = 0.15f;

    private static Player? _pendingOpenRequest;

    private static readonly Callback<IHandsController> IgnoreHandsResult = _ => { };
    private static readonly Color NormalSegmentColor = new(0.045f, 0.048f, 0.048f, 0.86f);
    private static readonly Color SelectedSegmentColor = new(0.22f, 0.25f, 0.26f, 0.96f);
    private static readonly Color UnavailableSegmentColor = new(0.13f, 0.055f, 0.045f, 0.9f);
    private static readonly Color NormalNameColor = new(0.82f, 0.83f, 0.8f, 1f);
    private static readonly Color UnavailableNameColor = new(0.43f, 0.44f, 0.42f, 1f);
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
    private bool _pendingHighlightActive;
    private bool _openedFromPendingRequest;
    private Player? _iconCachePlayer;
    private GamePlayerOwner? _playerOwner;
    private bool _previousBlockFirearms;
    private Action<Player>? _previousRotationAction;
    private bool _previousCursorVisible;
    private CursorLockMode _previousCursorLockMode;

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
        if (!Configuration.EnableQuickUseWheel.Value || !Application.isFocused)
        {
            _pendingOpenRequest = null;
            Close(false);
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
            ShowCursor();

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                _cancelled = true;
                Close(false);
                return;
            }


            if (_openedFromPendingRequest && Input.GetMouseButtonDown(0))
            {
                var useSelection = _selectedIndex >= 0 && GetSelectedItem()?.IsUsable == true;
                Close(useSelection);
                return;
            }

            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && PageCount > 1)
            {
                _pendingHighlightActive = false;
                _page = Mod(_page + (scroll < 0f ? 1 : -1), PageCount);
                RefreshWheel();
            }
        }

        if (_shortcutHeld && !shortcut.IsPressed())
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
        if (hasPendingAccess && Configuration.PendingItemUseBehavior.Value != Configuration.PendingUseMode.OpenWheel)
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
        _items.Clear();
        PopulateUsableItems(player);
#if DEBUG
        _logger?.LogDebug($"Opening quick-use wheel with {_items.Count} usable item(s).");
#endif
        _selectionVector = Vector2.zero;
        var pendingIndex = _pendingItemOnOpen is null
            ? -1
            : _items.FindIndex(wheelItem => ReferenceEquals(wheelItem.Item, _pendingItemOnOpen));
        _pendingHighlightActive = pendingIndex >= 0;
        _selectedIndex = _pendingHighlightActive ? pendingIndex % ItemsPerPage : -1;
        _page = _pendingHighlightActive ? pendingIndex / ItemsPerPage : 0;
        _pageStartIndex = 0;
        _pageItemCount = 0;
        _nextEmptyWheelRefreshTime = Time.unscaledTime + EmptyWheelRefreshInterval;
        _cancelled = false;
        _isOpen = true;
        InputBlocked = true;

        _previousBlockFirearms = player.MovementContext.BlockFirearms;
        _previousRotationAction = player.MovementContext.RotationAction;
        _previousCursorVisible = Cursor.visible;
        _previousCursorLockMode = Cursor.lockState;
        player.MovementContext.BlockFirearms = true;
        player.MovementContext.RotationAction = null;
        ShowCursor();

        TryAssignRuntimeFont();
        _uiRoot!.SetActive(true);
        _canvasGroup!.alpha = 0f;
        _wheelRoot!.localScale = Vector3.one * 0.92f;
        RefreshWheel();
    }

    private void RefreshUnavailableWheel()
    {
        if (_items.Exists(static item => item.IsUsable)
            || _player is null
            || !_player
            || Time.unscaledTime < _nextEmptyWheelRefreshTime)
        {
            return;
        }

        _nextEmptyWheelRefreshTime = Time.unscaledTime + EmptyWheelRefreshInterval;
        _items.Clear();
        PopulateUsableItems(_player);
        if (!_items.Exists(static item => item.IsUsable))
        {
            RefreshWheel();
            return;
        }

#if DEBUG
        _logger?.LogDebug("Quick-use wheel recovered an available item after a transient refresh.");
#endif
        _selectionVector = Vector2.zero;
        var pendingIndex = _pendingItemOnOpen is null
            ? -1
            : _items.FindIndex(wheelItem => ReferenceEquals(wheelItem.Item, _pendingItemOnOpen));
        _pendingHighlightActive = pendingIndex >= 0;
        _selectedIndex = _pendingHighlightActive ? pendingIndex % ItemsPerPage : -1;
        _page = _pendingHighlightActive ? pendingIndex / ItemsPerPage : 0;
        _pageStartIndex = 0;
        RefreshWheel();
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
        var pendingItem = _pendingItemOnOpen;

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
        _pendingHighlightActive = false;
        _openedFromPendingRequest = false;
        _uiRoot?.SetActive(false);
        Cursor.visible = _previousCursorVisible;
        Cursor.lockState = _previousCursorLockMode;

        if (player is not null && player && selectedItem != null && IsItemStillUsable(player, selectedItem))
        {
            if (pendingItem is not null && !ReferenceEquals(pendingItem, selectedItem))
            {
                ItemAccessDelayPatch.ReplacePendingItemAccess(
                    player,
                    selectedItem,
                    IgnoreHandsResult,
                    false);
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
            _selectedName!.text = "NO USABLE ITEMS";
            _cancelHint!.text = "RELEASE TO CLOSE";
            _pageHint!.gameObject.SetActive(false);
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
            view.Icon.color = wheelItem.IsUsable ? Color.white : UnavailableNameColor;
            view.Name.text = wheelItem.DisplayName;
            view.State.text = wheelItem.State;
            view.State.gameObject.SetActive(
                Configuration.QuickUseShowItemState.Value
                && !string.IsNullOrEmpty(wheelItem.State));
            view.Source.text = wheelItem.SourceName;
            view.Source.gameObject.SetActive(Configuration.QuickUseShowSourceSlot.Value);
            view.FavoriteBadge.gameObject.SetActive(wheelItem.IsFavorite);
        }

        _cancelHint!.text = "CENTER TO CANCEL";
        _pageHint!.gameObject.SetActive(PageCount > 1);
        _pageHint.text = PageCount > 1 ? $"PAGE {_page + 1} / {PageCount}   •   MOUSE WHEEL" : string.Empty;
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
            view.Segment.color = !wheelItem.IsUsable
                ? UnavailableSegmentColor
                : selected ? SelectedSegmentColor : NormalSegmentColor;
            view.Name.color = !wheelItem.IsUsable
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
            _cancelHint!.text = _openedFromPendingRequest
                ? selectedItem.HasValue
                    ? ReferenceEquals(selectedItem.Value.Item, _pendingItemOnOpen)
                        ? "CURRENTLY PENDING  •  LMB: KEEP"
                        : selectedItem.Value.IsUsable ? "LMB: REPLACE" : "ITEM UNAVAILABLE"
                    : "LMB: CLOSE"
                : selectedItem is { IsUsable: false }
                ? "ITEM UNAVAILABLE"
                : selectedItem.HasValue
                    ? selectedItem.Value.IsFavorite ? "MMB: UNFAVORITE" : "MMB: FAVORITE"
                    : "CENTER TO CANCEL";
            _centerBorder!.color = selectedItem is { IsUsable: false }
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
        if (_wheelRoot is null
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _wheelRoot,
                Input.mousePosition,
                null,
                out _selectionVector))
        {
            _selectionVector = Vector2.zero;
        }
        UpdateSelectedIndex();
    }

    private static void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
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
            if (!_pendingHighlightActive)
            {
                _selectedIndex = -1;
            }
            return;
        }

        _pendingHighlightActive = false;

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

            var hasResource = HasResource(item);
            var isUsable = hasResource
                && controller.IsAtReachablePlace(item)
                && item.CheckAction(null).Succeeded;
            var state = !hasResource
                ? "EMPTY"
                : !isUsable ? "UNAVAILABLE" : GetItemState(item);
            var sourceSlot = _sourceSlots.GetValueOrDefault(item, EquipmentSlot.Pockets);
            _items.Add(new WheelItem(
                item,
                GetDisplayName(item),
                GetFullName(item),
                state,
                isUsable,
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
        bool isFavorite,
        EquipmentSlot sourceSlot,
        ItemIcon? icon)
    {
        internal Item Item { get; } = item;
        internal string DisplayName { get; } = displayName;
        internal string FullName { get; } = fullName;
        internal string State { get; } = state;
        internal bool IsUsable { get; } = isUsable;
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
            value,
            SourceSlot,
            Icon);
    }
}
