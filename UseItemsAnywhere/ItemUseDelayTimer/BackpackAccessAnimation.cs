using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace UseItemsAnywhere.ItemUseDelayTimer;

/// <summary>
///     Drives Tarkov's native third-person inventory animation while a delayed
///     backpack item is being retrieved. The hands-controller inventory state is
///     intentionally left alone so the following item-use operation is not blocked.
/// </summary>
internal sealed class BackpackAccessAnimation
{
    private const float CrouchedPoseLevel = 0f;
    private const float PoseTolerance = 0.01f;

    private readonly Player _player;
    private readonly float _originalPoseLevel;
    private bool _changedPose;
    private bool _inventoryAnimationStarted;
    private bool _finished;

    private BackpackAccessAnimation(Player player)
    {
        _player = player;
        _originalPoseLevel = player.PoseLevel;
    }

    internal static BackpackAccessAnimation? Begin(
        Player player,
        Configuration.ItemAccessDelayInfo delayInfo)
    {
        if (!Configuration.AnimateBackpackAccess.Value
            || delayInfo.SourceSlot != EquipmentSlot.Backpack
            || !player
            || player.IsInPronePose
            || player.IsInventoryOpened)
        {
            return null;
        }

        var animation = new BackpackAccessAnimation(player);
        animation.Start();
        return animation;
    }

    internal void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        if (!_player)
        {
            return;
        }

        if (_inventoryAnimationStarted && !_player.IsInventoryOpened)
        {
            _player.MovementContext?.PlayerAnimator?.SetInventory(false);
            _player.OnInventoryInteraction(false, false);
        }

        // Respect a stance change made by the player during the delay. We only
        // restore the captured pose if the player is still at the crouch level
        // that this animation requested.
        if (_changedPose
            && !_player.IsInPronePose
            && Mathf.Abs(_player.PoseLevel - CrouchedPoseLevel) <= PoseTolerance)
        {
            _player.ChangePose(_originalPoseLevel - _player.PoseLevel);
        }
    }

    private void Start()
    {
        if (_originalPoseLevel > CrouchedPoseLevel + PoseTolerance)
        {
            _player.ChangePose(CrouchedPoseLevel - _originalPoseLevel);
            _changedPose = Mathf.Abs(_player.PoseLevel - _originalPoseLevel) > PoseTolerance;
        }

        var playerAnimator = _player.MovementContext?.PlayerAnimator;
        if (playerAnimator == null)
        {
            return;
        }

        playerAnimator.SetInventory(true);
        _player.OnInventoryInteraction(true, false);
        _inventoryAnimationStarted = true;
    }
}
