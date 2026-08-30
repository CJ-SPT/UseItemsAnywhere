using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace UseItemsAnywhere.Patches;

/// <summary>
///     TODO: This will require Fika Sync at some point. Only handles basic items such as meds, food, water...
/// </summary>
internal sealed class ItemAccessDelayPatch : ModulePatch
{
    private static readonly Dictionary<Player, PendingAccess> PendingPlayers = [];
    private static readonly Dictionary<Player, Item> WaitingForCurrentUse = [];
    private static readonly HashSet<Player> BypassPlayers = [];

    internal static void ClearPendingItemAccess()
    {
        foreach (var pendingAccess in PendingPlayers.Values)
        {
            pendingAccess.FollowUp = null;
            pendingAccess.IsCancelled = true;
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

        item = null!;
        return false;
    }

    internal static bool IsQueuedForAccess(Player player, Item item)
    {
        return WaitingForCurrentUse.TryGetValue(player, out var waitingItem)
                && ReferenceEquals(waitingItem, item)
            || PendingPlayers.TryGetValue(player, out var pendingAccess)
                && (ReferenceEquals(pendingAccess.Item, item)
                    || pendingAccess.FollowUp is { } followUp
                    && ReferenceEquals(followUp.Item, item));
    }

    internal static bool ReplacePendingItemAccess(
        Player player,
        Item item,
        Callback<IHandsController> completeCallback,
        bool scheduled)
    {
        if (!PendingPlayers.TryGetValue(player, out var pendingAccess)
            || ReferenceEquals(pendingAccess.Item, item))
        {
            return false;
        }

        Configuration.ItemAccessDelayInfo? delayInfo = null;
        if (ShouldDelay(item)
            && Configuration.TryGetItemAccessDelay(player.InventoryController.Inventory, item, out var replacementDelay)
            && replacementDelay.TotalDelay > 0f)
        {
            delayInfo = replacementDelay;
        }

        pendingAccess.FollowUp = new PendingRequest(item, completeCallback, true, delayInfo);
        pendingAccess.IsCancelled = true;
        return true;
    }

    internal static bool QueuePendingItemAccess(
        Player player,
        Item item,
        Callback<IHandsController> completeCallback,
        bool scheduled)
    {
        if (!PendingPlayers.TryGetValue(player, out var pendingAccess)
            || pendingAccess.FollowUp.HasValue
            || ReferenceEquals(pendingAccess.Item, item))
        {
            return false;
        }

        Configuration.ItemAccessDelayInfo? delayInfo = null;
        if (ShouldDelay(item)
            && Configuration.TryGetItemAccessDelay(player.InventoryController.Inventory, item, out var queuedDelay)
            && queuedDelay.TotalDelay > 0f)
        {
            delayInfo = queuedDelay;
        }

        pendingAccess.FollowUp = new PendingRequest(item, completeCallback, true, delayInfo, true);
        return true;
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

        if (PendingPlayers.TryGetValue(__instance, out var pendingAccess))
        {
            Configuration.ItemAccessDelayInfo? queuedDelayInfo = null;
            if (ShouldDelay(item)
                && Configuration.TryGetItemAccessDelay(
                    __instance.InventoryController.Inventory,
                    item,
                    out var queuedDelay)
                && queuedDelay.TotalDelay > 0f)
            {
                queuedDelayInfo = queuedDelay;
            }

            var queuedRequest = new PendingRequest(item, completeCallback, scheduled, queuedDelayInfo);
            switch (Configuration.PendingItemUseBehavior.Value)
            {
                case Configuration.PendingUseMode.CancelAndReplace:
                    pendingAccess.FollowUp = queuedRequest;
                    pendingAccess.IsCancelled = true;
                    break;
                case Configuration.PendingUseMode.QueueOne:
                    pendingAccess.FollowUp ??= queuedRequest.AfterCurrentItemIsUsed();
                    break;
                case Configuration.PendingUseMode.Ignore:
                    break;
                case Configuration.PendingUseMode.OpenWheel:
                    QuickUseWheel.RequestPendingOpen(__instance);
                    break;
                default:
                    break;
            }

            return false;
        }

        if (!ShouldDelay(item)
            || !Configuration.TryGetItemAccessDelay(
                __instance.InventoryController.Inventory,
                item,
                out var delayInfo)
            || delayInfo.TotalDelay <= 0f)
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
        var pendingAccess = new PendingAccess(request.Item);
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
        ItemUseDelayTimer.Presentation? presentation = null;
        var completed = false;
        try
        {
            if (Configuration.ShowTimerPanel.Value)
            {
                presentation = Plugin.DelayTimer?.Begin(player, request.Item, delayInfo);
            }

            var delayEndTime = Time.time + delayInfo.TotalDelay;
            while (Time.time < delayEndTime && !pendingAccess.IsCancelled)
            {
                presentation?.SetRemaining(delayEndTime - Time.time);
                yield return null;
            }

            if (pendingAccess.IsCancelled || !player)
            {
                yield break;
            }

            var completeCallback = request.CompleteCallback;
            if (pendingAccess.FollowUp is { StartAfterCurrentUse: true } queuedFollowUp)
            {
                pendingAccess.FollowUp = null;
                WaitingForCurrentUse[player] = queuedFollowUp.Item;
                completeCallback = result =>
                {
                    request.CompleteCallback?.Invoke(result);

                    if (result.Failed || !player || result.Value is not IQuickUseItem quickUseItem)
                    {
                        WaitingForCurrentUse.Remove(player);
                        return;
                    }

                    var restorePreviousItem = quickUseItem.GetOnUsedCallback();
                    quickUseItem.SetOnUsedCallback(useResult =>
                    {
                        restorePreviousItem?.Invoke(useResult);
                        WaitingForCurrentUse.Remove(player);

                        if (useResult.Succeed && player)
                        {
                            StartFollowUp(player, queuedFollowUp);
                        }
                    });
                };
            }

            BypassPlayers.Add(player);
            player.TryProceed(request.Item, completeCallback, request.Scheduled);
            completed = true;
        }
        finally
        {
            presentation?.Finish(completed);
            BypassPlayers.Remove(player);
            if (!completed)
            {
                WaitingForCurrentUse.Remove(player);
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

    private sealed class PendingAccess(Item item)
    {
        internal Item Item { get; set; } = item;
        internal bool IsCancelled;
        internal PendingRequest? FollowUp;
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
