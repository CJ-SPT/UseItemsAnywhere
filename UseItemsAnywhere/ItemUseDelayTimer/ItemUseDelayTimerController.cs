using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UseItemsAnywhere.UI;

namespace UseItemsAnywhere.ItemUseDelayTimer;

internal sealed class ItemUseDelayTimerController
{
    private readonly ItemUseDelayTimerView _view = new();
    private RuntimeUiService _ui = null!;
    private Player? _player;
    private int _nextPresentationId;
    private int _activePresentationId;

    internal void Initialize(RuntimeUiService ui)
    {
        _ui = ui;
        _view.Initialize(ui);
    }

    internal ItemUseDelayPresentation? Begin(
        Player player,
        Item item,
        Configuration.ItemAccessDelayInfo delayInfo,
        Item? queuedItem = null)
    {
        if (!_view.IsAvailable || !IsCurrentLocalPlayer(player))
        {
            return null;
        }

        HideImmediately();
        var presentationId = ++_nextPresentationId;
        _activePresentationId = presentationId;
        _player = player;
        _ui.SetItemCachePlayer(player);
        _view.Show(item, delayInfo, queuedItem);
        return new ItemUseDelayPresentation(this, presentationId);
    }

    internal void SetQueuedItem(Player player, Item? queuedItem)
    {
        if (_view.IsVisible && ReferenceEquals(_player, player))
        {
            _view.SetQueuedItem(queuedItem);
        }
    }

    internal void ShowWaitingForCurrentUse(Player player, Item currentItem, Item? queuedItem)
    {
        if (!Configuration.ShowTimerPanel.Value
            || !_view.IsAvailable
            || !IsCurrentLocalPlayer(player)
            || queuedItem is null)
        {
            return;
        }

        HideImmediately();
        _player = player;
        _ui.SetItemCachePlayer(player);
        _view.ShowWaitingForCurrentUse(currentItem, queuedItem);
    }

    internal void EndWaitingForCurrentUse(Player player)
    {
        if (_activePresentationId == 0 && ReferenceEquals(_player, player))
        {
            HideImmediately();
        }
    }

    internal void Update()
    {
        if (!_view.IsVisible)
        {
            return;
        }

        if (!IsCurrentLocalPlayer(_player))
        {
            HideImmediately();
            return;
        }

        _view.Update();
        if (!_view.IsVisible)
        {
            _activePresentationId = 0;
            _player = null;
        }
    }

    internal void OnDestroy()
    {
        HideImmediately();
        _view.Destroy();
    }

    internal void SetRemaining(int presentationId, float remaining)
    {
        if (!_view.IsVisible || presentationId != _activePresentationId)
        {
            return;
        }

        _view.SetRemaining(remaining);
    }

    internal void End(int presentationId, bool completed)
    {
        if (presentationId != _activePresentationId)
        {
            return;
        }

        _activePresentationId = 0;
        _view.ShowResult(completed);
    }

    private void HideImmediately()
    {
        _activePresentationId = 0;
        _player = null;
        _view.HideImmediately();
    }

    private static bool IsCurrentLocalPlayer(Player? player)
    {
        return player is not null
            && player
            && Singleton<IBotGame>.Instance is LocalGame localGame
            && localGame.PlayerOwner
            && ReferenceEquals(localGame.PlayerOwner.Player, player);
    }
}
