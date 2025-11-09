using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace UseItemsAnywhere.Patches;

public class ContainerSlotsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(InventoryEquipment), nameof(InventoryEquipment.ContainerSlots));
    }

    [PatchPrefix]
    public static bool PatchPrefix(InventoryEquipment __instance, ref IReadOnlyList<Slot> __result)
    {
        __instance.ContainerSlots_1 ??= new List<Slot>
        {
            __instance.GetSlot(EquipmentSlot.Backpack),
            __instance.GetSlot(EquipmentSlot.TacticalVest),
            __instance.GetSlot(EquipmentSlot.Pockets),
            __instance.GetSlot(EquipmentSlot.ArmBand),
            __instance.GetSlot(EquipmentSlot.SecuredContainer)
        };
        __result = __instance.ContainerSlots_1;
        return false;
    }
}