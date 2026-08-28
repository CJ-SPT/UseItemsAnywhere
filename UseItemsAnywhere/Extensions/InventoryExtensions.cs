using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;

namespace UseItemsAnywhere.Extensions;

public static class InventoryExtensions
{
    public static bool SlotsContainItem(this Inventory inventory, IEnumerable<EquipmentSlot> slots, Item item)
    {
        return inventory.GetItemsInSlots(slots).Contains(item);
    }
}