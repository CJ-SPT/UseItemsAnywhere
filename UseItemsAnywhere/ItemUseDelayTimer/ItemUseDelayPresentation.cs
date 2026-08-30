using System;

namespace UseItemsAnywhere.ItemUseDelayTimer;

internal sealed class ItemUseDelayPresentation : IDisposable
{
    private ItemUseDelayTimerController? _owner;
    private readonly int _presentationId;

    internal ItemUseDelayPresentation(ItemUseDelayTimerController owner, int presentationId)
    {
        _owner = owner;
        _presentationId = presentationId;
    }

    internal void SetRemaining(float remaining) => _owner?.SetRemaining(_presentationId, remaining);

    internal void Finish(bool completed)
    {
        var owner = _owner;
        _owner = null;
        owner?.End(_presentationId, completed);
    }

    public void Dispose() => Finish(false);
}
