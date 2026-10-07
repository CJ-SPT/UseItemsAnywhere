using System.Linq;
using System;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.Extensions;
using UseItemsAnywhere.Integration;

namespace UseItemsAnywhere.Patches;

public class IsAtReachablePlace : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAtReachablePlace));
    }

    [PatchPostfix]
    private static void Postfix(InventoryController __instance, ref bool __result, Item item)
    {
        if (!CoopRuntime.FeaturesEnabled) return;

        switch (item)
        {
            case Weapon weap:
                if (Configuration.FlareIds.Contains(weap.TemplateId))
                {
                    __result = __instance.Inventory.SlotsContainItem(Configuration.ActiveRules.Slots(ItemCategory.Flares), weap);
                    return;
                }
                
                __result = __instance.Inventory.SlotsContainItem(Configuration.AllAllowedWeaponSlots, item);
                return;
            case ThrowWeap:
                __result = __instance.Inventory.SlotsContainItem(Configuration.ActiveRules.Slots(ItemCategory.Grenades), item);
                return;
            case Ammo:
            case Magazine:
                __result = __instance.Inventory.SlotsContainItem(Configuration.ActiveRules.Slots(ItemCategory.Reload), item);
                return;
            case Meds:
                __result = __instance.Inventory.SlotsContainItem(Configuration.ActiveRules.Slots(ItemCategory.Meds), item);
                return;
            case FoodDrink:
                __result = __instance.Inventory.SlotsContainItem(Configuration.ActiveRules.Slots(ItemCategory.FoodDrink), item);
                return;
            default:
                if (item.GetItemComponent<KnifeComponent>() != null)
                {
                    __result = __instance.Inventory.SlotsContainItem(Configuration.AllAllowedMeleeSlots, item);
                    return;
                }
                
                
                __result = __instance.Inventory.SlotsContainItem(Configuration.ActiveRules.Slots(ItemCategory.Other), item);
                return;
        }
    }

    [PatchFinalizer]
    private static Exception? Finalizer(
        Exception? __exception,
        InventoryController __instance,
        ref bool __result,
        Item item)
    {
        if (!CoopRuntime.FeaturesEnabled || __exception is not NullReferenceException
            || __exception.StackTrace?.Contains(
                "PackNStrap.Helpers.Common.IsItemInReachableLocation") != true)
        {
            return __exception;
        }

        // Pack 'n Strap can pass a null CompoundItem to GetTopLevelItems when one
        // of its reachable equipment slots contains a non-container. Recover only
        // from that known compatibility failure and apply this mod's slot rules.
        try
        {
            Postfix(__instance, ref __result, item);
            return null;
        }
        catch
        {
            return __exception;
        }
    }
}
