using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace UseItemsAnywhere.Patches;

internal class GrenadeThrowingSlotsPatch : ModulePatch
{
    private static List<Slot>? _grenadeSlots = [];

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(InventoryEquipment), nameof(InventoryEquipment.GrenadeThrowingSlots));
    }

    [PatchPrefix]
    public static bool PatchPrefix(InventoryEquipment __instance, ref IReadOnlyList<Slot> __result)
    { 
        _grenadeSlots ??=
        [
            __instance.GetSlot(EquipmentSlot.TacticalVest),
            __instance.GetSlot(EquipmentSlot.Pockets),
            __instance.GetSlot(EquipmentSlot.ArmBand)
        ];

        __result = _grenadeSlots;
        return false;
    }
}