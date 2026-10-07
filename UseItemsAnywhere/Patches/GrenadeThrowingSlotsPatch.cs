using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.Integration;

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
        if (!CoopRuntime.FeaturesEnabled) return true;

        if (_grenadeThrowingSlots == null)
        {
            _grenadeThrowingSlots = [];
        }
        else
        {
            _grenadeThrowingSlots.Clear();
        }
        
        foreach (var eSlot in Configuration.ActiveRules.Slots(ItemCategory.Grenades))
        {
            var slot = InventorySlotQueries.ExistingSlot(__instance, eSlot);
            if (slot != null) _grenadeThrowingSlots.Add(slot);
        }
        
        __result = [.._grenadeThrowingSlots];
        return false;
    }
}
