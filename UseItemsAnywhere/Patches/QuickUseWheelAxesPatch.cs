using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.QuickUseWheel;

namespace UseItemsAnywhere.Patches;

internal sealed class QuickUseWheelAxesPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(GamePlayerOwner),
            nameof(GamePlayerOwner.TranslateAxes));
    }

    [PatchPrefix]
    private static bool Prefix()
    {
        // The wheel reads Unity's raw mouse axes directly. Suppressing EFT's
        // axis translation keeps radial selection active without moving the camera.
        return !QuickUseWheelController.InputBlocked;
    }
}
