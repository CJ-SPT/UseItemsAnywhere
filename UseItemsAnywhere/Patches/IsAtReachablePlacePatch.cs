using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

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
            case Weapon:
                __result = __instance.Inventory.GetItemsInSlots([..Configuration.DefaultWeaponSlots, ..Configuration.WeaponSlots.Value]).Contains(item);
                return;
            case ThrowWeap:
                __result = __instance.Inventory.GetItemsInSlots(Configuration.GrenadeThrowSlots.Value).Contains(item);
                return;
            case Ammo:
            case Magazine:
                __result = __instance.Inventory.GetItemsInSlots(Configuration.ReloadSlots.Value).Contains(item);
                return;
            case Meds:
                __result = __instance.Inventory.GetItemsInSlots(Configuration.MedsSlots.Value).Contains(item);
                return;
            default:
                __result = __instance.Inventory.GetItemsInSlots(Configuration.AllOtherItems.Value).Contains(item);
                return;
        }
    }
}