using System;
using System.Collections.Generic;

namespace UseItemsAnywhere.QuickUseWheel;

// Independent of Unity and inventory enumeration: reservations never reorder the item pages.
internal sealed class QuickUseCategorySlots<T>
{
    internal const int SlotCount = 8;
    private readonly string?[] _selectedIds = new string?[SlotCount];
    private readonly QuickUseCategory[] _categories = new QuickUseCategory[SlotCount];
    private object? _raid;
    private object? _player;

    internal void SetContext(object? raid, object? player)
    {
        if (ReferenceEquals(_raid, raid) && ReferenceEquals(_player, player))
        {
            return;
        }
        Array.Clear(_selectedIds, 0, SlotCount);
        Array.Clear(_categories, 0, SlotCount);
        _raid = raid;
        _player = player;
    }

    internal Candidate? Resolve(int slot, QuickUseCategory category, IReadOnlyList<Candidate> candidates)
    {
        if (_categories[slot] != category)
        {
            _selectedIds[slot] = null;
            _categories[slot] = category;
        }

        Candidate? best = null;
        foreach (var candidate in candidates)
        {
            if (category == QuickUseCategory.Unassigned || !candidate.Matches(category))
            {
                continue;
            }
            if (string.Equals(candidate.Id, _selectedIds[slot], StringComparison.Ordinal))
            {
                return candidate;
            }
            if (!best.HasValue || Compare(candidate, best.Value) < 0)
            {
                best = candidate;
            }
        }
        _selectedIds[slot] = best?.Id;
        return best;
    }

    private static int Compare(Candidate left, Candidate right)
    {
        var order = right.IsFavorite.CompareTo(left.IsFavorite);
        if (order == 0) order = left.AccessDelay.CompareTo(right.AccessDelay);
        if (order == 0) order = string.CompareOrdinal(left.TemplateId, right.TemplateId);
        return order != 0 ? order : string.CompareOrdinal(left.Id, right.Id);
    }

    internal readonly struct Candidate(T value, string id, string templateId, bool isFavorite,
        float accessDelay, int categoryMask)
    {
        internal T Value { get; } = value;
        internal string Id { get; } = id;
        internal string TemplateId { get; } = templateId;
        internal bool IsFavorite { get; } = isFavorite;
        internal float AccessDelay { get; } = accessDelay;
        internal bool Matches(QuickUseCategory category) => (categoryMask & (1 << (int)category)) != 0;
    }
}
