using System.Reflection;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.QuickUseWheel;
using UseItemsAnywhere.Integration;

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
    private static bool Prefix(GamePlayerOwner __instance, ref InputNode.ETranslateResult __result)
    {
        if (!CoopRuntime.IsLocalPlayer(__instance.Player) || !QuickUseWheelController.InputBlocked)
        {
            return true;
        }

        __result = InputNode.ETranslateResult.BlockAll;
        return false;
    }
}
