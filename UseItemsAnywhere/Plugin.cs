using System;
using System.Collections.Generic;
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

[BepInPlugin("com.cj.useFromAnywhere", "Use Items Anywhere", "2.1.4")]
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

    private static readonly System.Reflection.FieldInfo FastAccessSlotsField =
        AccessTools.Field(typeof(Inventory), nameof(Inventory.FastAccessSlots))
        ?? throw new MissingFieldException(typeof(Inventory).FullName, nameof(Inventory.FastAccessSlots));

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

        ExtendFastAccessSlots();

        var patchManager = new PatchManager(this, true);
        patchManager.EnablePatches();
    }

    internal void Start()
    {
        // Some inventory mods replace this shared array from their Awake method.
        // Merge once all plugin Awake methods have run so neither mod loses slots.
        ExtendFastAccessSlots();
    }

    private static void ExtendFastAccessSlots()
    {
        var mergedSlots = new List<EquipmentSlot>();
        if (FastAccessSlotsField.GetValue(null) is EquipmentSlot[] currentSlots)
        {
            foreach (var slot in currentSlots)
            {
                if (!mergedSlots.Contains(slot))
                {
                    mergedSlots.Add(slot);
                }
            }
        }

        foreach (var slot in ExtendedFastAccessSlots)
        {
            if (!mergedSlots.Contains(slot))
            {
                mergedSlots.Add(slot);
            }
        }

        FastAccessSlotsField.SetValue(null, mergedSlots.ToArray());
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
