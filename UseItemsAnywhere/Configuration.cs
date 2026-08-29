using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using DrakiaXYZ.VersionChecker;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace UseItemsAnywhere;

public static class Configuration
{
    public static readonly HashSet<EquipmentSlot> DefaultWeaponSlots =
        [EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster];
    private static ConfigEntry<List<EquipmentSlot>> _weaponSlots = null!; 
    public static HashSet<EquipmentSlot> AllAllowedWeaponSlots => [..DefaultWeaponSlots, .._weaponSlots.Value];
    public static ConfigEntry<List<EquipmentSlot>> GrenadeThrowSlots = null!;
    public static ConfigEntry<List<EquipmentSlot>> _meleeSlots = null!;
    public static HashSet<EquipmentSlot> AllAllowedMeleeSlots => [EquipmentSlot.Scabbard, .._meleeSlots.Value];
    public static ConfigEntry<List<EquipmentSlot>> FlareSlots = null!; 
    public static ConfigEntry<List<EquipmentSlot>> ReloadSlots = null!;
    public static ConfigEntry<List<EquipmentSlot>> MedsSlots = null!;
    public static ConfigEntry<List<EquipmentSlot>> FoodDrinkSlots = null!;
    public static ConfigEntry<List<EquipmentSlot>> AllOtherItems = null!; 
    
    public static ConfigEntry<bool> EnableSlotDelays = null!; 
    public static ConfigEntry<bool> ShowTimerPanel = null!; 
    public static ConfigEntry<KeyboardShortcut> ClearItemAccessDelay = null!;
    private static ConfigEntry<float> _additionalContainerNestingDelay = null!;
    
    public static ConfigEntry<bool> EnableQuickUseWheel = null!;
    public static ConfigEntry<KeyboardShortcut> QuickUseWheelKey = null!;
    public static ConfigEntry<int> QuickUseItemsPerPage = null!;
    public static ConfigEntry<bool> QuickUseShowSourceSlot = null!;
    public static ConfigEntry<bool> QuickUseShowPrimAndSecWeapons = null!;
    public static ConfigEntry<bool> QuickUseShowMelee = null!;
    public static ConfigEntry<bool> QuickUseShowMeds = null!;
    public static ConfigEntry<bool> QuickUseShowFoodDrink = null!;
    public static ConfigEntry<bool> QuickUseShowFlares = null!;
    
    public static readonly HashSet<MongoID> FlareIds =
    [
        new("62178c4d4ecf221597654e3d"), // Red Flare
        new("624c0b3340357b5f566e8766"), // Yellow Flare
        new("6217726288ed9f0845317459"), // green Flare
        new("62178be9d0050232da3485d9"), // white Flare
    ];
    
    private static readonly List<ConfigEntryBase> ConfigEntries = [];
    private static readonly Dictionary<EquipmentSlot, ConfigEntry<float>> SlotAccessDelayConfigurations = [];

    private const string SlotConfigurations = "Slot Configurations";
    private const string SlotAccessDelays = "Slot Access Delays";
    private const string QuickUseWheel = "Quick Use Wheel";
    private const float SlotToggleWidth = 135f;
    
    private static readonly (EquipmentSlot Slot, string DisplayName)[] ConfigurableSlots =
    [
        (EquipmentSlot.TacticalVest, "Tactical Vest"),
        (EquipmentSlot.Pockets, "Pockets"),
        (EquipmentSlot.Backpack, "Backpack"),
        (EquipmentSlot.SecuredContainer, "Secured Container"),
        (EquipmentSlot.ArmBand, "Arm Band"),
    ];
    
    public static void Init(ConfigFile configFile)
    {
        AddEquipmentSlotListConverter();

        InitSlotUsage(configFile);
        InitSlotAccessDelay(configFile);
        InitQuickUseWheel(configFile);
        
        RecalcOrder();
    }

    private static void InitQuickUseWheel(ConfigFile configFile)
    {
        ConfigEntries.Add(EnableQuickUseWheel = configFile.Bind(
            QuickUseWheel,
            "Enable Quick Use Wheel",
            true,
            new ConfigDescription(
                "Configures whether or not the quick-use item wheel is enabled.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(QuickUseWheelKey = configFile.Bind(
            QuickUseWheel,
            "Quick Use Wheel Key",
            new KeyboardShortcut(KeyCode.H),
            new ConfigDescription(
                "Key held to open and select from the quick-use item wheel.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(QuickUseItemsPerPage = configFile.Bind(
            QuickUseWheel,
            "Items Per Page",
            8,
            new ConfigDescription(
                "Maximum number of items displayed on each wheel page.",
                new AcceptableValueRange<int>(4, 12),
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(QuickUseShowSourceSlot = configFile.Bind(
            QuickUseWheel,
            "Show Source Slot",
            true,
            new ConfigDescription(
                "Configures whether or not each item displays its source equipment slot.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));
        
        ConfigEntries.Add(QuickUseShowPrimAndSecWeapons = configFile.Bind(
            QuickUseWheel,
            "Show Guns",
            true,
            new ConfigDescription(
                "Configures whether or not to show guns in the quick wheel.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));
        
        ConfigEntries.Add(QuickUseShowMelee = configFile.Bind(
            QuickUseWheel,
            "Show Melee",
            true,
            new ConfigDescription(
                "Configures whether or not to show melee weapon in the quick wheel.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));
        
        ConfigEntries.Add(QuickUseShowMeds = configFile.Bind(
            QuickUseWheel,
            "Show Meds",
            true,
            new ConfigDescription(
                "Configures whether or not to show meds in the quick wheel.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));
        
        ConfigEntries.Add(QuickUseShowFoodDrink = configFile.Bind(
            QuickUseWheel,
            "Show Food/Drink",
            true,
            new ConfigDescription(
                "Configures whether or not to show food and drink in the quick wheel.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));
        
        ConfigEntries.Add(QuickUseShowFlares = configFile.Bind(
            QuickUseWheel,
            "Show Flares",
            true,
            new ConfigDescription(
                "Configures whether or not to show flares in the quick wheel.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));
    }

    private static void InitSlotUsage(ConfigFile configFile)
    {
         ConfigEntries.Add(_weaponSlots = configFile.Bind(
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
        
        ConfigEntries.Add(_meleeSlots = configFile.Bind(
            SlotConfigurations,
            "Melee Slots",
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
        
        ConfigEntries.Add(FlareSlots = configFile.Bind(
            SlotConfigurations,
            "Flare Slots",
            new List<EquipmentSlot>
            {
                EquipmentSlot.TacticalVest,
                EquipmentSlot.Pockets,
            },
            new ConfigDescription(
                "Configures which slots can supply flares.",
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
        
        ConfigEntries.Add(FlareSlots = configFile.Bind(
            SlotConfigurations,
            "Flare Slots",
            new List<EquipmentSlot>
            {
                EquipmentSlot.TacticalVest,
                EquipmentSlot.Pockets,
            },
            new ConfigDescription(
                "Configures which slots can supply flares.",
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
        
        ConfigEntries.Add(FoodDrinkSlots = configFile.Bind(
            SlotConfigurations,
            "Food/Drink Slots",
            new List<EquipmentSlot>
            {
                EquipmentSlot.TacticalVest,
                EquipmentSlot.Pockets,
            },
            new ConfigDescription(
                "Configures which slots can bind Food/Drinks.",
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
    }

    private static void InitSlotAccessDelay(ConfigFile configFile)
    {
        ConfigEntries.Add(EnableSlotDelays = configFile.Bind(
            SlotAccessDelays,
            "Enable Slot Delays",
            false,
            new ConfigDescription(
                "Configures whether or not to use the configurable delays below when using items from those slots.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));
        
        ConfigEntries.Add(ShowTimerPanel = configFile.Bind(
            SlotAccessDelays,
            "Show Timer Panel",
            true,
            new ConfigDescription(
                "Configures whether or not to show the themed item-use delay timer.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(ClearItemAccessDelay = configFile.Bind(
            SlotAccessDelays,
            "Clear Item Access Delay",
            KeyboardShortcut.Empty,
            new ConfigDescription(
                "Key used to cancel the currently queued item use and clear its access delay.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(_additionalContainerNestingDelay = configFile.Bind(
            SlotAccessDelays,
            "Additional Backpack Nesting Delay",
            0.5f,
            new ConfigDescription(
                "Additional delay, in seconds, for each container layer between the item and the equipped backpack, regardless of container type.",
                new AcceptableValueRange<float>(0f, 5f),
                new VersionChecker.ConfigurationManagerAttributes())));
        
        BindSlotAccessDelay(configFile, EquipmentSlot.Pockets, "Pockets", 0f);
        BindSlotAccessDelay(configFile, EquipmentSlot.TacticalVest, "Tactical Vest", 0.25f);
        BindSlotAccessDelay(configFile, EquipmentSlot.ArmBand, "Arm Band", 0.5f);
        BindSlotAccessDelay(configFile, EquipmentSlot.Backpack, "Backpack", 1.5f);
        BindSlotAccessDelay(configFile, EquipmentSlot.SecuredContainer, "Secured Container", 2f);
    }
    
    internal static float GetItemAccessDelay(Inventory inventory, Item item)
    {
        foreach (var (slot, delayConfiguration) in SlotAccessDelayConfigurations)
        {
            if (inventory.GetItemsInSlots([slot]).Contains(item))
            {
                var nestingDelay = slot == EquipmentSlot.Backpack
                    ? GetBackpackNestingDelay(inventory, item)
                    : 0f;

                return delayConfiguration.Value + nestingDelay;
            }
        }

        return 0f;
    }

    private static float GetBackpackNestingDelay(Inventory inventory, Item item)
    {
        var equippedBackpack = inventory.Equipment
            .GetSlot(EquipmentSlot.Backpack)
            .ContainedItem;

        if (equippedBackpack == null)
        {
            return 0f;
        }

        var nestingDepth = 0;
        foreach (var parentItem in item.GetAllParentItems())
        {
            if (ReferenceEquals(parentItem, equippedBackpack))
            {
                return nestingDepth * _additionalContainerNestingDelay.Value;
            }

            nestingDepth++;
        }

        return 0f;
    }

    private static void BindSlotAccessDelay(
        ConfigFile configFile,
        EquipmentSlot slot,
        string displayName,
        float defaultDelay)
    {
        var entry = configFile.Bind(
            SlotAccessDelays,
            $"{displayName} Delay",
            defaultDelay,
            new ConfigDescription(
                $"Additional delay, in seconds, before using a consumable stored in {displayName.ToLowerInvariant()}.",
                new AcceptableValueRange<float>(0f, 5f),
                new VersionChecker.ConfigurationManagerAttributes()));

        SlotAccessDelayConfigurations[slot] = entry;
        ConfigEntries.Add(entry);
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
