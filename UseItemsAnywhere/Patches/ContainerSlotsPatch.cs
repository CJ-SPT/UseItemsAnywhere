using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.Integration;

namespace UseItemsAnywhere.Patches;

public class ContainerSlotsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(
            typeof(InventoryEquipment),
            nameof(InventoryEquipment.ContainerSlots)
        );
    }

    [PatchPrefix]
    public static bool PatchPrefix(InventoryEquipment __instance, ref IReadOnlyList<Slot> __result)
    {
        if (!CoopRuntime.FeaturesEnabled) return true;

        __result = new Slot?[]
        {
            InventorySlotQueries.ExistingSlot(__instance, EquipmentSlot.Backpack),
            InventorySlotQueries.ExistingSlot(__instance, EquipmentSlot.TacticalVest),
            InventorySlotQueries.ExistingSlot(__instance, EquipmentSlot.Pockets),
            InventorySlotQueries.ExistingSlot(__instance, EquipmentSlot.ArmBand),
            InventorySlotQueries.ExistingSlot(__instance, EquipmentSlot.SecuredContainer),
        }.OfType<Slot>().ToArray();
        return false;
    }
}
