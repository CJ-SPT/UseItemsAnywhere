using System;

namespace UseItemsAnywhere.BackpackAccess;

/// <summary>Two Hermite segments sharing a nonzero tangent at the bag clearance point.</summary>
internal readonly struct BackpackWithdrawalPath
{
    // Preserve the roughly 0.60s lift, then use only 0.45s for the drop.
    internal const float ClearTime = 0.55f;
    internal const float EndTime = 0.96f;
    internal readonly float StartWeight;
    internal readonly float ClearWeight;
    internal readonly float RestWeight;
    internal readonly float TangentWeight;

    internal BackpackWithdrawalPath(float exit)
    {
        if (exit <= ClearTime)
        {
            var t = Math.Max(0f, exit / ClearTime);
            ClearWeight = BackpackAccessMotion.Smooth(t);
            StartWeight = 1f - ClearWeight;
            RestWeight = 0f;
            TangentWeight = (t * t * t - t * t) * ClearTime;
        }
        else
        {
            var span = EndTime - ClearTime;
            var t = Math.Min(1f, (exit - ClearTime) / span);
            RestWeight = BackpackAccessMotion.Smooth(t);
            ClearWeight = 1f - RestWeight;
            StartWeight = 0f;
            TangentWeight = (t * t * t - 2f * t * t + t) * span;
        }
    }
}
