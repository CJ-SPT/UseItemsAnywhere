using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace UseItemsAnywhere.Patches;

internal class GrenadeThrowingSlotsPatch : ModulePatch
{
    private static HashSet<Slot>? _grenadeThrowingSlots;
    
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(
            typeof(InventoryEquipment),
            nameof(InventoryEquipment.GrenadeThrowingSlots)
        );
    }

    [PatchPrefix]
    public static bool PatchPrefix(InventoryEquipment __instance, ref IReadOnlyList<Slot> __result)
    {
        if (_grenadeThrowingSlots == null)
        {
            _grenadeThrowingSlots = [];
        }
        else
        {
            _grenadeThrowingSlots.Clear();
        }
        
        foreach (var eSlot in Configuration.GrenadeThrowSlots.Value)
        {
            _grenadeThrowingSlots.Add(__instance.GetSlot(eSlot));
        }
        
        __result = [.._grenadeThrowingSlots];
        return false;
    }
}
