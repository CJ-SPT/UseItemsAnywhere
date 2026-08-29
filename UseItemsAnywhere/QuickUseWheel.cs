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

    private static readonly Callback<IHandsController> IgnoreHandsResult = _ => { };
    private static readonly Color NormalSegmentColor = new(0.035f, 0.045f, 0.043f, 0.58f);
    private static readonly Color SelectedSegmentColor = new(0.34f, 0.29f, 0.18f, 0.82f);
    private static readonly Color NormalNameColor = new(0.87f, 0.89f, 0.85f, 1f);
    private static readonly EquipmentSlot[] SlotPriority =
    [
        EquipmentSlot.Pockets,
        EquipmentSlot.TacticalVest,
        EquipmentSlot.ArmBand,
        EquipmentSlot.Backpack,
        EquipmentSlot.SecuredContainer,
    ];
    private static readonly EquipmentSlot[][] SourceSlotQueries =
    [
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
    private Player? _player;
    private Player? _iconCachePlayer;
    private GamePlayerOwner? _playerOwner;
    private bool _previousBlockFirearms;
    private Action<Player>? _previousRotationAction;
    private bool _previousCursorVisible;
    private CursorLockMode _previousCursorLockMode;

    internal static bool InputBlocked { get; private set; }

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
            Close(false);
            return;
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
           
            Open();
        }

        if (_isOpen)
        {
            if (!IsGameplayValid())
            {
                Close(false);
                return;
            }

            UpdateSelection();
            UpdatePresentation();
            ShowCursor();

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                _cancelled = true;
                Close(false);
                return;
            }

            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && PageCount > 1)
            {
                _page = Mod(_page + (scroll < 0f ? 1 : -1), PageCount);
                RefreshWheel();
            }
        }

        if (_shortcutHeld && !shortcut.IsPressed())
        {
            var useSelection = _isOpen && !_cancelled && _selectedIndex >= 0;
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
        DestroyRuntimeFont();
    }

    private static int ItemsPerPage => Configuration.QuickUseItemsPerPage.Value;

    private int PageCount => Mathf.Max(1, Mathf.CeilToInt((float)_items.Count / ItemsPerPage));

    private void Open()
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
        
        if (ItemAccessDelayPatch.HasPendingItemAccess(player))
        {
#if DEBUG
            _logger?.LogDebug("Quick-use wheel was suppressed while an item-access delay is pending.");
#endif
            return;
        }

        _player = player;
        _playerOwner = playerOwner;
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
        _cancelled = false;
        _isOpen = true;

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

        RestoreInput();
        _shortcutHeld = false;
        InputBlocked = false;
        _isOpen = false;
        _cancelled = false;
        _selectedIndex = -1;
        _items.Clear();
        _player = null;
        _playerOwner = null;
        _uiRoot?.SetActive(false);
        Cursor.visible = _previousCursorVisible;
        Cursor.lockState = _previousCursorLockMode;

        if (player is not null && player && selectedItem != null && IsItemStillUsable(player, selectedItem))
        {
            player.SetItemInHands(selectedItem, IgnoreHandsResult);
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
            view.Source.text = string.Empty;
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
            view.ItemRoot.sizeDelta = new Vector2(labelWidth, 116f);
            view.Name.rectTransform.sizeDelta = new Vector2(labelWidth, 28f);
            view.Source.rectTransform.sizeDelta = new Vector2(labelWidth, 22f);
            view.Icon.sprite = wheelItem.Icon?.Sprite;
            // Implicit conversion to bool, I know, it looks weird, comparing to null is more expensive
            view.Icon.enabled = view.Icon.sprite;
            view.Name.text = wheelItem.DisplayName;
            view.Source.text = wheelItem.SourceName;
            view.Source.gameObject.SetActive(Configuration.QuickUseShowSourceSlot.Value);
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
            if (view.IsSelected != selected)
            {
                view.IsSelected = selected;
                view.Segment.color = selected ? SelectedSegmentColor : NormalSegmentColor;
                view.Name.color = selected ? Color.white : NormalNameColor;
            }
            view.ItemRoot.localScale = Vector3.Lerp(
                view.ItemRoot.localScale,
                selected ? Vector3.one * 1.08f : Vector3.one,
                Time.unscaledDeltaTime * 20f);

            var wheelItem = GetPageItem(index);
            if (!view.Icon.sprite && wheelItem.Icon?.Sprite)
            {
                view.Icon.sprite = wheelItem.Icon!.Sprite;
                view.Icon.enabled = true;
            }
        }

        if (_presentedSelectedIndex != _selectedIndex)
        {
            _presentedSelectedIndex = _selectedIndex;
            _selectedName!.text = _selectedIndex >= 0 && _selectedIndex < _pageItemCount
                ? GetPageItem(_selectedIndex).FullName
                : "CANCEL";
            _centerBorder!.color = _selectedIndex >= 0
                ? new Color(0.58f, 0.43f, 0.22f, 0.78f)
                : new Color(0.16f, 0.19f, 0.18f, 0.62f);
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
                RequireComponent<TMP_Text>(itemRoot, "Source")));
        }
    }

    private void UpdateSelection()
    {
        var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        _selectionVector = (Vector2)Input.mousePosition - screenCenter;
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
        if (count == 0 || _selectionVector.magnitude < 24f)
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
                if (!_sourceSlots.ContainsKey(item))
                {
                    _sourceSlots.Add(item, SlotPriority[slotIndex]);
                }
            }
        }

        foreach (var item in inventory.GetItemsInSlots(Configuration.MedsSlots.Value))
        {
            if (item is Meds && _seenItems.Add(item))
            {
                _candidateItems.Add(item);
            }
        }

        foreach (var item in inventory.GetItemsInSlots(Configuration.AllOtherItems.Value))
        {
            if (item is FoodDrink && _seenItems.Add(item))
            {
                _candidateItems.Add(item);
            }
        }

        foreach (var item in _candidateItems)
        {
            if (!controller.Examined(item)
                || !controller.IsAtReachablePlace(item)
                || !item.CheckAction(null).Succeeded
                || !HasResource(item))
            {
                continue;
            }

            var sourceSlot = _sourceSlots.GetValueOrDefault(item, EquipmentSlot.Pockets);
            _items.Add(new WheelItem(
                item,
                GetDisplayName(item),
                GetFullName(item),
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
        var comparison = Array.IndexOf(SlotPriority, left.SourceSlot)
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
        TMP_Text source)
    {
        internal Image Segment { get; } = segment;
        internal RectTransform ItemRoot { get; } = itemRoot;
        internal Image Icon { get; } = icon;
        internal TMP_Text Name { get; } = name;
        internal TMP_Text Source { get; } = source;
        internal bool? IsSelected { get; set; }
    }

    private readonly struct WheelItem(
        Item item,
        string displayName,
        string fullName,
        EquipmentSlot sourceSlot,
        ItemIcon? icon)
    {
        internal Item Item { get; } = item;
        internal string DisplayName { get; } = displayName;
        internal string FullName { get; } = fullName;
        internal EquipmentSlot SourceSlot { get; } = sourceSlot;
        internal ItemIcon? Icon { get; } = icon;
        internal string SourceName { get; } = GetSlotName(sourceSlot);
    }
}
