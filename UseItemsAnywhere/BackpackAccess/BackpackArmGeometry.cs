using System;

namespace UseItemsAnywhere.BackpackAccess;

internal static class BackpackArmGeometry
{
    internal static float MaximumReach(float upperLength, float lowerLength, float retraction)
    {
        var extended = (upperLength + lowerLength) * 0.94f;
        // A 75-degree bend from straight, computed from the actual limb
        // lengths so glove/character rigs do not need fixed joint rotations.
        var folded = (float)Math.Sqrt(upperLength * upperLength + lowerLength * lowerLength
            + 2f * upperLength * lowerLength * Math.Cos(75f * Math.PI / 180f));
        folded = Math.Min(extended, folded);
        return extended + (folded - extended) * Math.Max(0f, Math.Min(1f, retraction));
    }
}
