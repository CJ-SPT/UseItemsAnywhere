using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace UseItemsAnywhere.Patches;

public class UseItemsFromAnywherePatches
{
    private static readonly HashSet<string> Items = [
        "62178c4d4ecf221597654e3d", // Red Flare
        "624c0b3340357b5f566e8766", // Yellow Flare
        "6217726288ed9f0845317459", // green Flare
        "62178be9d0050232da3485d9"  // white Flare
    ];

    public class IsAtBindablePlace : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAtBindablePlace));
        }

        [PatchPostfix]
        private static void Postfix(InventoryController __instance, ref bool __result, Item item)
        {
            if (item is Weapon || item is ThrowWeapItemClass || item is MedsItemClass
                || item is FoodItemClass || item is CompassItemClass || item is PortableRangeFinderItemClass
                || item is RadioTransmitterItemClass || item.GetItemComponent<KnifeComponent>() != null
                || Items.Contains(item.TemplateId))
            {
                __result = __instance.Inventory.Equipment.Contains(item);
            }
        }
    }

    public class IsAtReachablePlace : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAtReachablePlace));
        }

        [PatchPostfix]
        private static void Postfix(InventoryController __instance, ref bool __result, Item item)
        {
            __result = __instance.Inventory.Equipment.Contains(item);
        }
    }
}