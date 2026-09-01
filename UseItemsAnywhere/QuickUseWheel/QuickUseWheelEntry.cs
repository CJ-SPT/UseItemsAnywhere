using EFT.UI.DragAndDrop;

namespace UseItemsAnywhere.QuickUseWheel;

internal readonly struct QuickUseWheelEntry(
    string displayName,
    string fullName,
    string state,
    string sourceName,
    bool isUsable,
    bool isQueued,
    bool isFavorite,
    bool showState,
    bool showSource,
    ItemIcon? icon)
{
    internal string DisplayName { get; } = displayName;
    internal string FullName { get; } = fullName;
    internal string State { get; } = state;
    internal string SourceName { get; } = sourceName;
    internal bool IsUsable { get; } = isUsable;
    internal bool IsQueued { get; } = isQueued;
    internal bool IsFavorite { get; } = isFavorite;
    internal bool ShowState { get; } = showState;
    internal bool ShowSource { get; } = showSource;
    internal ItemIcon? Icon { get; } = icon;
}

internal readonly struct QuickUseWheelViewState(
    string header,
    string emptyTitle,
    string emptyHint,
    string controls,
    string status)
{
    internal string Header { get; } = header;
    internal string EmptyTitle { get; } = emptyTitle;
    internal string EmptyHint { get; } = emptyHint;
    internal string Controls { get; } = controls;
    internal string Status { get; } = status;
}
