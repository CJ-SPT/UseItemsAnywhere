using System.Reflection;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

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
        if (!QuickUseWheel.InputBlocked)
        {
            return true;
        }

        __result = InputNode.ETranslateResult.BlockAll;
        return false;
    }
}
