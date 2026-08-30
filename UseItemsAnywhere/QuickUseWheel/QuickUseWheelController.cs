using System;
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
    private const float CenterCancelRadius = 104f;
    private const float EmptyWheelRefreshInterval = 0.15f;
    private const float MouseSelectionSpeed = 24f;
    private const float MaximumSelectionRadius = 280f;
    private const float MaximumVisibleSegmentDegrees = 42f;

    private static readonly Callback<IHandsController> IgnoreHandsResult = _ => { };
    private static Player? _pendingOpenRequest;

    private readonly QuickUseWheelInventory _inventory = new();
    private readonly QuickUseWheelView _view = new();
    private RuntimeUiService _ui = null!;
    private ManualLogSource? _logger;
#if DEBUG
    private bool _inputDetectionLogged;
    private bool _canvasWarningLogged;
#endif
    private bool _shortcutHeld;
    private bool _isOpen;
    private bool _cancelled;
    private Vector2 _selectionVector;
    private int _page;
    private int _pageStartIndex;
    private int _pageItemCount;
    private int _selectedIndex = -1;
    private float _nextEmptyWheelRefreshTime;
    private Player? _player;
    private Item? _pendingItemOnOpen;
    private bool _openedFromPendingRequest;
    private bool _pendingClickArmed;
    private GamePlayerOwner? _playerOwner;
    private bool _previousBlockFirearms;
    private Action<Player>? _previousRotationAction;

    internal static bool InputBlocked { get; private set; }

    private static int ItemsPerPage => Configuration.QuickUseItemsPerPage.Value;

    private int PageCount => Mathf.Max(1, Mathf.CeilToInt((float)_inventory.Items.Count / ItemsPerPage));

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
        _view.Initialize(ui);
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

        HandlePendingOpenRequest();

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
            UpdateOpenWheel();
        }

        if (_shortcutHeld && !QuickUseWheelShortcut.IsMainKeyPressed(shortcut))
        {
            var useSelection = _isOpen
                && !_cancelled
                && _selectedIndex >= 0
                && GetSelectedItem()?.IsUsable == true;
            if (_isOpen)
            {
                PlaySound(useSelection ? EUISoundType.ButtonClick : EUISoundType.MenuEscape);
            }
            Close(useSelection);
        }
    }

    internal void OnDestroy()
    {
        Close(false);
        _view.Destroy();
        _inventory.Clear();
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
            Open(true);
        }
    }

    private void UpdateOpenWheel()
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
            PlaySound(EUISoundType.MenuEscape);
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

    private void Open(bool openedFromPendingRequest = false)
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
        _inventory.Populate(player);
#if DEBUG
        _logger?.LogDebug($"Opening quick-use wheel with {_inventory.Items.Count} usable item(s).");
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

        _view.Show();
        RefreshWheel();
        PlaySound(EUISoundType.MenuContextMenu);
    }

    private void RefreshUnavailableWheel()
    {
        var hasQueuedItems = _inventory.HasQueuedItems;
        var hadUsableItem = _inventory.HasUsableItems;
        if ((!hasQueuedItems && hadUsableItem)
            || _player is null
            || !_player
            || Time.unscaledTime < _nextEmptyWheelRefreshTime)
        {
            return;
        }

        _nextEmptyWheelRefreshTime = Time.unscaledTime + EmptyWheelRefreshInterval;
        _inventory.Populate(_player);
        _page = Mathf.Clamp(_page, 0, PageCount - 1);
        RefreshWheel();

#if DEBUG
        if (!hadUsableItem && _inventory.HasUsableItems)
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
        _inventory.ClearItems();
        _player = null;
        _playerOwner = null;
        _pendingItemOnOpen = null;
        _openedFromPendingRequest = false;
        _pendingClickArmed = false;
        _view.Hide();

        if (player is not null
            && player
            && selectedItem != null
            && QuickUseWheelInventory.IsItemStillUsable(player, selectedItem))
        {
            UseSelectedItem(player, selectedItem, pendingItem);
        }
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
        _view.Refresh(
            _inventory.Items,
            _pageStartIndex,
            _pageItemCount,
            _page,
            PageCount,
            _openedFromPendingRequest);
        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        var selectedItem = GetSelectedItem();
        _view.UpdatePresentation(
            _inventory.Items,
            _pageStartIndex,
            _pageItemCount,
            _selectedIndex,
            selectedItem,
            GetSelectionHint(selectedItem));
    }

    private string GetSelectionHint(QuickUseWheelItem? selectedItem)
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

    private void ToggleSelectedFavorite()
    {
        var selectedItem = GetSelectedItem();
        if (!selectedItem.HasValue)
        {
            return;
        }

        _inventory.ToggleFavorite(selectedItem.Value);
        PlaySound(EUISoundType.MenuCheckBox);
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
        var slice = 360f / _pageItemCount;
        var candidateIndex = Mathf.FloorToInt((degrees + slice * 0.5f) / slice) % _pageItemCount;
        var visibleSegmentDegrees = Mathf.Min(slice - Mathf.Min(2f, slice * 0.08f), MaximumVisibleSegmentDegrees);
        var degreesFromCandidateCenter = Mathf.Abs(Mathf.DeltaAngle(degrees, candidateIndex * slice));
        _selectedIndex = degreesFromCandidateCenter <= visibleSegmentDegrees * 0.5f
            && !GetPageItem(candidateIndex).IsQueued
            ? candidateIndex
            : -1;
    }

    private QuickUseWheelItem? GetSelectedItem()
    {
        return _selectedIndex >= 0 && _selectedIndex < _pageItemCount
            ? GetPageItem(_selectedIndex)
            : null;
    }

    private void UpdatePageRange()
    {
        _pageStartIndex = _page * ItemsPerPage;
        _pageItemCount = Mathf.Min(ItemsPerPage, Mathf.Max(0, _inventory.Items.Count - _pageStartIndex));
    }

    private QuickUseWheelItem GetPageItem(int pageIndex) => _inventory.Items[_pageStartIndex + pageIndex];

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
