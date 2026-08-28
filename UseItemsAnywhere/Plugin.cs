using System;
using BepInEx;
using DrakiaXYZ.VersionChecker;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace UseItemsAnywhere
{
    [BepInPlugin("com.cj.useFromAnywhere", "Use Items Anywhere", "2.0.0")]
    [BepInDependency("com.SPT.custom", "4.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const int TarkovVersion = 40743;

        private static readonly EquipmentSlot[] ExtendedFastAccessSlots =
        [
            EquipmentSlot.Pockets,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Backpack,
            EquipmentSlot.SecuredContainer,
            EquipmentSlot.ArmBand,
        ];

        internal void Awake()
        {
            if (!VersionChecker.CheckEftVersion(Logger, Info, Config))
            {
                throw new Exception("Invalid EFT Version");
            }

            DontDestroyOnLoad(this);
            Configuration.Init(Config);
            
            var fastAccessSlots = AccessTools.Field(
                typeof(Inventory),
                nameof(Inventory.FastAccessSlots)
            );
            fastAccessSlots.SetValue(fastAccessSlots, ExtendedFastAccessSlots);

            var patchManager = new PatchManager(this, true);
            patchManager.EnablePatches();
        }
    }
}
