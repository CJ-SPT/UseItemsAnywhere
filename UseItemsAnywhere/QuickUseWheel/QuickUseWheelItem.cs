using System.Collections.Generic;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;

namespace UseItemsAnywhere.QuickUseWheel;

internal readonly struct QuickUseWheelItem(
    Item item,
    Item[] groupedItems,
    int quantity,
    string displayName,
    string fullName,
    string state,
    Configuration.ItemAccessDelayInfo? delayInfo,
    bool isUsable,
    bool isQueued,
    bool isNextQueued,
    bool isFavorite,
    EquipmentSlot sourceSlot,
    string sourceName,
    ItemIcon? icon)
{
    internal Item Item { get; } = item;
    internal IReadOnlyList<Item> GroupedItems { get; } = groupedItems;
    internal int Quantity { get; } = quantity;
    internal bool IsGrouped => GroupedItems.Count > 1;
    internal string DisplayName { get; } = displayName;
    internal string FullName { get; } = fullName;
    internal string State { get; } = state;
    internal Configuration.ItemAccessDelayInfo? DelayInfo { get; } = delayInfo;
    internal bool IsUsable { get; } = isUsable;
    internal bool IsQueued { get; } = isQueued;
    internal bool IsNextQueued { get; } = isNextQueued;
    internal bool IsFavorite { get; } = isFavorite;
    internal EquipmentSlot SourceSlot { get; } = sourceSlot;
    internal ItemIcon? Icon { get; } = icon;
    internal string SourceName { get; } = sourceName;

    internal QuickUseWheelItem WithFavorite(bool value) => new(
        Item,
        [..GroupedItems],
        Quantity,
        DisplayName,
        FullName,
        State,
        DelayInfo,
        IsUsable,
        IsQueued,
        IsNextQueued,
        value,
        SourceSlot,
        SourceName,
        Icon);
}
