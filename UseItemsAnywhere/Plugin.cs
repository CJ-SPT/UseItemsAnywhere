using System;
using System.IO;
using BepInEx;
using DrakiaXYZ.VersionChecker;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.ItemUseDelayTimer;
using UseItemsAnywhere.Patches;
using UseItemsAnywhere.QuickUseWheel;
using UseItemsAnywhere.UI;

namespace UseItemsAnywhere;

[BepInPlugin("com.cj.useFromAnywhere", "Use Items Anywhere", "2.1.1")]
[BepInDependency("com.SPT.custom", "4.1.0")]
public class Plugin : BaseUnityPlugin
{
    private readonly QuickUseWheelController _quickUseWheel = new();
    private readonly ItemUseDelayTimerController _itemUseDelayTimer = new();
    private RuntimeUiService? _runtimeUi;

    internal static ItemUseDelayTimerController? DelayTimer { get; private set; }

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
        _runtimeUi = new RuntimeUiService(pluginDirectory, Logger, transform);
        _quickUseWheel.Initialize(Logger, _runtimeUi);
        _itemUseDelayTimer.Initialize(_runtimeUi);
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
        _runtimeUi?.Destroy();
        _runtimeUi = null;
    }
}
