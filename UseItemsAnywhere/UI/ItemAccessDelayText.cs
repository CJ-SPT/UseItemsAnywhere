namespace UseItemsAnywhere.UI;

internal static class ItemAccessDelayText
{
    internal static string FormatWheelState(Configuration.ItemAccessDelayInfo delayInfo) =>
        $"{delayInfo.TotalDelay:0.0}s ACCESS";

    internal static string FormatWheelSelection(Configuration.ItemAccessDelayInfo delayInfo)
    {
        var source = RuntimeUiService.GetSlotName(delayInfo.SourceSlot).ToUpperInvariant();
        if (delayInfo.NestingDelay <= 0f)
        {
            return $"ACCESS {delayInfo.TotalDelay:0.0}s • {source}\nBASE DELAY {delayInfo.BaseDelay:0.0}s";
        }

        return $"ACCESS {delayInfo.TotalDelay:0.0}s • {source}\nBASE {delayInfo.BaseDelay:0.0}s + NESTED {delayInfo.NestingDelay:0.0}s ({delayInfo.NestingDepth})";
    }

    internal static string FormatTimerDetail(Configuration.ItemAccessDelayInfo delayInfo)
    {
        if (delayInfo.NestingDelay <= 0f)
        {
            return $"BASE ACCESS DELAY  {delayInfo.BaseDelay:0.0}s";
        }

        var layerLabel = delayInfo.NestingDepth == 1 ? "LAYER" : "LAYERS";
        return $"BASE {delayInfo.BaseDelay:0.0}s  +  {delayInfo.NestingDelay:0.0}s NESTED  •  {delayInfo.NestingDepth} {layerLabel}";
    }
}
