using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace UseItemsAnywhere.Patches;

internal class GetThrowablePriorityGrenadesListPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(InventoryExtension),
            nameof(InventoryExtension.GetThrowablePriorityGrenadesList)
        );
    }

    [PatchPrefix]
    public static bool PatchPrefix(
        ref List<ThrowWeap> __result,
        InventoryController inventoryController
    )
    {
        var list = new List<CompoundItem>();
        if (
            inventoryController
                .Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest)
                .ContainedItem
            is CompoundItem compoundItem
        )
        {
            list.Add(compoundItem);
        }

        if (
            inventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem
            is CompoundItem compoundItem2
        )
        {
            list.Add(compoundItem2);
        }

        if (
            inventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.ArmBand).ContainedItem
            is CompoundItem compoundItem3
        )
        {
            list.Add(compoundItem3);
        }

        if (
            inventoryController
                .Inventory.Equipment.GetSlot(EquipmentSlot.SecuredContainer)
                .ContainedItem
            is CompoundItem compoundItem4
        )
        {
            list.Add(compoundItem4);
        }

        if (
            inventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem
            is CompoundItem compoundItem5
        )
        {
            list.Add(compoundItem5);
        }

        var list2 = list.GetTopLevelItems()
            .OfType<ThrowWeap>()
            .Where(inventoryController.Examined)
            .ToList();

        list2.Sort(InventoryExtension.CG_Class2411.CG_Class2411.method_3);

        __result = list2;
        return false;
    }
}
