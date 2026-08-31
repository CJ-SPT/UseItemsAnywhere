using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using UnityEngine;
using UseItemsAnywhere.Patches;
using UseItemsAnywhere.UI;

namespace UseItemsAnywhere.QuickUseWheel;

internal sealed class QuickUseWheelController
{
    private enum WheelMode
    {
        Items,
        WeaponDevices,
    }

    private const float CenterCancelRadius = 104f;
    private const float WheelRefreshInterval = 0.15f;
    private const float MouseSelectionSpeed = 24f;
    private const float MaximumSelectionRadius = 280f;

    private static readonly Callback<IHandsController> IgnoreHandsResult = _ => { };
    private static Player? _pendingOpenRequest;

    private readonly QuickUseWheelInventory _inventory = new();
    private readonly WeaponDeviceWheelInventory _deviceInventory = new();
    private readonly List<QuickUseWheelEntry> _entries = [];
    private readonly QuickUseWheelView _view = new();
    private RuntimeUiService _ui = null!;
    private ManualLogSource? _logger;
#if DEBUG
    private bool _inputDetectionLogged;
    private bool _canvasWarningLogged;
#endif
    private bool _shortcutHeld;
    private bool _shortcutHoldTriggered;
    private float _shortcutPressedTime;
    private WheelMode _gestureMode;
    private WheelMode _mode;
    private bool _isOpen;
    private bool _cancelled;
    private Vector2 _selectionVector;
    private int _page;
    private int _pageStartIndex;
    private int _pageItemCount;
    private int _selectedIndex = -1;
    private float _nextWheelRefreshTime;
    private Player? _player;
    private Item? _pendingItemOnOpen;
    private bool _openedFromPendingRequest;
    private bool _pendingClickArmed;
    private GamePlayerOwner? _playerOwner;
    private bool _previousBlockFirearms;
    private Action<Player>? _previousRotationAction;
    private string? _lastItemTemplateId;

    internal static bool InputBlocked { get; private set; }

    private static int ItemsPerPage => Configuration.QuickUseItemsPerPage.Value;

    private int PageCount => Mathf.Max(1, Mathf.CeilToInt((float)_entries.Count / ItemsPerPage));

    internal static void RequestPendingOpen(Player player)
    {
        _pendingOpenRequest = player;
    }

    internal void Initialize(
        ManualLogSource logger,
        RuntimeUiService ui)
    {
        _logger = logger;
        _ui = ui;
        _inventory.LoadFavorites();
        _inventory.Initialize(ui);
        _deviceInventory.Initialize(ui);
        _view.Initialize(ui);
    }

    internal void Update()
    {
        var itemWheelEnabled = Configuration.EnableQuickUseWheel.Value;
        var deviceWheelEnabled = Configuration.EnableWeaponDeviceWheel.Value;
        if (_isOpen
            && (_mode == WheelMode.Items && !itemWheelEnabled
                || _mode == WheelMode.WeaponDevices && !deviceWheelEnabled))
        {
            Close(false);
            return;
        }

        if (!itemWheelEnabled)
        {
            _pendingOpenRequest = null;
        }
        if (!itemWheelEnabled && !deviceWheelEnabled)
        {
            ResetShortcutGesture();
            InputBlocked = false;
            return;
        }

        if (!Application.isFocused && !_isOpen)
        {
            ResetShortcutGesture();
            return;
        }

        if (itemWheelEnabled)
        {
            HandlePendingOpenRequest();
        }

        if (!_shortcutHeld && !_isOpen)
        {
            // Check the modified device shortcut first. This prevents its main key
            // from also starting the ordinary item-wheel gesture.
            if (deviceWheelEnabled && Configuration.WeaponDeviceWheelKey.Value.IsDown())
            {
                BeginShortcutGesture(WheelMode.WeaponDevices);
            }
            else if (itemWheelEnabled && Configuration.QuickUseWheelKey.Value.IsDown())
            {
                BeginShortcutGesture(WheelMode.Items);
            }
        }

#if DEBUG
        if (_shortcutHeld && !_inputDetectionLogged)
        {
            _logger?.LogInfo("Quick-use wheel tap/hold input detected.");
            _inputDetectionLogged = true;
        }
#endif

        var shortcut = _gestureMode == WheelMode.WeaponDevices
            ? Configuration.WeaponDeviceWheelKey.Value
            : Configuration.QuickUseWheelKey.Value;
        var shortcutPressed = _shortcutHeld && QuickUseWheelShortcut.IsMainKeyPressed(shortcut);
        if (shortcutPressed
            && !_shortcutHoldTriggered
            && !_isOpen
            && Time.unscaledTime - _shortcutPressedTime >= Configuration.QuickUseWheelHoldDuration.Value)
        {
            _shortcutHoldTriggered = true;
            Open(_gestureMode);
        }

        if (_isOpen)
        {
            UpdateOpenWheel();
        }

        if (_shortcutHeld && !shortcutPressed)
        {
            var useSelection = _isOpen
                && _mode == WheelMode.Items
                && !_cancelled
                && _selectedIndex >= 0
                && GetSelectedItem()?.IsUsable == true;
            if (_isOpen)
            {
                PlaySound(useSelection ? EUISoundType.ButtonClick : EUISoundType.MenuEscape);
                Close(useSelection);
            }
            else
            {
                var useLastItem = _gestureMode == WheelMode.Items
                    && !_shortcutHoldTriggered
                    && Configuration.QuickUseTapLastItem.Value;
                ResetShortcutGesture();
                if (useLastItem)
                {
                    UseLastItem();
                }
            }
        }
    }

    internal void OnDestroy()
    {
        Close(false);
        _view.Destroy();
        _inventory.Clear();
        _deviceInventory.Clear();
        _entries.Clear();
        _pendingOpenRequest = null;
    }

    private void HandlePendingOpenRequest()
    {
        if (_pendingOpenRequest is null)
        {
            return;
        }

        var requestedPlayer = _pendingOpenRequest;
        _pendingOpenRequest = null;
        if (!_isOpen
            && Configuration.PendingItemUseBehavior.Value == Configuration.PendingUseMode.OpenWheel
            && TryGetLocalPlayer(out var localPlayer, out _)
            && ReferenceEquals(localPlayer, requestedPlayer))
        {
            Open(WheelMode.Items, true);
        }
    }

    private void UpdateOpenWheel()
    {
        if (!IsGameplayValid())
        {
            Close(false);
            return;
        }
        if (_mode == WheelMode.WeaponDevices
            && (_player is null || !_deviceInventory.IsCurrentFirearm(_player)))
        {
            Close(false);
            return;
        }

        RefreshWheelItems();
        UpdateSelection();

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            _cancelled = true;
            PlaySound(EUISoundType.MenuEscape);
            Close(false);
            return;
        }

        if (_mode == WheelMode.WeaponDevices)
        {
            if (Input.GetMouseButtonDown(0))
            {
                ControlSelectedDevice(false);
            }
            else if (Input.GetMouseButtonDown(2))
            {
                ControlSelectedDevice(true);
            }
        }
        else if (Input.GetMouseButtonDown(2))
        {
            ToggleSelectedFavorite();
        }

        UpdatePresentation();

        if (_mode == WheelMode.Items && _openedFromPendingRequest && !_pendingClickArmed)
        {
            _pendingClickArmed = !Input.GetMouseButton(0);
        }
        else if (_mode == WheelMode.Items
            && _openedFromPendingRequest
            && Input.GetMouseButtonDown(0))
        {
            var useSelection = _selectedIndex >= 0 && GetSelectedItem()?.IsUsable == true;
            PlaySound(useSelection ? EUISoundType.ButtonClick : EUISoundType.MenuEscape);
            Close(useSelection);
            return;
        }

        var scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f && PageCount > 1)
        {
            _page = Mod(_page + (scroll < 0f ? 1 : -1), PageCount);
            PlaySound(EUISoundType.MenuDropdownSelect);
            RefreshWheel();
        }
    }

    private void Open(WheelMode mode, bool openedFromPendingRequest = false)
    {
        if (!_view.IsAvailable)
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

        Item? pendingItem = null;
        var hasPendingAccess = mode == WheelMode.Items
            && ItemAccessDelayPatch.TryGetPendingItem(player, out pendingItem);
        var pendingMode = Configuration.PendingItemUseBehavior.Value;
        if (mode == WheelMode.Items
            && hasPendingAccess
            && pendingMode == Configuration.PendingUseMode.Ignore)
        {
#if DEBUG
            _logger?.LogDebug("Quick-use wheel was suppressed while an item-access delay is pending.");
#endif
            return;
        }

        _player = player;
        _playerOwner = playerOwner;
        _mode = mode;
        _pendingItemOnOpen = mode == WheelMode.Items && hasPendingAccess ? pendingItem : null;
        _openedFromPendingRequest = mode == WheelMode.Items && openedFromPendingRequest && hasPendingAccess;
        _pendingClickArmed = false;
        PopulateEntries();
        if (mode == WheelMode.WeaponDevices && _entries.Count == 0)
        {
            _player = null;
            _playerOwner = null;
            _deviceInventory.Clear();
            PlaySound(EUISoundType.MenuEscape);
            return;
        }
#if DEBUG
        _logger?.LogDebug($"Opening {_mode} wheel with {_entries.Count} entries.");
#endif
        _selectionVector = Vector2.zero;
        _selectedIndex = -1;
        _page = 0;
        _pageStartIndex = 0;
        _pageItemCount = 0;
        _nextWheelRefreshTime = Time.unscaledTime + WheelRefreshInterval;
        _cancelled = false;
        _isOpen = true;
        InputBlocked = true;

        _previousBlockFirearms = player.MovementContext.BlockFirearms;
        _previousRotationAction = player.MovementContext.RotationAction;
        player.MovementContext.BlockFirearms = true;
        player.MovementContext.RotationAction = null;

        _view.Show();
        RefreshWheel();
        PlaySound(EUISoundType.MenuContextMenu);
    }

    private void RefreshWheelItems()
    {
        var hadUsableItem = _mode == WheelMode.Items
            ? _inventory.HasUsableItems
            : _deviceInventory.HasAvailableItems;
        if (_player is null
            || !_player
            || Time.unscaledTime < _nextWheelRefreshTime)
        {
            return;
        }

        _nextWheelRefreshTime = Time.unscaledTime + WheelRefreshInterval;
        PopulateEntries();
        _page = Mathf.Clamp(_page, 0, PageCount - 1);
        RefreshWheel();

#if DEBUG
        var hasUsableItem = _mode == WheelMode.Items
            ? _inventory.HasUsableItems
            : _deviceInventory.HasAvailableItems;
        if (!hadUsableItem && hasUsableItem)
        {
            _logger?.LogDebug("Quick-use wheel recovered an available entry after a transient refresh.");
        }
#endif
    }

    private void PopulateEntries()
    {
        _entries.Clear();
        if (_player is null || !_player)
        {
            return;
        }

        if (_mode == WheelMode.Items)
        {
            _inventory.Populate(_player);
            foreach (var item in _inventory.Items)
            {
                _entries.Add(new QuickUseWheelEntry(
                    item.DisplayName,
                    item.FullName,
                    item.State,
                    item.SourceName,
                    item.IsUsable,
                    item.IsQueued,
                    item.IsFavorite,
                    item.IsQueued || item.IsGrouped || Configuration.QuickUseShowItemState.Value,
                    Configuration.QuickUseShowSourceSlot.Value,
                    item.Icon));
            }
            return;
        }

        _deviceInventory.Populate(_player);
        foreach (var device in _deviceInventory.Items)
        {
            _entries.Add(new QuickUseWheelEntry(
                device.DisplayName,
                device.FullName,
                device.State,
                device.SourceName,
                device.IsAvailable,
                false,
                false,
                true,
                !string.IsNullOrEmpty(device.SourceName),
                device.Icon));
        }
    }

    private void Close(bool useSelection)
    {
        if (!_isOpen)
        {
            ResetShortcutGesture();
            InputBlocked = false;
            return;
        }

        var player = _player;
        var selectedWheelItem = _mode == WheelMode.Items && useSelection ? GetSelectedItem() : null;
        var selectedItem = player is not null
            && player
            && selectedWheelItem.HasValue
                ? QuickUseWheelInventory.ResolveItemForUse(player, selectedWheelItem.Value)
                : null;
        var pendingItem = player is not null
            && player
            && ItemAccessDelayPatch.TryGetPendingItem(player, out var currentPendingItem)
                ? currentPendingItem
                : null;

        RestoreInput();
        ResetShortcutGesture();
        InputBlocked = false;
        _isOpen = false;
        _cancelled = false;
        _selectedIndex = -1;
        _inventory.ClearItems();
        _deviceInventory.Clear();
        _entries.Clear();
        _player = null;
        _playerOwner = null;
        _pendingItemOnOpen = null;
        _openedFromPendingRequest = false;
        _pendingClickArmed = false;
        _view.Hide();

        if (player is not null
            && player
            && selectedItem != null)
        {
            _lastItemTemplateId = selectedItem.TemplateId.ToString();
            UseSelectedItem(player, selectedItem, pendingItem);
        }
    }

    private void UseLastItem()
    {
        if (string.IsNullOrEmpty(_lastItemTemplateId)
            || !TryGetLocalPlayer(out var player, out _)
            || player.IsInventoryOpened
            || !player.HealthController.IsAlive)
        {
            return;
        }

        var selectedItem = _inventory.ResolveItemForTemplate(player, _lastItemTemplateId);
        if (selectedItem is null)
        {
            PlaySound(EUISoundType.MenuEscape);
            return;
        }

        var pendingItem = ItemAccessDelayPatch.TryGetPendingItem(player, out var currentPendingItem)
            ? currentPendingItem
            : null;
        if (pendingItem is not null)
        {
            switch (Configuration.PendingItemUseBehavior.Value)
            {
                case Configuration.PendingUseMode.Ignore:
                    PlaySound(EUISoundType.MenuEscape);
                    return;
                case Configuration.PendingUseMode.OpenWheel:
                    Open(WheelMode.Items, true);
                    return;
            }
        }

        _lastItemTemplateId = selectedItem.TemplateId.ToString();
        PlaySound(EUISoundType.ButtonClick);
        UseSelectedItem(player, selectedItem, pendingItem);
    }

    private static void UseSelectedItem(Player player, Item selectedItem, Item? pendingItem)
    {
        if (pendingItem is not null && !ReferenceEquals(pendingItem, selectedItem))
        {
            if (Configuration.PendingItemUseBehavior.Value == Configuration.PendingUseMode.QueueOne)
            {
                ItemAccessDelayPatch.QueuePendingItemAccess(player, selectedItem, IgnoreHandsResult, false);
            }
            else
            {
                ItemAccessDelayPatch.ReplacePendingItemAccess(player, selectedItem, IgnoreHandsResult, false);
            }
        }
        else if (pendingItem is null)
        {
            player.SetItemInHands(selectedItem, IgnoreHandsResult);
        }
    }

    private void ControlSelectedDevice(bool cycleMode)
    {
        var selectedDevice = GetSelectedDevice();
        if (_player is null
            || !_player
            || !selectedDevice.HasValue
            || !selectedDevice.Value.IsAvailable)
        {
            PlaySound(EUISoundType.MenuEscape);
            return;
        }

        var succeeded = cycleMode
            ? _deviceInventory.CycleMode(_player, selectedDevice.Value)
            : _deviceInventory.Toggle(_player, selectedDevice.Value);
        if (!succeeded)
        {
            PlaySound(EUISoundType.MenuEscape);
            PopulateEntries();
            RefreshWheel();
            return;
        }

        if (!cycleMode)
        {
            _player.PlayTacticalSound();
        }
        PlaySound(cycleMode ? EUISoundType.MenuDropdownSelect : EUISoundType.ButtonClick);
        PopulateEntries();
        _page = Mathf.Clamp(_page, 0, PageCount - 1);
        RefreshWheel();
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

    private void ResetShortcutGesture()
    {
        _shortcutHeld = false;
        _shortcutHoldTriggered = false;
        _shortcutPressedTime = 0f;
        _gestureMode = WheelMode.Items;
    }

    private void BeginShortcutGesture(WheelMode mode)
    {
        _shortcutHeld = true;
        _shortcutHoldTriggered = false;
        _shortcutPressedTime = Time.unscaledTime;
        _gestureMode = mode;
    }

    private void RefreshWheel()
    {
        UpdatePageRange();
        UpdateSelectedIndex();
        _view.Refresh(
            _entries,
            _pageStartIndex,
            _pageItemCount,
            _page,
            PageCount,
            GetViewState());
        UpdatePresentation();
    }

    private QuickUseWheelViewState GetViewState()
    {
        if (_mode == WheelMode.WeaponDevices)
        {
            return new QuickUseWheelViewState(
                "WEAPON DEVICES",
                "NO DEVICES\nAVAILABLE",
                "EQUIP A WEAPON WITH TACTICAL DEVICES",
                "LMB TOGGLE   •   MMB MODE   •   RELEASE / ESC / RMB CLOSE",
                "CHANGES APPLY IMMEDIATELY");
        }

        var pendingMode = Configuration.PendingItemUseBehavior.Value switch
        {
            Configuration.PendingUseMode.Ignore => "IGNORE",
            Configuration.PendingUseMode.CancelAndReplace => "REPLACE",
            Configuration.PendingUseMode.QueueOne => "QUEUE ONE",
            Configuration.PendingUseMode.OpenWheel => "OPEN WHEEL",
            _ => "UNKNOWN",
        };
        var groupingMode = !Configuration.QuickUseGroupIdenticalItems.Value
            ? "OFF"
            : Configuration.QuickUseGroupedItemSelection.Value switch
            {
                Configuration.GroupedItemSelectionMode.LowestResourceFirst => "LOWEST RESOURCE",
                Configuration.GroupedItemSelectionMode.HighestResourceFirst => "HIGHEST RESOURCE",
                Configuration.GroupedItemSelectionMode.FastestAccessFirst => "FASTEST ACCESS",
                _ => "UNKNOWN",
            };
        var controls = _openedFromPendingRequest
            ? "LMB CONFIRM   •   MMB FAVORITE   •   ESC / RIGHT CLICK TO CLOSE"
            : "RELEASE TO USE   •   MMB FAVORITE   •   ESC / RIGHT CLICK TO CANCEL";
        return new QuickUseWheelViewState(
            "QUICK USE",
            "NO ITEMS\nAVAILABLE",
            "CHECK YOUR LOADOUT",
            controls,
            $"PENDING: {pendingMode}   •   GROUP: {groupingMode}");
    }

    private void UpdatePresentation()
    {
        var selectedItem = GetSelectedEntry();
        _view.UpdatePresentation(
            _entries,
            _pageStartIndex,
            _pageItemCount,
            _selectedIndex,
            selectedItem,
            GetSelectionHint(selectedItem));
    }

    private string GetSelectionHint(QuickUseWheelEntry? selectedEntry)
    {
        if (_mode == WheelMode.WeaponDevices)
        {
            if (!selectedEntry.HasValue)
            {
                return "SELECT A DEVICE";
            }
            if (!selectedEntry.Value.IsUsable)
            {
                return "DEVICE UNAVAILABLE";
            }
            return GetSelectedDevice() switch
            {
                { IsAggregate: true, CanCycleMode: true } => "LMB TOGGLE ALL\nMMB NEXT MODES",
                { IsAggregate: true } => "LMB: TOGGLE ALL",
                { CanCycleMode: true } => "LMB TOGGLE\nMMB NEXT MODE",
                _ => "LMB: TOGGLE",
            };
        }

        var selectedItem = GetSelectedItem();
        if (selectedEntry is { IsQueued: true })
        {
            return "QUEUED  •  WAITING FOR ACCESS";
        }

        if (_openedFromPendingRequest)
        {
            if (!selectedEntry.HasValue)
            {
                return "LMB: CLOSE";
            }
            if (selectedItem.HasValue && ReferenceEquals(selectedItem.Value.Item, _pendingItemOnOpen))
            {
                return "CURRENTLY PENDING  •  LMB: KEEP";
            }
            return selectedEntry.Value.IsUsable ? "LMB: REPLACE" : "ITEM UNAVAILABLE";
        }

        if (selectedEntry is { IsUsable: false })
        {
            return "ITEM UNAVAILABLE";
        }
        if (!selectedEntry.HasValue)
        {
            return "CENTER TO CANCEL";
        }
        if (_pendingItemOnOpen is not null)
        {
            if (selectedItem.HasValue && ReferenceEquals(selectedItem.Value.Item, _pendingItemOnOpen))
            {
                return "CURRENTLY PENDING";
            }
            return Configuration.PendingItemUseBehavior.Value == Configuration.PendingUseMode.QueueOne
                ? "RELEASE TO QUEUE"
                : "RELEASE TO REPLACE";
        }
        return selectedEntry.Value.IsFavorite ? "MMB: UNFAVORITE" : "MMB: FAVORITE";
    }

    private void ToggleSelectedFavorite()
    {
        var selectedItem = GetSelectedItem();
        if (!selectedItem.HasValue)
        {
            return;
        }

        _inventory.ToggleFavorite(selectedItem.Value);
        PlaySound(EUISoundType.MenuCheckBox);
        PopulateEntries();
        RefreshWheel();
    }

    private void UpdateSelection()
    {
        var previousSelectedIndex = _selectedIndex;
        var mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        if (mouseDelta.sqrMagnitude > 100f)
        {
            mouseDelta = mouseDelta.normalized * 10f;
        }

        _selectionVector = Vector2.ClampMagnitude(
            _selectionVector + mouseDelta * MouseSelectionSpeed,
            MaximumSelectionRadius);
        UpdateSelectedIndex();
        if (_selectedIndex >= 0 && _selectedIndex != previousSelectedIndex)
        {
            PlaySound(EUISoundType.ButtonOver);
        }
    }

    private void UpdateSelectedIndex()
    {
        if (_pageItemCount == 0 || _selectionVector.magnitude <= CenterCancelRadius)
        {
            _selectedIndex = -1;
            return;
        }

        var degrees = Mathf.Atan2(_selectionVector.x, _selectionVector.y) * Mathf.Rad2Deg;
        if (degrees < 0f)
        {
            degrees += 360f;
        }
        var slice = QuickUseWheelGeometry.GetSliceDegrees(_pageItemCount);
        var candidateIndex = Mathf.FloorToInt((degrees + slice * 0.5f) / slice) % _pageItemCount;
        _selectedIndex = !GetPageEntry(candidateIndex).IsQueued
            ? candidateIndex
            : -1;
    }

    private QuickUseWheelEntry? GetSelectedEntry()
    {
        return _selectedIndex >= 0 && _selectedIndex < _pageItemCount
            ? GetPageEntry(_selectedIndex)
            : null;
    }

    private QuickUseWheelItem? GetSelectedItem()
    {
        var itemIndex = _pageStartIndex + _selectedIndex;
        return _mode == WheelMode.Items
            && _selectedIndex >= 0
            && _selectedIndex < _pageItemCount
            && itemIndex < _inventory.Items.Count
                ? _inventory.Items[itemIndex]
                : null;
    }

    private WeaponDeviceWheelItem? GetSelectedDevice()
    {
        var itemIndex = _pageStartIndex + _selectedIndex;
        return _mode == WheelMode.WeaponDevices
            && _selectedIndex >= 0
            && _selectedIndex < _pageItemCount
            && itemIndex < _deviceInventory.Items.Count
                ? _deviceInventory.Items[itemIndex]
                : null;
    }

    private void UpdatePageRange()
    {
        _pageStartIndex = _page * ItemsPerPage;
        _pageItemCount = Mathf.Min(ItemsPerPage, Mathf.Max(0, _entries.Count - _pageStartIndex));
    }

    private QuickUseWheelEntry GetPageEntry(int pageIndex) => _entries[_pageStartIndex + pageIndex];

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

    private static int Mod(int value, int modulo) => (value % modulo + modulo) % modulo;

    private void PlaySound(EUISoundType soundType)
    {
        _ui.PlaySound(Configuration.QuickUseWheelSounds.Value, soundType, "Quick-use wheel");
    }
}
