using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace UseItemsAnywhere.Patches;

internal class GrenadeThrowingSlotsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(InventoryEquipment), nameof(InventoryEquipment.GrenadeThrowingSlots));
    }

    [PatchPrefix]
    public static bool PatchPrefix(InventoryEquipment __instance, ref IReadOnlyList<Slot> __result)
    { 
        __instance.PaymentSlots_1 ??= new List<Slot>
        {
            __instance.GetSlot(EquipmentSlot.Backpack),
            __instance.GetSlot(EquipmentSlot.TacticalVest),
            __instance.GetSlot(EquipmentSlot.Pockets),
            __instance.GetSlot(EquipmentSlot.SecuredContainer),
            __instance.GetSlot(EquipmentSlot.ArmBand)
        };

        __result = __instance.PaymentSlots_1;
        return false;
    }
}