using System;
using System.IO;
using BepInEx;
using DrakiaXYZ.VersionChecker;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.Patches;

namespace UseItemsAnywhere
{
    [BepInPlugin("com.cj.useFromAnywhere", "Use Items Anywhere", "2.1.0")]
    [BepInDependency("com.SPT.custom", "4.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private readonly QuickUseWheel _quickUseWheel = new();

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
            _quickUseWheel.Initialize(Path.GetDirectoryName(Info.Location)!, Logger, transform);
            
            var fastAccessSlots = AccessTools.Field(
                typeof(Inventory),
                nameof(Inventory.FastAccessSlots)
            );
            fastAccessSlots.SetValue(fastAccessSlots, ExtendedFastAccessSlots);

            var patchManager = new PatchManager(this, true);
            patchManager.EnablePatches();
        }

        internal void Update()
        {
            if (Configuration.ClearItemAccessDelay.Value.IsDown())
            {
                ItemAccessDelayPatch.ClearPendingItemAccess();
            }

            _quickUseWheel.Update();
        }

        internal void OnDestroy() => _quickUseWheel.OnDestroy();
    }
}
