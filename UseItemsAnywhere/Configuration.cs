using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using DrakiaXYZ.VersionChecker;
using EFT.InventoryLogic;
using UnityEngine;

namespace UseItemsAnywhere;

public static class Configuration
{
    private static readonly List<ConfigEntryBase> ConfigEntries = [];

    private static readonly (EquipmentSlot Slot, string DisplayName)[] ConfigurableSlots =
    [
        (EquipmentSlot.TacticalVest, "Tactical Vest"),
        (EquipmentSlot.Pockets, "Pockets"),
        (EquipmentSlot.Backpack, "Backpack"),
        (EquipmentSlot.SecuredContainer, "Secured Container"),
        (EquipmentSlot.ArmBand, "Arm Band"),
    ];

    public static readonly HashSet<EquipmentSlot> DefaultWeaponSlots =
        [EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster];
    
    private const string SlotConfigurations = "Slot Configurations";
    private const float SlotToggleWidth = 135f;
    
    public static ConfigEntry<List<EquipmentSlot>> WeaponSlots; 
    public static ConfigEntry<List<EquipmentSlot>> GrenadeThrowSlots; 
    public static ConfigEntry<List<EquipmentSlot>> ReloadSlots;
    public static ConfigEntry<List<EquipmentSlot>> MedsSlots;
    public static ConfigEntry<List<EquipmentSlot>> AllOtherItems; 

    public static void Init(ConfigFile configFile)
    {
        AddEquipmentSlotListConverter();

        ConfigEntries.Add(WeaponSlots = configFile.Bind(
            SlotConfigurations,
            "Weapon Slots",
            new List<EquipmentSlot>
            {
            },
            new ConfigDescription(
                "Configures which slots can supply weapons. Default weapon slots are always active and cannot be altered.",
                null,
                new VersionChecker.ConfigurationManagerAttributes
                {
                    CustomDrawer = EquipmentSlotListDrawer,
                })));
        
        ConfigEntries.Add(GrenadeThrowSlots = configFile.Bind(
            SlotConfigurations,
            "Grenade Throwing Slots",
            new List<EquipmentSlot>
            {
                EquipmentSlot.TacticalVest,
                EquipmentSlot.Pockets,
            },
            new ConfigDescription(
                "Configures which slots can supply grenades.",
                null,
                new VersionChecker.ConfigurationManagerAttributes
                {
                    CustomDrawer = EquipmentSlotListDrawer,
                })));

        ConfigEntries.Add(ReloadSlots = configFile.Bind(
            SlotConfigurations,
            "Reload Slots",
            new List<EquipmentSlot>
            {
                EquipmentSlot.TacticalVest,
                EquipmentSlot.Pockets,
            },
            new ConfigDescription(
                "Configures which slots can supply magazines/ammo when reloading.",
                null,
                new VersionChecker.ConfigurationManagerAttributes
                {
                    CustomDrawer = EquipmentSlotListDrawer,
                })));
        
        ConfigEntries.Add(MedsSlots = configFile.Bind(
            SlotConfigurations,
            "Meds Slots",
            new List<EquipmentSlot>
            {
                EquipmentSlot.TacticalVest,
                EquipmentSlot.Pockets,
            },
            new ConfigDescription(
                "Configures which slots can bind meds.",
                null,
                new VersionChecker.ConfigurationManagerAttributes
                {
                    CustomDrawer = EquipmentSlotListDrawer,
                })));
        
        ConfigEntries.Add(AllOtherItems = configFile.Bind(
            SlotConfigurations,
            "All Other Items",
            new List<EquipmentSlot>
            {
                EquipmentSlot.TacticalVest,
                EquipmentSlot.Pockets,
            },
            new ConfigDescription(
                "Configures which slots can bind all other items without explicit configs.",
                null,
                new VersionChecker.ConfigurationManagerAttributes
                {
                    CustomDrawer = EquipmentSlotListDrawer,
                })));
        
        RecalcOrder();
    }

    private static void AddEquipmentSlotListConverter()
    {
        TomlTypeConverter.AddConverter(
            typeof(List<EquipmentSlot>),
            new TypeConverter
            {
                ConvertToString = (value, _) => string.Join(", ", (List<EquipmentSlot>)value),
                ConvertToObject = (value, _) => DeserializeEquipmentSlots(value),
            });
    }

    private static List<EquipmentSlot> DeserializeEquipmentSlots(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var slots = new List<EquipmentSlot>();
        foreach (var slotName in value.Split(','))
        {
            if (!Enum.TryParse(slotName.Trim(), true, out EquipmentSlot slot))
            {
                throw new FormatException($"'{slotName.Trim()}' is not a valid equipment slot.");
            }

            if (!slots.Contains(slot))
            {
                slots.Add(slot);
            }
        }

        return slots;
    }

    private static void EquipmentSlotListDrawer(ConfigEntryBase entry)
    {
        if (entry.BoxedValue is not List<EquipmentSlot> currentSlots)
        {
            GUILayout.Label("Unable to edit this slot list.", GUILayout.ExpandWidth(true));
            return;
        }

        var selectedSlots = new HashSet<EquipmentSlot>(currentSlots);
        var changed = false;

        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        for (var index = 0; index < ConfigurableSlots.Length; index += 2)
        {
            GUILayout.BeginHorizontal();
            changed |= DrawSlotToggle(ConfigurableSlots[index], selectedSlots);

            if (index + 1 < ConfigurableSlots.Length)
            {
                changed |= DrawSlotToggle(ConfigurableSlots[index + 1], selectedSlots);
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();

        if (changed)
        {
            entry.BoxedValue = ConfigurableSlots
                .Where(option => selectedSlots.Contains(option.Slot))
                .Select(option => option.Slot)
                .ToList();
        }
    }

    private static bool DrawSlotToggle(
        (EquipmentSlot Slot, string DisplayName) option,
        ISet<EquipmentSlot> selectedSlots)
    {
        var wasSelected = selectedSlots.Contains(option.Slot);
        var isSelected = GUILayout.Toggle(
            wasSelected,
            option.DisplayName,
            GUILayout.Width(SlotToggleWidth));

        if (isSelected == wasSelected)
        {
            return false;
        }

        if (isSelected)
        {
            selectedSlots.Add(option.Slot);
        }
        else
        {
            selectedSlots.Remove(option.Slot);
        }

        return true;
    }
    
    private static void RecalcOrder()
    {
        // Set the Order field for all settings, to avoid unnecessary changes when adding new settings
        var settingOrder = ConfigEntries.Count;
        foreach (var attributes in ConfigEntries.Select(entry => entry.Description.Tags[0] as VersionChecker.ConfigurationManagerAttributes))
        {
            attributes?.Order = settingOrder;

            settingOrder--;
        }
    }
}
