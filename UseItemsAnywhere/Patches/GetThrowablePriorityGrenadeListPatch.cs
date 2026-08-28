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
        var list = Configuration.GrenadeThrowSlots.Value
            .Select(slot => inventoryController.Inventory.Equipment.GetSlot(slot).ContainedItem)
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
