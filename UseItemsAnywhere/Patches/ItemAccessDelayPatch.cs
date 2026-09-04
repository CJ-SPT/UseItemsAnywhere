using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using UseItemsAnywhere.ItemUseDelayTimer;
using UseItemsAnywhere.QuickUseWheel;

namespace UseItemsAnywhere.Patches;

/// <summary>
///     TODO: This will require Fika Sync at some point. Only handles basic items such as meds, food, water...
/// </summary>
internal sealed class ItemAccessDelayPatch : ModulePatch
{
    private const float MovementInputThresholdSqr = 0.0001f;

    private static readonly Dictionary<Player, PendingAccess> PendingPlayers = [];
    private static readonly Dictionary<Player, WaitingAccess> WaitingForCurrentUse = [];
    private static readonly HashSet<Player> BypassPlayers = [];

    internal static void ClearPendingItemAccess()
    {
        foreach (var pendingAccess in PendingPlayers.Values)
        {
            pendingAccess.FollowUp = null;
            pendingAccess.IsCancelled = true;
        }
        foreach (var player in WaitingForCurrentUse.Keys)
        {
            Plugin.DelayTimer?.EndWaitingForCurrentUse(player);
        }
        WaitingForCurrentUse.Clear();
    }

    internal static bool TryGetPendingItem(Player player, out Item item)
    {
        if (PendingPlayers.TryGetValue(player, out var pendingAccess))
        {
            item = pendingAccess.Item;
            return true;
        }

        if (WaitingForCurrentUse.TryGetValue(player, out var waitingAccess))
        {
            item = waitingAccess.CurrentItem;
            return true;
        }

        item = null!;
        return false;
    }

    internal static bool IsQueuedForAccess(Player player, Item item)
    {
        return WaitingForCurrentUse.TryGetValue(player, out var waitingAccess)
                && (ReferenceEquals(waitingAccess.CurrentItem, item)
                    || waitingAccess.Next is { } waitingNext
                    && ReferenceEquals(waitingNext.Item, item))
            || PendingPlayers.TryGetValue(player, out var pendingAccess)
                && (ReferenceEquals(pendingAccess.Item, item)
                    || pendingAccess.FollowUp is { } followUp
                    && ReferenceEquals(followUp.Item, item));
    }

    internal static bool IsNextQueuedItem(Player player, Item item)
    {
        return WaitingForCurrentUse.TryGetValue(player, out var waitingAccess)
                && waitingAccess.Next is { } waitingNext
                && ReferenceEquals(waitingNext.Item, item)
            || PendingPlayers.TryGetValue(player, out var pendingAccess)
                && pendingAccess.FollowUp is { } followUp
                && ReferenceEquals(followUp.Item, item);
    }

    internal static bool TryGetQueueState(Player player, out AccessQueueState queueState)
    {
        if (PendingPlayers.TryGetValue(player, out var pendingAccess))
        {
            queueState = new AccessQueueState(pendingAccess.Item, pendingAccess.FollowUp?.Item);
            return true;
        }

        if (WaitingForCurrentUse.TryGetValue(player, out var waitingAccess))
        {
            queueState = new AccessQueueState(waitingAccess.CurrentItem, waitingAccess.Next?.Item);
            return true;
        }

        queueState = default;
        return false;
    }

    internal static bool RemoveNextQueuedItem(Player player)
    {
        if (PendingPlayers.TryGetValue(player, out var pendingAccess)
            && pendingAccess.FollowUp.HasValue)
        {
            pendingAccess.FollowUp = null;
            Plugin.DelayTimer?.SetQueuedItem(player, null);
            return true;
        }

        if (WaitingForCurrentUse.Remove(player))
        {
            Plugin.DelayTimer?.EndWaitingForCurrentUse(player);
            return true;
        }

        return false;
    }

    internal static bool TryGetEffectiveDelay(
        Player player,
        Item item,
        out Configuration.ItemAccessDelayInfo delayInfo)
    {
        delayInfo = default;
        return Configuration.EnableSlotDelays.Value
            && ShouldDelay(item)
            && Configuration.TryGetItemAccessDelay(
                player.InventoryController.Inventory,
                item,
                out delayInfo)
            && delayInfo.TotalDelay > 0f;
    }

    internal static bool ReplacePendingItemAccess(
        Player player,
        Item item,
        Callback<IHandsController> completeCallback,
        bool scheduled)
    {
        var request = CreateRequest(player, item, completeCallback, scheduled);
        if (PendingPlayers.TryGetValue(player, out var pendingAccess))
        {
            if (ReferenceEquals(pendingAccess.Item, item))
            {
                return false;
            }

            pendingAccess.FollowUp = request;
            pendingAccess.IsCancelled = true;
            Plugin.DelayTimer?.SetQueuedItem(player, item);
            return true;
        }

        if (WaitingForCurrentUse.TryGetValue(player, out var waitingAccess)
            && !ReferenceEquals(waitingAccess.CurrentItem, item))
        {
            waitingAccess.Next = request.AfterCurrentItemIsUsed();
            Plugin.DelayTimer?.SetQueuedItem(player, item);
            return true;
        }

        return false;
    }

    internal static bool QueuePendingItemAccess(
        Player player,
        Item item,
        Callback<IHandsController> completeCallback,
        bool scheduled)
    {
        var request = CreateRequest(player, item, completeCallback, scheduled)
            .AfterCurrentItemIsUsed();
        if (PendingPlayers.TryGetValue(player, out var pendingAccess))
        {
            if (ReferenceEquals(pendingAccess.Item, item))
            {
                return false;
            }

            pendingAccess.FollowUp = request;
            Plugin.DelayTimer?.SetQueuedItem(player, item);
            return true;
        }

        if (WaitingForCurrentUse.TryGetValue(player, out var waitingAccess)
            && !ReferenceEquals(waitingAccess.CurrentItem, item))
        {
            waitingAccess.Next = request;
            Plugin.DelayTimer?.SetQueuedItem(player, item);
            return true;
        }

        return false;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(Player),
            nameof(Player.TryProceed),
            [typeof(Item), typeof(Callback<IHandsController>), typeof(bool)]);
    }

    [PatchPrefix]
    private static bool Prefix(
        Player __instance,
        Item item,
        Callback<IHandsController> completeCallback,
        bool scheduled)
    {
        if (!Configuration.EnableSlotDelays.Value)
        {
            return true;
        }

        if (BypassPlayers.Remove(__instance))
        {
            return true;
        }

        if (__instance.IsAI)
        {
            return true;
        }

        if (WaitingForCurrentUse.TryGetValue(__instance, out var waitingAccess))
        {
            if (ReferenceEquals(waitingAccess.CurrentItem, item))
            {
                return false;
            }

            var waitingRequest = CreateRequest(__instance, item, completeCallback, scheduled)
                .AfterCurrentItemIsUsed();
            switch (Configuration.PendingItemUseBehavior.Value)
            {
                case Configuration.PendingUseMode.CancelAndReplace:
                    waitingAccess.Next = waitingRequest;
                    Plugin.DelayTimer?.SetQueuedItem(__instance, item);
                    break;
                case Configuration.PendingUseMode.QueueOne:
                    if (!waitingAccess.Next.HasValue)
                    {
                        waitingAccess.Next = waitingRequest;
                        Plugin.DelayTimer?.SetQueuedItem(__instance, item);
                    }
                    break;
                case Configuration.PendingUseMode.OpenWheel:
                    QuickUseWheelController.RequestPendingOpen(__instance);
                    break;
            }
            return false;
        }

        if (PendingPlayers.TryGetValue(__instance, out var pendingAccess))
        {
            var queuedRequest = CreateRequest(__instance, item, completeCallback, scheduled);
            switch (Configuration.PendingItemUseBehavior.Value)
            {
                case Configuration.PendingUseMode.CancelAndReplace:
                    pendingAccess.FollowUp = queuedRequest;
                    pendingAccess.IsCancelled = true;
                    Plugin.DelayTimer?.SetQueuedItem(__instance, item);
                    break;
                case Configuration.PendingUseMode.QueueOne:
                    if (!pendingAccess.FollowUp.HasValue)
                    {
                        pendingAccess.FollowUp = queuedRequest.AfterCurrentItemIsUsed();
                        Plugin.DelayTimer?.SetQueuedItem(__instance, item);
                    }
                    break;
                case Configuration.PendingUseMode.Ignore:
                    break;
                case Configuration.PendingUseMode.OpenWheel:
                    QuickUseWheelController.RequestPendingOpen(__instance);
                    break;
                default:
                    break;
            }

            return false;
        }

        if (!TryGetEffectiveDelay(__instance, item, out var delayInfo))
        {
            return true;
        }

        var request = new PendingRequest(item, completeCallback, scheduled, delayInfo);
        StartPendingAccess(__instance, request, delayInfo);
        return false;
    }

    private static void StartPendingAccess(
        Player player,
        PendingRequest request,
        Configuration.ItemAccessDelayInfo delayInfo)
    {
        var pendingAccess = new PendingAccess(request.Item, delayInfo);
        PendingPlayers.Add(player, pendingAccess);
        player.StartCoroutine(ProceedAfterDelay(player, request, delayInfo, pendingAccess));
    }

    private static bool ShouldDelay(Item item)
    {
        if (item is Meds or FoodDrink)
        {
            return true;
        }

        if (item is Weapon or ThrowWeap or PortableRangeFinder or RadioTransmitter)
        {
            return false;
        }

        if (item.GetItemComponent<KnifeComponent>() != null)
        {
            return false;
        }

        return item.UsePrefab != null;
    }

    private static IEnumerator ProceedAfterDelay(
        Player player,
        PendingRequest request,
        Configuration.ItemAccessDelayInfo delayInfo,
        PendingAccess pendingAccess)
    {
        ItemUseDelayPresentation? presentation = null;
        BackpackAccessAnimation? backpackAnimation = null;
        var healthController = player.HealthController;
        Action<EBodyPart, float, DamageInfo>? damageHandler = null;
        var completed = false;
        try
        {
            if (healthController != null)
            {
                damageHandler = (_, damage, _) =>
                {
                    if (Configuration.CancelAccessOnDamage.Value && damage > 0f)
                    {
                        CancelPendingAccess(player, pendingAccess);
                    }
                };
                healthController.ApplyDamageEvent += damageHandler;
            }

            backpackAnimation = BackpackAccessAnimation.Begin(player, delayInfo);

            if (Configuration.ShowTimerPanel.Value)
            {
                presentation = Plugin.DelayTimer?.Begin(
                    player,
                    request.Item,
                    delayInfo,
                    pendingAccess.FollowUp?.Item);
            }

            var delayEndTime = Time.time + delayInfo.TotalDelay;
            while (Time.time < delayEndTime && !pendingAccess.IsCancelled)
            {
                if (ShouldCancelForMovement(player))
                {
                    CancelPendingAccess(player, pendingAccess);
                    break;
                }

                presentation?.SetRemaining(delayEndTime - Time.time);
                yield return null;
            }

            if (pendingAccess.IsCancelled || !player)
            {
                yield break;
            }

            if (!QuickUseWheelInventory.IsItemStillUsable(player, request.Item))
            {
                yield break;
            }

            var completeCallback = request.CompleteCallback;
            if (pendingAccess.FollowUp is { StartAfterCurrentUse: true } queuedFollowUp)
            {
                pendingAccess.FollowUp = null;
                var waitingAccess = new WaitingAccess(request.Item, queuedFollowUp);
                WaitingForCurrentUse[player] = waitingAccess;
                completeCallback = result =>
                {
                    request.CompleteCallback?.Invoke(result);

                    if (result.Failed || !player || result.Value is not IQuickUseItem quickUseItem)
                    {
                        if (WaitingForCurrentUse.TryGetValue(player, out var currentWaiting)
                            && ReferenceEquals(currentWaiting, waitingAccess))
                        {
                            WaitingForCurrentUse.Remove(player);
                            Plugin.DelayTimer?.EndWaitingForCurrentUse(player);
                        }
                        return;
                    }

                    var restorePreviousItem = quickUseItem.GetOnUsedCallback();
                    quickUseItem.SetOnUsedCallback(useResult =>
                    {
                        restorePreviousItem?.Invoke(useResult);
                        if (!WaitingForCurrentUse.TryGetValue(player, out var currentWaiting)
                            || !ReferenceEquals(currentWaiting, waitingAccess))
                        {
                            return;
                        }

                        WaitingForCurrentUse.Remove(player);
                        Plugin.DelayTimer?.EndWaitingForCurrentUse(player);
                        if (useResult.Succeed && player && currentWaiting.Next is { } next)
                        {
                            StartFollowUp(player, next);
                        }
                    });
                    Plugin.DelayTimer?.ShowWaitingForCurrentUse(
                        player,
                        request.Item,
                        waitingAccess.Next?.Item);
                };
            }

            var resultPresentation = presentation;
            var resultCallback = completeCallback;
            completeCallback = result =>
            {
                resultPresentation?.Finish(result.Succeed);
                resultCallback?.Invoke(result);
            };

            BypassPlayers.Add(player);
            player.TryProceed(request.Item, completeCallback, request.Scheduled);
            presentation = null;
            completed = true;
        }
        finally
        {
            if (damageHandler != null && healthController != null)
            {
                healthController.ApplyDamageEvent -= damageHandler;
            }

            backpackAnimation?.Finish();
            presentation?.Finish(completed);
            BypassPlayers.Remove(player);
            if (!completed)
            {
                if (WaitingForCurrentUse.Remove(player))
                {
                    Plugin.DelayTimer?.EndWaitingForCurrentUse(player);
                }
            }

            if (PendingPlayers.TryGetValue(player, out var current)
                && ReferenceEquals(current, pendingAccess))
            {
                PendingPlayers.Remove(player);
            }

            var followUp = pendingAccess.FollowUp;
            if (followUp.HasValue && player)
            {
                StartFollowUp(player, followUp.Value);
            }
        }
    }

    private static void CancelPendingAccess(Player player, PendingAccess pendingAccess)
    {
        pendingAccess.FollowUp = null;
        pendingAccess.IsCancelled = true;
        Plugin.DelayTimer?.SetQueuedItem(player, null);
    }

    private static bool ShouldCancelForMovement(Player player)
    {
        return Configuration.CancelAccessOnMovement.Value
            && player
            && player.MovementContext is { } movementContext
            && movementContext.MovementDirection.sqrMagnitude > MovementInputThresholdSqr;
    }

    private static void StartFollowUp(Player player, PendingRequest request)
    {
        WaitingForCurrentUse.Remove(player);
        if (Configuration.EnableSlotDelays.Value && request.DelayInfo.HasValue)
        {
            StartPendingAccess(player, request, request.DelayInfo.Value);
            return;
        }

        BypassPlayers.Add(player);
        player.TryProceed(request.Item, request.CompleteCallback, request.Scheduled);
        BypassPlayers.Remove(player);
    }

    private static PendingRequest CreateRequest(
        Player player,
        Item item,
        Callback<IHandsController> completeCallback,
        bool scheduled)
    {
        Configuration.ItemAccessDelayInfo? delayInfo = null;
        if (TryGetEffectiveDelay(player, item, out var effectiveDelay))
        {
            delayInfo = effectiveDelay;
        }
        return new PendingRequest(item, completeCallback, scheduled, delayInfo);
    }

    private sealed class PendingAccess(Item item, Configuration.ItemAccessDelayInfo delayInfo)
    {
        internal Item Item { get; set; } = item;
        internal Configuration.ItemAccessDelayInfo DelayInfo { get; } = delayInfo;
        internal bool IsCancelled;
        internal PendingRequest? FollowUp;
    }

    private sealed class WaitingAccess(Item currentItem, PendingRequest next)
    {
        internal Item CurrentItem { get; } = currentItem;
        internal PendingRequest? Next { get; set; } = next;
    }

    internal readonly struct AccessQueueState(Item currentItem, Item? nextItem)
    {
        internal Item CurrentItem { get; } = currentItem;
        internal Item? NextItem { get; } = nextItem;
    }

    private readonly struct PendingRequest(
        Item item,
        Callback<IHandsController> completeCallback,
        bool scheduled,
        Configuration.ItemAccessDelayInfo? delayInfo,
        bool startAfterCurrentUse = false)
    {
        internal Item Item { get; } = item;
        internal Callback<IHandsController> CompleteCallback { get; } = completeCallback;
        internal bool Scheduled { get; } = scheduled;
        internal Configuration.ItemAccessDelayInfo? DelayInfo { get; } = delayInfo;
        internal bool StartAfterCurrentUse { get; } = startAfterCurrentUse;

        internal PendingRequest AfterCurrentItemIsUsed() => new(
            Item,
            CompleteCallback,
            true,
            DelayInfo,
            true);
    }
}
