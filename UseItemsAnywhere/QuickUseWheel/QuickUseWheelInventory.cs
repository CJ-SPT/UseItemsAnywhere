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
    private readonly List<Item> _candidateItems = [];
    private readonly HashSet<Item> _seenItems = [];
    private readonly Dictionary<Item, EquipmentSlot> _sourceSlots = [];
    private readonly Dictionary<string, List<Item>> _groupedCandidates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _favoriteTemplateIds = new(StringComparer.Ordinal);
    private RuntimeUiService _ui = null!;

    internal IReadOnlyList<QuickUseWheelItem> Items => _items;

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

    internal void Populate(Player player)
    {
        _items.Clear();
        var controller = player.InventoryController;
        var inventory = controller.Inventory;
        _ui.SetItemCachePlayer(player);
        _candidateItems.Clear();
        _seenItems.Clear();
        _sourceSlots.Clear();
        _groupedCandidates.Clear();

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
                if (item is Weapon && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }

        if (Configuration.QuickUseShowMelee.Value)
        {
            foreach (var item in inventory.GetItemsInSlots(Configuration.AllAllowedMeleeSlots))
            {
                if (item.GetItemComponent<KnifeComponent>() != null && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }
        
        if (Configuration.QuickUseShowGrenades.Value)
        {
            foreach (var item in inventory.GetItemsInSlots(Configuration.GrenadeThrowSlots.Value))
            {
                if (item is ThrowWeap && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }

        if (Configuration.QuickUseShowMeds.Value)
        {
            foreach (var item in inventory.GetItemsInSlots(Configuration.MedsSlots.Value))
            {
                if (item is Meds && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }

        if (Configuration.QuickUseShowFoodDrink.Value)
        {
            foreach (var item in inventory.GetItemsInSlots(Configuration.FoodDrinkSlots.Value))
            {
                if (item is FoodDrink && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }

        if (Configuration.QuickUseShowFlares.Value)
        {
            foreach (var item in inventory.GetItemsInSlots(Configuration.FlareSlots.Value))
            {
                if (item is not null
                    && Configuration.FlareIds.Contains(item.TemplateId)
                    && _seenItems.Add(item))
                {
                    _candidateItems.Add(item);
                }
            }
        }

        foreach (var item in _candidateItems)
        {
            if (!controller.Examined(item))
            {
                continue;
            }

            if (Configuration.QuickUseGroupIdenticalItems.Value && IsGroupable(item))
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
            AddWheelItem(player, group);
        }

        _items.Sort(CompareWheelItems);
        _candidateItems.Clear();
        _seenItems.Clear();
        _sourceSlots.Clear();
        _groupedCandidates.Clear();
    }

    internal void ClearItems()
    {
        _items.Clear();
    }

    internal void Clear()
    {
        _items.Clear();
        _candidateItems.Clear();
        _seenItems.Clear();
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
        return player.HealthController.IsAlive
            && PlayerOwnsItem(player, item)
            && player.InventoryController.Examined(item)
            && player.InventoryController.IsAtReachablePlace(item)
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
        Populate(player);
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
        var item = SelectRepresentativeItem(player, groupedItems);
        var isQueued = ItemAccessDelayPatch.IsQueuedForAccess(player, item);
        var hasResource = HasResource(item);
        var isUsable = !isQueued
            && hasResource
            && player.InventoryController.IsAtReachablePlace(item)
            && item.CheckAction(null).Succeeded;
        var quantity = GetCombinedQuantity(groupedItems);
        var state = isQueued
            ? "QUEUED"
            : !hasResource
                ? "EMPTY"
                : !isUsable ? "UNAVAILABLE" : GetItemState(item, groupedItems.Count == 1);
        if (groupedItems.Count > 1)
        {
            state = JoinItemState($"×{quantity}", state);
        }

        var sourceSlot = _sourceSlots.GetValueOrDefault(item, EquipmentSlot.Pockets);
        _items.Add(new QuickUseWheelItem(
            item,
            [..groupedItems],
            quantity,
            _ui.GetItemDisplayName(item, 18),
            _ui.GetItemName(item),
            state,
            isUsable,
            isQueued,
            _favoriteTemplateIds.Contains(item.TemplateId.ToString()),
            sourceSlot,
            RuntimeUiService.GetSlotName(sourceSlot),
            _ui.GetItemIcon(item)));
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
        return player.InventoryController.IsAtReachablePlace(item)
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
        return Configuration.EnableSlotDelays.Value
            && Configuration.TryGetItemAccessDelay(player.InventoryController.Inventory, item, out var delayInfo)
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
        foreach (var ownedItem in player.InventoryController.Inventory.AllRealPlayerItems)
        {
            if (ReferenceEquals(ownedItem, item))
            {
                return true;
            }
        }
        return false;
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

}
