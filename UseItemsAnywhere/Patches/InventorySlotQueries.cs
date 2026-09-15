using System;
using System.Collections.Generic;
using EFT.InventoryLogic;

namespace UseItemsAnywhere.Patches;

internal static class InventorySlotQueries
{
    internal static IEnumerable<TItem> ReloadItems<TItem>(InventoryController controller, Predicate<TItem>? predicate)
        where TItem : Item
    {
        var items = new List<TItem>();
        CollectReloadItems(controller, items, predicate);
        return items;
    }

    internal static void CollectReloadItems<TItem>(InventoryController controller, IList<TItem> items, Predicate<TItem>? predicate)
        where TItem : Item => Collect(controller, Configuration.ReloadSlots.Value, items, predicate);

    internal static void CollectGrenades<TItem>(InventoryController controller, IList<TItem> items, Predicate<TItem>? predicate)
        where TItem : Item => Collect(controller, Configuration.GrenadeThrowSlots.Value, items, predicate);

    internal static void CollectNativeItems<TItem>(InventoryController controller, IList<TItem> items, Predicate<TItem>? predicate)
        where TItem : Item => Collect(controller, Inventory.FastAccessSlots, items, predicate);

    private static void Collect<TItem>(InventoryController controller, IEnumerable<EquipmentSlot> requestedSlots,
        IList<TItem> items, Predicate<TItem>? predicate) where TItem : Item
    {
        var equipment = controller.Inventory.Equipment;
        // GetSlot indexes this cache directly. Bots/modded equipment can have a shorter
        // cache or an unpopulated slot even when the enum value exists on the player.
        var slots = SelectExistingSlots(requestedSlots, equipment._cachedSlots);
        controller.GetAcceptableItemsNonAlloc(slots, items, predicate, null);
    }

    internal static EquipmentSlot[] SelectExistingSlots(IEnumerable<EquipmentSlot> requestedSlots, Slot[] cachedSlots)
    {
        var result = new List<EquipmentSlot>();
        foreach (var slot in requestedSlots)
        {
            var index = (int)slot;
            if (index >= 0 && index < cachedSlots.Length && cachedSlots[index] != null && !result.Contains(slot))
            {
                result.Add(slot);
            }
        }
        return result.ToArray();
    }
}
