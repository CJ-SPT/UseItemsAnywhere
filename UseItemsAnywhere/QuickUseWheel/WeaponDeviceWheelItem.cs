using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;

namespace UseItemsAnywhere.QuickUseWheel;

internal readonly struct WeaponDeviceWheelItem(
    LightComponent? light,
    string displayName,
    string fullName,
    string state,
    string sourceName,
    bool isAvailable,
    bool isAggregate,
    bool canCycleMode,
    ItemIcon? icon)
{
    internal LightComponent? Light { get; } = light;
    internal string DisplayName { get; } = displayName;
    internal string FullName { get; } = fullName;
    internal string State { get; } = state;
    internal string SourceName { get; } = sourceName;
    internal bool IsAvailable { get; } = isAvailable;
    internal bool IsAggregate { get; } = isAggregate;
    internal bool CanCycleMode { get; } = canCycleMode;
    internal ItemIcon? Icon { get; } = icon;
}
