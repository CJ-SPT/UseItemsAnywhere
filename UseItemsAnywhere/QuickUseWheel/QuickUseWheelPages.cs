using System;

namespace UseItemsAnywhere.QuickUseWheel;

internal static class QuickUseWheelPages
{
    internal const int CategorySlotCount = 8;

    internal static int Count(int entryCount, int itemsPerPage, bool hasCategoryPage)
    {
        var prefix = hasCategoryPage ? CategorySlotCount : 0;
        var ordinaryCount = Math.Max(0, entryCount - prefix);
        return Math.Max(1, (hasCategoryPage ? 1 : 0) + (ordinaryCount + itemsPerPage - 1) / itemsPerPage);
    }

    internal static (int Start, int Count) Range(int page, int entryCount, int itemsPerPage, bool hasCategoryPage)
    {
        page = Math.Max(0, Math.Min(page, Count(entryCount, itemsPerPage, hasCategoryPage) - 1));
        if (hasCategoryPage && page == 0)
        {
            return (0, CategorySlotCount);
        }
        var start = hasCategoryPage ? CategorySlotCount + (page - 1) * itemsPerPage : page * itemsPerPage;
        return (start, Math.Min(itemsPerPage, Math.Max(0, entryCount - start)));
    }
}
