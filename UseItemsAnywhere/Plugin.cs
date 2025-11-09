using System;
using BepInEx;
using DrakiaXYZ.VersionChecker;
using EFT.InventoryLogic;
using HarmonyLib;
using static UseItemsAnywhere.Patches.UseItemsFromAnywherePatches;

namespace UseItemsAnywhere
{
    [BepInPlugin("com.cj.useFromAnywhere", "Use items anywhere", "1.3.1")]
    [BepInDependency("com.SPT.custom", "4.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const int TarkovVersion = 40087;
        
        private static readonly EquipmentSlot[] ExtendedFastAccessSlots =
        [
            EquipmentSlot.Pockets,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Backpack,
            EquipmentSlot.SecuredContainer,
            EquipmentSlot.ArmBand
        ];

        internal void Awake()
        {
            if (!VersionChecker.CheckEftVersion(Logger, Info, Config))
            {
                throw new Exception("Invalid EFT Version");
            }
            
            DontDestroyOnLoad(this);

            var fastAccessSlots = AccessTools.Field(typeof(Inventory), nameof(Inventory.FastAccessSlots));
            fastAccessSlots.SetValue(fastAccessSlots, ExtendedFastAccessSlots);

            new IsAtBindablePlace().Enable();
            new IsAtReachablePlace().Enable();
        }
    }
}