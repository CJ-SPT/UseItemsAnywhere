using System;

namespace UseItemsAnywhere.BackpackAccess;

internal static class BackpackHandBoneNames
{
    internal static bool TryGetDigit(string name, out int finger, out int joint)
    {
        finger = 0;
        joint = 0;
        var marker = name.LastIndexOf("Digit", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || marker + 7 != name.Length)
        {
            return false;
        }

        finger = name[marker + 5] - '0';
        joint = name[marker + 6] - '0';
        return finger >= 1 && finger <= 5 && joint >= 1 && joint <= 3;
    }

    // Verified against the game's skeleton.bundle: digit 1 is the thumb,
    // digit 2 is the index, and digit 5 is the little finger. The last digit
    // identifies the joint; 1 is the knuckle attached directly to the palm.
    internal static bool IsIndex(string name) =>
        name.EndsWith("Digit21", StringComparison.OrdinalIgnoreCase)
        || name.IndexOf("Index", StringComparison.OrdinalIgnoreCase) >= 0;

    internal static bool IsLittle(string name) =>
        name.EndsWith("Digit51", StringComparison.OrdinalIgnoreCase)
        || name.IndexOf("Pinky", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Little", StringComparison.OrdinalIgnoreCase) >= 0;
}
