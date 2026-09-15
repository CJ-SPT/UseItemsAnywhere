using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
using UseItemsAnywhere.Patches;
using UseItemsAnywhere.UI;

namespace UseItemsAnywhere.QuickUseWheel;

internal sealed class QuickUseWheelInventory
{
    private static readonly EquipmentSlot[] SlotPriority =
    [
        EquipmentSlot.FirstPrimaryWeapon,
        EquipmentSlot.SecondPrimaryWeapon,
        EquipmentSlot.Holster,
        EquipmentSlot.Scabbard,
        EquipmentSlot.Pockets,
        EquipmentSlot.TacticalVest,
        EquipmentSlot.ArmBand,
        EquipmentSlot.Backpack,
        EquipmentSlot.SecuredContainer,
    ];

    private static readonly EquipmentSlot[][] SourceSlotQueries =
    [
        [EquipmentSlot.FirstPrimaryWeapon],
        [EquipmentSlot.SecondPrimaryWeapon],
        [EquipmentSlot.Holster],
        [EquipmentSlot.Scabbard],
        [EquipmentSlot.Pockets],
        [EquipmentSlot.TacticalVest],
        [EquipmentSlot.ArmBand],
        [EquipmentSlot.Backpack],
        [EquipmentSlot.SecuredContainer],
    ];

    private readonly List<QuickUseWheelItem> _items = [];
    private readonly List<QuickUseCategorySlots<QuickUseWheelItem>.Candidate> _categoryCandidates = [];
    private readonly List<Item> _candidateItems = [];
    private readonly HashSet<Item> _seenItems = [];
    private readonly Dictionary<Item, int> _categorySourceMasks = [];
    private readonly Dictionary<Item, EquipmentSlot> _sourceSlots = [];
    private readonly Dictionary<string, List<Item>> _groupedCandidates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _favoriteTemplateIds = new(StringComparer.Ordinal);
    private RuntimeUiService _ui = null!;

    internal IReadOnlyList<QuickUseWheelItem> Items => _items;
    internal IReadOnlyList<QuickUseCategorySlots<QuickUseWheelItem>.Candidate> CategoryCandidates => _categoryCandidates;

    internal bool HasQueuedItems => _items.Exists(static item => item.IsQueued);

    internal bool HasUsableItems => _items.Exists(static item => item.IsUsable);

    internal void Initialize(RuntimeUiService ui)
    {
        _ui = ui;
    }

    internal void LoadFavorites()
    {
        _favoriteTemplateIds.Clear();
        foreach (var templateId in Configuration.QuickUseFavoriteTemplateIds.Value.Split(','))
        {
            var normalized = templateId.Trim();
            if (!string.IsNullOrEmpty(normalized))
            {
                _favoriteTemplateIds.Add(normalized);
            }
        }
    }

    internal bool Populate(Player player)
    {
        _items.Clear();
        _categoryCandidates.Clear();
        ClearWorkingSets();
        if (player is null || !player)
        {
            return false;
        }

        var controller = player.InventoryController;
        if (controller is null)
        {
            return false;
        }

        var inventory = controller.Inventory;
        if (inventory is null)
        {
            return false;
        }

        _ui.SetItemCachePlayer(player);
        try
        {
            for (var slotIndex = 0; slotIndex < SlotPriority.Length; slotIndex++)
            {
                foreach (var item in inventory.GetItemsInSlots(SourceSlotQueries[slotIndex]))
                {
                    if (item is not null && !_sourceSlots.ContainsKey(item))
                    {
                        _sourceSlots.Add(item, SlotPriority[slotIndex]);
                    }
                }
            }

            // Separate iterators so each category respects its configured source slots.
            if (Configuration.QuickUseShowPrimAndSecWeapons.Value)
            {
                foreach (var item in inventory.GetItemsInSlots(Configuration.AllAllowedWeaponSlots))
                {
                    if (item is Weapon)
                    {
                        AddCandidate(item, QuickUseCategory.Guns);
                    }
                }
            }

            if (Configuration.QuickUseShowMelee.Value)
            {
                foreach (var item in inventory.GetItemsInSlots(Configuration.AllAllowedMeleeSlots))
                {
                    if (item is not null
                        && item.GetItemComponent<KnifeComponent>() != null)
                    {
                        AddCandidate(item, QuickUseCategory.Melee);
                    }
                }
            }

            if (Configuration.QuickUseShowGrenades.Value)
            {
                foreach (var item in inventory.GetItemsInSlots(Configuration.GrenadeThrowSlots.Value))
                {
                    if (item is ThrowWeap)
                    {
                        AddCandidate(item, QuickUseCategory.Grenades);
                    }
                }
            }

            if (Configuration.QuickUseShowMeds.Value)
            {
                foreach (var item in inventory.GetItemsInSlots(Configuration.MedsSlots.Value))
                {
                    if (item is Meds)
                    {
                        AddCandidate(item, QuickUseCategory.AllMedical);
                    }
                }
            }

            if (Configuration.QuickUseShowFoodDrink.Value)
            {
                foreach (var item in inventory.GetItemsInSlots(Configuration.FoodDrinkSlots.Value))
                {
                    if (item is FoodDrink)
                    {
                        AddCandidate(item, QuickUseCategory.FoodDrink);
                    }
                }
            }

            if (Configuration.QuickUseShowFlares.Value)
            {
                foreach (var item in inventory.GetItemsInSlots(Configuration.FlareSlots.Value))
                {
                    if (item is not null
                        && Configuration.FlareIds.Contains(item.TemplateId))
                    {
                        AddCandidate(item, QuickUseCategory.Flares);
                    }
                }
            }

            foreach (var item in _candidateItems)
            {
                if (!controller.Examined(item))
                {
                    continue;
                }

                if (Configuration.QuickUseGroupIdenticalItems.Value
                    && IsGroupable(item)
                    && !ItemAccessDelayPatch.IsQueuedForAccess(player, item))
                {
                    var templateId = item.TemplateId.ToString();
                    if (!_groupedCandidates.TryGetValue(templateId, out var group))
                    {
                        group = [];
                        _groupedCandidates.Add(templateId, group);
                    }
                    group.Add(item);
                    continue;
                }

                AddWheelItem(player, [item]);
            }

            foreach (var group in _groupedCandidates.Values)
            {
                if (group.Count > 0)
                {
                    AddWheelItem(player, group);
                }
            }

            _items.Sort(CompareWheelItems);
            if (Configuration.HasQuickUseCategorySlots)
            {
                foreach (var item in _candidateItems)
                {
                    if (!controller.Examined(item)) continue;
                    var mask = 0;
                    foreach (var position in Configuration.QuickUseCategoryPositions)
                    {
                        if (HasCategorySource(item, position.Value)
                            && QuickUseCategoryClassifier.Matches(item, position.Value))
                        {
                            mask |= 1 << (int)position.Value;
                        }
                    }
                    if (mask == 0) continue;
                    var singleItem = CreateWheelItem(player, [item]);
                    if (!singleItem.IsUsable) continue;
                    _categoryCandidates.Add(new QuickUseCategorySlots<QuickUseWheelItem>.Candidate(
                        singleItem, item.Id, item.TemplateId.ToString(), singleItem.IsFavorite,
                        singleItem.DelayInfo?.TotalDelay ?? 0f, mask));
                }
            }
            return true;
        }
        finally
        {
            ClearWorkingSets();
        }
    }

    internal void ClearItems()
    {
        _items.Clear();
        _categoryCandidates.Clear();
    }

    internal void Clear()
    {
        _items.Clear();
        _categoryCandidates.Clear();
        _candidateItems.Clear();
        _seenItems.Clear();
        _categorySourceMasks.Clear();
        _sourceSlots.Clear();
        _groupedCandidates.Clear();
    }

    internal void ToggleFavorite(QuickUseWheelItem selectedItem)
    {
        var templateId = selectedItem.Item.TemplateId.ToString();
        var isFavorite = !_favoriteTemplateIds.Remove(templateId);
        if (isFavorite)
        {
            _favoriteTemplateIds.Add(templateId);
        }

        Configuration.QuickUseFavoriteTemplateIds.Value = string.Join(",", _favoriteTemplateIds);
        for (var index = 0; index < _items.Count; index++)
        {
            if (string.Equals(_items[index].Item.TemplateId.ToString(), templateId, StringComparison.Ordinal))
            {
                _items[index] = _items[index].WithFavorite(isFavorite);
            }
        }
    }

    internal static bool IsItemStillUsable(Player player, Item item)
    {
        if (player is null
            || !player
            || item is null
            || player.HealthController is null
            || player.InventoryController is null
            || player.InventoryController.Inventory is null)
        {
            return false;
        }

        return player.HealthController.IsAlive
            && PlayerOwnsItem(player, item)
            && player.InventoryController.Examined(item)
            && IsAtReachablePlace(player, item)
            && item.CheckAction(null).Succeeded
            && HasResource(item);
    }

    internal static Item? ResolveItemForUse(Player player, QuickUseWheelItem wheelItem)
    {
        Item? selectedItem = null;
        foreach (var item in wheelItem.GroupedItems)
        {
            if (!IsItemStillUsable(player, item))
            {
                continue;
            }

            if (selectedItem is null || ComparePreferredItems(player, item, selectedItem) < 0)
            {
                selectedItem = item;
            }
        }
        return selectedItem;
    }

    internal Item? ResolveItemForTemplate(Player player, string templateId)
    {
        if (!Populate(player))
        {
            return null;
        }
        try
        {
            Item? selectedItem = null;
            foreach (var wheelItem in _items)
            {
                if (!string.Equals(
                        wheelItem.Item.TemplateId.ToString(),
                        templateId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var candidate = ResolveItemForUse(player, wheelItem);
                if (candidate is not null
                    && (selectedItem is null || ComparePreferredItems(player, candidate, selectedItem) < 0))
                {
                    selectedItem = candidate;
                }
            }
            return selectedItem;
        }
        finally
        {
            ClearItems();
        }
    }

    private void AddWheelItem(Player player, IReadOnlyList<Item> groupedItems)
    {
        _items.Add(CreateWheelItem(player, groupedItems));
    }

    private QuickUseWheelItem CreateWheelItem(Player player, IReadOnlyList<Item> groupedItems)
    {
        var item = SelectRepresentativeItem(player, groupedItems);
        var isQueued = ItemAccessDelayPatch.IsQueuedForAccess(player, item);
        var isNextQueued = ItemAccessDelayPatch.IsNextQueuedItem(player, item);
        Configuration.ItemAccessDelayInfo? delayInfo = null;
        if (ItemAccessDelayPatch.TryGetEffectiveDelay(player, item, out var effectiveDelay))
        {
            delayInfo = effectiveDelay;
        }
        var hasResource = HasResource(item);
        var isUsable = !isQueued
            && hasResource
            && IsAtReachablePlace(player, item)
            && item.CheckAction(null).Succeeded;
        var quantity = GetCombinedQuantity(groupedItems);
        var state = isQueued
            ? isNextQueued ? "NEXT" : "ACCESSING"
            : !hasResource
                ? "EMPTY"
                : !isUsable ? "UNAVAILABLE" : GetItemState(item, groupedItems.Count == 1);
        if (groupedItems.Count > 1)
        {
            state = JoinItemState($"×{quantity}", state);
        }

        var sourceSlot = _sourceSlots.GetValueOrDefault(item, EquipmentSlot.Pockets);
        return new QuickUseWheelItem(
            item,
            [..groupedItems],
            quantity,
            _ui.GetItemDisplayName(item, 18),
            _ui.GetItemName(item),
            state,
            delayInfo,
            isUsable,
            isQueued,
            isNextQueued,
            _favoriteTemplateIds.Contains(item.TemplateId.ToString()),
            sourceSlot,
            RuntimeUiService.GetSlotName(sourceSlot),
            _ui.GetItemIcon(item));
    }

    internal Item? RevalidateCategoryItem(Player player, Item displayedItem, QuickUseCategory category)
    {
        // Repopulation rechecks configured source slots as well as current queue/action state.
        // Never substitute a grouped or replacement item for the one the player confirmed.
        if (!Populate(player)) return null;
        foreach (var candidate in _categoryCandidates)
        {
            if (ReferenceEquals(candidate.Value.Item, displayedItem) && candidate.Matches(category))
            {
                return IsItemStillUsable(player, displayedItem) ? displayedItem : null;
            }
        }
        return null;
    }

    private void AddCandidate(Item item, QuickUseCategory sourceCategory)
    {
        if (_seenItems.Add(item)) _candidateItems.Add(item);
        _categorySourceMasks[item] = _categorySourceMasks.GetValueOrDefault(item) | (1 << (int)sourceCategory);
    }

    private bool HasCategorySource(Item item, QuickUseCategory category)
    {
        // Record admission per category before deduplication. A flare admitted by the
        // grenade iterator must not bypass the separate flare source/visibility settings.
        var sourceCategory = category switch
        {
            QuickUseCategory.Medkits or QuickUseCategory.LightBleedTreatment
                or QuickUseCategory.HeavyBleedTreatment or QuickUseCategory.FractureTreatment
                or QuickUseCategory.Surgery or QuickUseCategory.Painkillers or QuickUseCategory.Stimulants
                => QuickUseCategory.AllMedical,
            _ => category,
        };
        return (_categorySourceMasks.GetValueOrDefault(item) & (1 << (int)sourceCategory)) != 0;
    }

    private Item SelectRepresentativeItem(Player player, IReadOnlyList<Item> groupedItems)
    {
        var selectedItem = groupedItems[0];
        for (var index = 1; index < groupedItems.Count; index++)
        {
            var candidate = groupedItems[index];
            var candidateRank = GetAvailabilityRank(player, candidate);
            var selectedRank = GetAvailabilityRank(player, selectedItem);
            if (candidateRank != selectedRank
                ? candidateRank < selectedRank
                : ComparePreferredItems(player, candidate, selectedItem) < 0)
            {
                selectedItem = candidate;
            }
        }
        return selectedItem;
    }

    private static int GetAvailabilityRank(Player player, Item item)
    {
        if (ItemAccessDelayPatch.IsQueuedForAccess(player, item))
        {
            return 1;
        }
        if (!HasResource(item))
        {
            return 3;
        }
        return IsAtReachablePlace(player, item)
            && item.CheckAction(null).Succeeded
                ? 0
                : 2;
    }

    private static int ComparePreferredItems(Player player, Item left, Item right)
    {
        var comparison = Configuration.QuickUseGroupedItemSelection.Value switch
        {
            Configuration.GroupedItemSelectionMode.LowestResourceFirst =>
                GetResourceValue(left).CompareTo(GetResourceValue(right)),
            Configuration.GroupedItemSelectionMode.HighestResourceFirst =>
                GetResourceValue(right).CompareTo(GetResourceValue(left)),
            Configuration.GroupedItemSelectionMode.FastestAccessFirst =>
                GetAccessDelay(player, left).CompareTo(GetAccessDelay(player, right)),
            _ => 0,
        };
        return comparison != 0 ? comparison : string.CompareOrdinal(left.Id, right.Id);
    }

    private static float GetAccessDelay(Player player, Item item)
    {
        return ItemAccessDelayPatch.TryGetEffectiveDelay(player, item, out var delayInfo)
                ? delayInfo.TotalDelay
                : 0f;
    }

    private static float GetResourceValue(Item item)
    {
        var medKit = item.GetItemComponent<MedKitComponent>();
        if (medKit != null)
        {
            return medKit.HpResource;
        }
        var foodDrink = item.GetItemComponent<FoodDrinkComponent>();
        if (foodDrink != null)
        {
            return foodDrink.HpPercent;
        }
        var resource = item.GetItemComponent<ResourceComponent>();
        return resource?.Value ?? item.StackObjectsCount;
    }

    private static int GetCombinedQuantity(IReadOnlyList<Item> groupedItems)
    {
        var quantity = 0;
        foreach (var item in groupedItems)
        {
            quantity += Math.Max(1, item.StackObjectsCount);
        }
        return quantity;
    }

    private static bool IsGroupable(Item item)
    {
        return item is Meds or FoodDrink or ThrowWeap
            || Configuration.FlareIds.Contains(item.TemplateId);
    }

    private static int CompareWheelItems(QuickUseWheelItem left, QuickUseWheelItem right)
    {
        var comparison = (left.IsFavorite ? 0 : 1).CompareTo(right.IsFavorite ? 0 : 1);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Array.IndexOf(SlotPriority, left.SourceSlot)
            .CompareTo(Array.IndexOf(SlotPriority, right.SourceSlot));
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = (left.Item is Meds ? 0 : 1).CompareTo(right.Item is Meds ? 0 : 1);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.OrdinalIgnoreCase.Compare(left.FullName, right.FullName);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.Item.Id, right.Item.Id);
    }

    private static bool PlayerOwnsItem(Player player, Item item)
    {
        var inventory = player.InventoryController?.Inventory;
        if (inventory is null)
        {
            return false;
        }

        foreach (var ownedItem in inventory.AllRealPlayerItems)
        {
            if (ReferenceEquals(ownedItem, item))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAtReachablePlace(Player player, Item item)
    {
        var controller = player.InventoryController;
        if (controller is null)
        {
            return false;
        }

        try
        {
            return controller.IsAtReachablePlace(item);
        }
        catch (NullReferenceException)
        {
            // Pack 'n Strap patches IsAtReachablePlace and can throw while walking
            // modded top-level equipment. Wheel candidates have already been found
            // through explicitly allowed player inventory slots, so retain the
            // original slot-based behavior when that external reachability check fails.
            return true;
        }
    }

    private static bool HasResource(Item item)
    {
        var medKit = item.GetItemComponent<MedKitComponent>();
        if (medKit != null)
        {
            return medKit.HpResource > 0f;
        }
        var foodDrink = item.GetItemComponent<FoodDrinkComponent>();
        if (foodDrink != null)
        {
            return foodDrink.HpPercent > 0f;
        }
        var resource = item.GetItemComponent<ResourceComponent>();
        return resource == null || resource.Value > 0f;
    }

    private static string GetItemState(Item item, bool includeStackQuantity = true)
    {
        if (item is Weapon weapon)
        {
            var magazineMaximum = weapon.GetMaxMagazineCount();
            var ammunition = magazineMaximum > 0
                ? $"{weapon.GetCurrentMagazineCount()}/{magazineMaximum}"
                : string.Empty;
            var repairable = weapon.Repairable;
            var durability = repairable != null && repairable.MaxDurability > 0f
                ? $"DUR {Mathf.RoundToInt(repairable.Durability / repairable.MaxDurability * 100f)}%"
                : string.Empty;
            return JoinItemState(ammunition, durability);
        }

        if (item is Magazine magazine)
        {
            return $"{magazine.Count}/{magazine.MaxCount} RDS";
        }

        var medKit = item.GetItemComponent<MedKitComponent>();
        if (medKit != null)
        {
            return $"{Mathf.CeilToInt(medKit.HpResource)}/{medKit.MaxHpResource} HP";
        }

        var foodDrink = item.GetItemComponent<FoodDrinkComponent>();
        if (foodDrink != null)
        {
            return FormatResource(foodDrink.HpPercent, foodDrink.MaxResource);
        }

        var resource = item.GetItemComponent<ResourceComponent>();
        if (resource != null)
        {
            return FormatResource(resource.Value, resource.MaxResource);
        }

        return includeStackQuantity && item.StackMaxSize > 1 ? $"×{item.StackObjectsCount}" : string.Empty;
    }

    private static string FormatResource(float current, float maximum)
    {
        return maximum > 0f
            ? $"{Mathf.CeilToInt(current)}/{Mathf.CeilToInt(maximum)}"
            : Mathf.CeilToInt(current).ToString();
    }

    private static string JoinItemState(string first, string second)
    {
        if (string.IsNullOrEmpty(first))
        {
            return second;
        }
        return string.IsNullOrEmpty(second) ? first : $"{first} • {second}";
    }

    private void ClearWorkingSets()
    {
        _candidateItems.Clear();
        _seenItems.Clear();
        _categorySourceMasks.Clear();
        _sourceSlots.Clear();
        _groupedCandidates.Clear();
    }

}
