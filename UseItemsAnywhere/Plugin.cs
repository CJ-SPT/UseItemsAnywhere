using BepInEx;
using DrakiaXYZ.VersionChecker;
using EFT.InventoryLogic;
using HarmonyLib;
using System;
using static UseItemsAnywhere.Patches.UseItemsFromAnywhere;

namespace UseItemsFromAnywhere
{
    [BepInPlugin("com.dirtbikercj.useFromAnywhere", "Use items anywhere", "1.3.0")]
    [BepInDependency("com.SPT.custom", "4.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const int TarkovVersion = 40087;
        
        private static EquipmentSlot[] _extendedFastAccessSlots =
        {
            EquipmentSlot.Pockets,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Backpack,
            EquipmentSlot.SecuredContainer,
            EquipmentSlot.ArmBand
        };

        internal void Awake()
        {
            if (!VersionChecker.CheckEftVersion(Logger, Info, Config))
            {
                throw new Exception("Invalid EFT Version");
            }
            
            DontDestroyOnLoad(this);

            var fastAccessSlots = AccessTools.Field(typeof(Inventory), "FastAccessSlots");
            fastAccessSlots.SetValue(fastAccessSlots, _extendedFastAccessSlots);

            new IsAtBindablePlace().Enable();
            new IsAtReachablePlace().Enable();
        }
    }
}