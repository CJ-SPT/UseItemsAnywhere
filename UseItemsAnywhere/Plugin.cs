using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using DrakiaXYZ.VersionChecker;
using SPT.Reflection.Patching;
using UseItemsAnywhere.ItemUseDelayTimer;
using UseItemsAnywhere.Patches;
using UseItemsAnywhere.QuickUseWheel;
using UseItemsAnywhere.UI;
using UseItemsAnywhere.Integration;

namespace UseItemsAnywhere;

[BepInPlugin(PluginGuid, "Use Items Anywhere", PluginVersion)]
[BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.fika.headless", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.SPT.custom", "4.1.0")]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.cj.useFromAnywhere";
    public const string PluginVersion = "2.1.6";

    private readonly QuickUseWheelController _quickUseWheel = new();
    private readonly ItemUseDelayTimerController _itemUseDelayTimer = new();
    private RuntimeUiService? _runtimeUi;

    internal static ItemUseDelayTimerController? DelayTimer { get; private set; }
    internal static ManualLogSource? LogSource { get; private set; }

    public const int TarkovVersion = 40743;

    internal void Awake()
    {
        if (!VersionChecker.CheckEftVersion(Logger, Info, Config))
        {
            throw new Exception("Invalid EFT Version");
        }

        DontDestroyOnLoad(this);
        LogSource = Logger;
        Configuration.Init(Config);
        CoopRuntime.Cleanup += ResetSession;
        if (!CoopRuntime.IsHeadless)
        {
            var pluginDirectory = Path.GetDirectoryName(Info.Location)!;
            _runtimeUi = new RuntimeUiService(pluginDirectory, Logger, transform);
            _quickUseWheel.Initialize(Logger, _runtimeUi);
            _itemUseDelayTimer.Initialize(_runtimeUi);
            DelayTimer = _itemUseDelayTimer;
        }

        InventoryQueryPatches.Enable();

        var patchManager = new PatchManager(this, true);
        patchManager.EnablePatches();
    }

    internal void Update()
    {
        CoopRuntime.Tick();
        if (_runtimeUi == null) return;
        if (Configuration.ClearItemAccessDelay.Value.IsDown())
        {
            ItemAccessDelayPatch.ClearPendingItemAccess();
        }

        _quickUseWheel.Update();
        _itemUseDelayTimer.Update();
    }

    private void ResetSession()
    {
        ItemAccessDelayPatch.ResetSession();
        if (_runtimeUi == null) return;
        _quickUseWheel.ResetSession();
        _itemUseDelayTimer.HideImmediately();
    }

    internal void OnDestroy()
    {
        CoopRuntime.EndSession();
        CoopRuntime.Cleanup -= ResetSession;
        DelayTimer = null;
        if (_runtimeUi == null) return;
        _itemUseDelayTimer.OnDestroy();
        _quickUseWheel.OnDestroy();
        _runtimeUi?.Destroy();
        _runtimeUi = null;
    }
}
