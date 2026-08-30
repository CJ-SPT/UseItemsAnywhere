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
    [BepInPlugin("com.cj.useFromAnywhere", "R.A.T. - Radial Access Toolkit", "2.1.1")]
    [BepInDependency("com.SPT.custom", "4.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private readonly QuickUseWheel _quickUseWheel = new();
        private readonly ItemUseDelayTimer _itemUseDelayTimer = new();

        internal static ItemUseDelayTimer? DelayTimer { get; private set; }

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
            var pluginDirectory = Path.GetDirectoryName(Info.Location)!;
            _quickUseWheel.Initialize(pluginDirectory, Logger, transform);
            _itemUseDelayTimer.Initialize(pluginDirectory, Logger, transform);
            DelayTimer = _itemUseDelayTimer;
            
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
            _itemUseDelayTimer.Update();
        }

        internal void OnDestroy()
        {
            DelayTimer = null;
            _itemUseDelayTimer.OnDestroy();
            _quickUseWheel.OnDestroy();
        }
    }
}
