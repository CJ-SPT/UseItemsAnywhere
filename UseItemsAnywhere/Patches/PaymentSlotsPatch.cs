using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.Integration;

namespace UseItemsAnywhere.Patches;

public class PaymentSlotsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(
            typeof(InventoryEquipment),
            nameof(InventoryEquipment.PaymentSlots)
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
            InventorySlotQueries.ExistingSlot(__instance, EquipmentSlot.SecuredContainer),
            InventorySlotQueries.ExistingSlot(__instance, EquipmentSlot.ArmBand),
        }.OfType<Slot>().ToArray();

        return false;
    }
}
