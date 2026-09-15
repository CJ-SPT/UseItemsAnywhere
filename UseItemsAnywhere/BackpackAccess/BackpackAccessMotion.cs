using System;

namespace UseItemsAnywhere.BackpackAccess;

/// <summary>One clock for the bag, both hands, and search audio.</summary>
internal readonly struct BackpackAccessMotion
{
    internal const float LowerItemDuration = 0.30f;
    internal const float BringInDuration = 0.55f;
    internal const float ReachDuration = 0.32f;
    internal const float SeamDuration = 0.58f;
    internal const float InsertDuration = 0.38f;
    internal const float PutAwayDuration = 1.10f;
    internal const float GraspDuration = 0.30f;
    internal const float SearchStart = LowerItemDuration + BringInDuration + ReachDuration + SeamDuration + InsertDuration;

    internal readonly float Raise;
    internal readonly float LeftWeight;
    internal readonly float RightWeight;
    internal readonly float Seam;
    internal readonly float Insert;
    internal readonly float Exit;
    internal readonly float Lower;
    internal readonly float SearchPress;
    internal readonly float SearchLift;
    internal readonly float SearchSide;
    internal readonly float Grasp;
    internal readonly float ClearHand;
    internal readonly float LowerRightHand;
    internal readonly float PresentItem;
    internal readonly float ElbowTuck;
    private readonly float _fingerTime;
    private readonly float _fingerActivity;

    internal static float TimeScale(float totalDelay) => Math.Min(1f, totalDelay / 4.0f);

    internal BackpackAccessMotion(float elapsed, float remaining, float timingScale)
    {
        var time = elapsed / timingScale;
        Raise = Smooth((time - LowerItemDuration) / BringInDuration);
        LeftWeight = Raise;
        RightWeight = Smooth((time - LowerItemDuration - BringInDuration) / ReachDuration);
        Seam = Smooth((time - LowerItemDuration - BringInDuration - ReachDuration) / SeamDuration);
        Insert = Smooth((time - SearchStart + InsertDuration) / InsertDuration);
        Exit = Clamp01(1f - remaining / (PutAwayDuration * timingScale));
        Lower = Smooth((Exit - BackpackWithdrawalPath.ClearTime) / (1f - BackpackWithdrawalPath.ClearTime));
        ClearHand = Smooth(Exit / BackpackWithdrawalPath.ClearTime);
        LowerRightHand = Smooth((Exit - BackpackWithdrawalPath.ClearTime)
            / (BackpackWithdrawalPath.EndTime - BackpackWithdrawalPath.ClearTime));
        // Let the grip start clearing before rolling inward; finish the turn
        // during the cross-body drop instead of presenting it above the bag.
        PresentItem = Smooth((Exit - 0.28f) / 0.55f);
        ElbowTuck = Smooth((Exit - 0.35f) / 0.50f);
        RightWeight *= 1f - Smooth((Exit - 0.92f) / 0.08f);
        LeftWeight *= 1f - Smooth((Exit - 0.75f) / 0.25f);
        Grasp = Smooth((PutAwayDuration + GraspDuration - remaining / timingScale) / GraspDuration);

        // Each search has a press, a lateral feel, a small lift, and a pause.
        // Alternating sides gives readable movement without constant shaking.
        var searchTime = Math.Max(0f, time - SearchStart);
        var cycle = searchTime / 1.35f;
        var phase = cycle - (float)Math.Floor(cycle);
        var side = ((int)Math.Floor(cycle) & 1) == 0 ? 1f : -1f;
        var fade = Smooth((remaining / timingScale - PutAwayDuration - GraspDuration) / 0.18f);
        SearchPress = Smooth(phase / 0.28f) * (1f - Smooth((phase - 0.50f) / 0.28f)) * fade;
        SearchLift = Smooth((phase - 0.55f) / 0.20f) * (1f - Smooth((phase - 0.80f) / 0.20f)) * fade;
        SearchSide = Smooth((phase - 0.18f) / 0.25f) * (1f - Smooth((phase - 0.60f) / 0.40f)) * side * fade;
        _fingerTime = searchTime;
        _fingerActivity = Smooth(searchTime / 0.20f) * fade;
    }

    internal bool ShowRetrievedItem => Grasp >= 0.70f && Exit < 0.98f;

    internal float RightFingerCurl(int finger)
    {
        // Fingers feel in sequence rather than opening and closing as one fist.
        var feel = 0.5f + 0.5f * (float)Math.Sin(_fingerTime * 6.2f - finger * 0.85f);
        var searchCurl = 0.12f + feel * 0.50f * _fingerActivity;
        var gripCurl = finger <= 1 ? 0.65f : 0.82f;
        return searchCurl + (gripCurl - searchCurl) * Grasp;
    }

    internal static float Smooth(float value)
    {
        value = Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}
