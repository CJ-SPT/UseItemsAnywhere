using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using MultiFlare;
using SPT.Reflection.Patching;
using UseItemsAnywhere.Extensions;

namespace UseItemsAnywhere.Patches;

public class IsAtBindablePlace : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(InventoryController),
            nameof(InventoryController.IsAtBindablePlace)
        );
    }

    [PatchPostfix]
    private static void Postfix(InventoryController __instance, ref bool __result, Item item)
    {
        if (item is CompoundItem compoundItem && compoundItem.MissingVitalParts.Any())
        {
            __result = false;
            return;
        }

        if (!IsValidItemForBinding(item) || !__instance.Examined(item))
        {
            return;
        }
        
        switch (item)
        {
            case Weapon weap:
                if (Configuration.FlareIds.Contains(weap.TemplateId))
                {
                    __result = __instance.Inventory.SlotsContainItem(Configuration.FlareSlots.Value, weap);
                    return;
                }
                
                __result = __instance.Inventory.SlotsContainItem(Configuration.AllAllowedWeaponSlots, item);
                return;
            case ThrowWeap:
                __result = __instance.Inventory.SlotsContainItem(Configuration.GrenadeThrowSlots.Value, item);
                return;
            case Ammo:
            case Magazine:
                __result = __instance.Inventory.SlotsContainItem(Configuration.ReloadSlots.Value, item);
                return;
            case Meds:
                __result = __instance.Inventory.SlotsContainItem(Configuration.MedsSlots.Value, item);
                return;
            case FoodDrink:
                __result = __instance.Inventory.SlotsContainItem(Configuration.FoodDrinkSlots.Value, item);
                return;
            default:
                if (item.GetItemComponent<KnifeComponent>() != null)
                {
                    __result = __instance.Inventory.SlotsContainItem(Configuration.AllAllowedMeleeSlots, item);
                    return;
                }
                
                __result = __instance.Inventory.SlotsContainItem(Configuration.AllOtherItems.Value, item);
                return;
        }
    }

    private static bool IsValidItemForBinding(Item item)
    {
        return item is Weapon
            || item is ThrowWeap
            || item is Meds
            || item is Food
            || item is Compass
            || item is PortableRangeFinder
            || item is RadioTransmitter
            || item.GetItemComponent<KnifeComponent>() != null
            || Configuration.FlareIds.Contains(item.TemplateId);
    }
}
