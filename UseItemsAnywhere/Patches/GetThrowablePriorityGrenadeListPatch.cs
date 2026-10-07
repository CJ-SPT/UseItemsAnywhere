using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.Integration;

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
        if (!CoopRuntime.FeaturesEnabled) return true;

        var list = Configuration.ActiveRules.Slots(ItemCategory.Grenades)
            .Select(slot => InventorySlotQueries.ExistingSlot(inventoryController.Inventory.Equipment, slot)?.ContainedItem)
            .OfType<CompoundItem>()
            .Distinct()
            .ToList();

        var list2 = list.GetTopLevelItems()
            .OfType<ThrowWeap>()
            .Where(inventoryController.Examined)
            .ToList();

        list2.Sort(InventoryExtension.CG_Class2411.CG_Class2411.method_3);

        __result = list2;
        return false;
    }
}
