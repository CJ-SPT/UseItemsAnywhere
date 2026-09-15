using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UseItemsAnywhere.BackpackAccess;

namespace UseItemsAnywhere.Patches;

/// <summary>
///     Applies the staged backpack hand targets after Tarkov has completed its
///     normal first-person IK pass for the frame.
/// </summary>
internal sealed class BackpackRummageHandsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(Player), nameof(Player.VisualPass));

    [PatchPostfix]
    private static void PatchPostfix(Player __instance, bool ____bodyupdated)
    {
        // VisualPass also runs on frames that reuse the last body pose. Do not
        // compound our wrist/elbow offsets when native IK has not refreshed it.
        if (____bodyupdated && !__instance.UsedSimplifiedSkeleton
            && (__instance.EnabledAnimators & Player.EAnimatorMask.IK) != 0)
        {
            BackpackRummageHands.ApplyActive(__instance);
        }
    }
}
