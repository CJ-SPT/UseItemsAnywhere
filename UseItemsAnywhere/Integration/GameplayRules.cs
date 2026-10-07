using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using EFT.InventoryLogic;
using Newtonsoft.Json;

namespace UseItemsAnywhere.Integration;

public enum ItemCategory { Weapons, Melee, Grenades, Flares, Reload, Meds, FoodDrink, Other }

// Wire/configuration boundary only. GameplayRules always takes a defensive copy.
public sealed class GameplayRulesData
{
    [JsonProperty(Required = Required.Always)]
    public Dictionary<ItemCategory, EquipmentSlot[]> Slots { get; set; } = new();
    [JsonProperty(Required = Required.Always)]
    public Dictionary<EquipmentSlot, float> Delays { get; set; } = new();
    [JsonProperty(Required = Required.Always)]
    public bool EnableDelays { get; set; }
    [JsonProperty(Required = Required.Always)]
    public float NestingDelay { get; set; }
    [JsonProperty(Required = Required.Always)]
    public bool CancelOnMovement { get; set; }
    [JsonProperty(Required = Required.Always)]
    public bool CancelOnDamage { get; set; }
}

public sealed class GameplayRules
{
    private readonly Dictionary<ItemCategory, ReadOnlyCollection<EquipmentSlot>> _slots = new();
    public IReadOnlyDictionary<EquipmentSlot, float> Delays { get; }
    public bool EnableDelays { get; }
    public float NestingDelay { get; }
    public bool CancelOnMovement { get; }
    public bool CancelOnDamage { get; }

    public GameplayRules(GameplayRulesData data)
    {
        if (data == null || data.Slots == null || data.Delays == null
            || data.Slots.Count != Enum.GetValues(typeof(ItemCategory)).Length
            || !ValidDelay(data.NestingDelay))
            throw new ArgumentException("Invalid gameplay rules.");
        foreach (ItemCategory category in Enum.GetValues(typeof(ItemCategory)))
        {
            if (!data.Slots.TryGetValue(category, out var slots) || slots == null || slots.Length > 32
                || slots.Any(slot => !Enum.IsDefined(typeof(EquipmentSlot), slot)))
                throw new ArgumentException("Invalid item access slots.");
            _slots.Add(category, Array.AsReadOnly(slots.Distinct().ToArray()));
        }
        var required = new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest,
            EquipmentSlot.ArmBand, EquipmentSlot.Backpack, EquipmentSlot.SecuredContainer };
        if (data.Delays.Count != required.Length
            || required.Any(slot => !data.Delays.TryGetValue(slot, out var delay) || !ValidDelay(delay)))
            throw new ArgumentException("Invalid item access delays.");
        Delays = new ReadOnlyDictionary<EquipmentSlot, float>(new Dictionary<EquipmentSlot, float>(data.Delays));
        EnableDelays = data.EnableDelays;
        NestingDelay = data.NestingDelay;
        CancelOnMovement = data.CancelOnMovement;
        CancelOnDamage = data.CancelOnDamage;
    }

    public IReadOnlyList<EquipmentSlot> Slots(ItemCategory category) => _slots[category];

    public GameplayRulesData ToData() => new()
    {
        Slots = _slots.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()),
        Delays = Delays.ToDictionary(pair => pair.Key, pair => pair.Value),
        EnableDelays = EnableDelays,
        NestingDelay = NestingDelay,
        CancelOnMovement = CancelOnMovement,
        CancelOnDamage = CancelOnDamage,
    };

    private static bool ValidDelay(float value) => !float.IsNaN(value) && value >= 0 && value <= 5;
}
