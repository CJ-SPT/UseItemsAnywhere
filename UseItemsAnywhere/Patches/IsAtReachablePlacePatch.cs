using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.Extensions;

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
                __result = __instance.Inventory.SlotsContainItem(Configuration.AllOtherItems.Value, item);
                return;
        }
    }
}