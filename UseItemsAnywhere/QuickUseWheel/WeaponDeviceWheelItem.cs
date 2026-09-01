using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;

namespace UseItemsAnywhere.QuickUseWheel;

internal enum WeaponWheelItemKind
{
    FireMode,
    AllDevices,
    Device,
}

internal readonly struct WeaponDeviceWheelItem(
    WeaponWheelItemKind kind,
    LightComponent? light,
    Weapon.EFireMode? fireMode,
    string displayName,
    string fullName,
    string state,
    string sourceName,
    bool isAvailable,
    bool isSelectedFireMode,
    bool canCycleMode,
    ItemIcon? icon)
{
    internal WeaponWheelItemKind Kind { get; } = kind;
    internal LightComponent? Light { get; } = light;
    internal Weapon.EFireMode? FireMode { get; } = fireMode;
    internal string DisplayName { get; } = displayName;
    internal string FullName { get; } = fullName;
    internal string State { get; } = state;
    internal string SourceName { get; } = sourceName;
    internal bool IsAvailable { get; } = isAvailable;
    internal bool IsFireMode => Kind == WeaponWheelItemKind.FireMode;
    internal bool IsSelectedFireMode { get; } = isSelectedFireMode;
    internal bool IsAggregate => Kind == WeaponWheelItemKind.AllDevices;
    internal bool CanCycleMode { get; } = canCycleMode;
    internal ItemIcon? Icon { get; } = icon;
}
