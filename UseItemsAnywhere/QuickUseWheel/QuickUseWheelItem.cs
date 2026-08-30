using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;

namespace UseItemsAnywhere.QuickUseWheel;

internal readonly struct QuickUseWheelItem(
    Item item,
    string displayName,
    string fullName,
    string state,
    bool isUsable,
    bool isQueued,
    bool isFavorite,
    EquipmentSlot sourceSlot,
    string sourceName,
    ItemIcon? icon)
{
    internal Item Item { get; } = item;
    internal string DisplayName { get; } = displayName;
    internal string FullName { get; } = fullName;
    internal string State { get; } = state;
    internal bool IsUsable { get; } = isUsable;
    internal bool IsQueued { get; } = isQueued;
    internal bool IsFavorite { get; } = isFavorite;
    internal EquipmentSlot SourceSlot { get; } = sourceSlot;
    internal ItemIcon? Icon { get; } = icon;
    internal string SourceName { get; } = sourceName;

    internal QuickUseWheelItem WithFavorite(bool value) => new(
        Item,
        DisplayName,
        FullName,
        State,
        IsUsable,
        IsQueued,
        value,
        SourceSlot,
        SourceName,
        Icon);
}
