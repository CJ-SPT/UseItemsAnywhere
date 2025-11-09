using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace UseItemsAnywhere.Patches;

public class IsAtBindablePlace : ModulePatch
{
    private static readonly HashSet<MongoID> Items = [
        new ("62178c4d4ecf221597654e3d"),         // Red Flare
        new ("624c0b3340357b5f566e8766"),       // Yellow Flare
        new ("6217726288ed9f0845317459"),       // green Flare
        new ("62178be9d0050232da3485d9")         // white Flare
    ];
    
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAtBindablePlace));
    }

    [PatchPostfix]
    private static void Postfix(InventoryController __instance, ref bool __result, Item item)
    {
        if (item is CompoundItem compoundItem && compoundItem.MissingVitalParts.Any())
        {
            __result = false;
            return;
        }
            
        if (IsValidItemForBinding(item) && __instance.Examined(item))
        {
            __result = __instance.Inventory.Equipment.Contains(item);
        }
    }

    private static bool IsValidItemForBinding(Item item)
    {
        return item is Weapon || item is ThrowWeapItemClass || item is MedsItemClass
               || item is FoodItemClass || item is CompassItemClass || item is PortableRangeFinderItemClass
               || item is RadioTransmitterItemClass || item.GetItemComponent<KnifeComponent>() != null
               || Items.Contains(item.TemplateId);
    }
}