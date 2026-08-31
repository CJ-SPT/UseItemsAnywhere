using System.Reflection;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.QuickUseWheel;

namespace UseItemsAnywhere.Patches;

internal sealed class QuickUseWheelInputPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(GamePlayerOwner),
            nameof(GamePlayerOwner.TranslateCommand));
    }

    [PatchPrefix]
    private static bool Prefix(ref InputNode.ETranslateResult __result)
    {
        if (!QuickUseWheelController.InputBlocked)
        {
            return true;
        }

        __result = InputNode.ETranslateResult.BlockAll;
        return false;
    }
}
