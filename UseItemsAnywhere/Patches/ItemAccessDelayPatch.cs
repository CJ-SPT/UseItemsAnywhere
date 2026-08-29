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
    private static readonly HashSet<Player> BypassPlayers = [];

    internal static void ClearPendingItemAccess()
    {
        foreach (var pendingAccess in PendingPlayers.Values)
        {
            pendingAccess.IsCancelled = true;
        }
    }

    internal static bool HasPendingItemAccess(Player player) => PendingPlayers.ContainsKey(player);

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

        if (__instance.IsAI || !ShouldDelay(item))
        {
            return true;
        }

        var delay = Configuration.GetItemAccessDelay(__instance.InventoryController.Inventory, item);
        if (delay <= 0f)
        {
            return true;
        }

        if (!PendingPlayers.ContainsKey(__instance))
        {
            var pendingAccess = new PendingAccess();
            PendingPlayers.Add(__instance, pendingAccess);
            __instance.StartCoroutine(ProceedAfterDelay(
                __instance,
                item,
                completeCallback,
                scheduled,
                delay,
                pendingAccess));
        }

        return false;
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
        Item item,
        Callback<IHandsController> completeCallback,
        bool scheduled,
        float delay,
        PendingAccess pendingAccess)
    {
        var playerOwner = ((LocalGame)Singleton<IBotGame>.Instance).PlayerOwner;

        try
        {
            if (Configuration.ShowTimerPanel.Value&& playerOwner)
            {
                playerOwner.ShowObjectivesPanel(GetTimerText(item), delay);
            }

            var delayEndTime = Time.time + delay;
            while (Time.time < delayEndTime && !pendingAccess.IsCancelled)
            {
                yield return null;
            }

            if (pendingAccess.IsCancelled || !player)
            {
                yield break;
            }

            BypassPlayers.Add(player);
            player.TryProceed(item, completeCallback, scheduled);
        }
        finally
        {
            if (Configuration.ShowTimerPanel.Value&& playerOwner)
            {
                playerOwner.CloseObjectivesPanel();
            }

            PendingPlayers.Remove(player);
            BypassPlayers.Remove(player);
        }
    }

    private sealed class PendingAccess
    {
        internal bool IsCancelled;
    }
    
    private static string GetTimerText(Item item)
    {
        var itemName = item.LocalizedName();
        if (string.IsNullOrWhiteSpace(itemName))
        {
            itemName = item.ShortName;
        }

        // BattleUIPanelExtraction formats this text with the remaining time.
        // Escape any braces in a localized item name so they are not parsed as placeholders.
        itemName = itemName
            .Replace("{", "{{")
            .Replace("}", "}}");

        return $"Using {itemName} {{0:F1}}";
    }
}
