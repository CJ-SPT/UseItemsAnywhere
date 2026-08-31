using UnityEngine;

namespace UseItemsAnywhere.QuickUseWheel;

internal static class QuickUseWheelGeometry
{
    private const float VisualGapDegrees = 2f;

    internal static float GetSliceDegrees(int itemCount) => itemCount > 0 ? 360f / itemCount : 360f;

    internal static float GetVisibleSegmentDegrees(int itemCount)
    {
        var slice = GetSliceDegrees(itemCount);
        return itemCount == 1 ? slice : Mathf.Max(0f, slice - VisualGapDegrees);
    }
}
