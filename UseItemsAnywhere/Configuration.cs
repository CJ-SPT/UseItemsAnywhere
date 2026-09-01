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
    public enum PendingUseMode
    {
        Ignore,
        CancelAndReplace,
        QueueOne,
        OpenWheel,
    }

    public enum GroupedItemSelectionMode
    {
        LowestResourceFirst,
        HighestResourceFirst,
        FastestAccessFirst,
    }

    public static readonly HashSet<EquipmentSlot> DefaultWeaponSlots =
        [EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster];
    private static ConfigEntry<List<EquipmentSlot>> _weaponSlots = null!; 
    public static HashSet<EquipmentSlot> AllAllowedWeaponSlots => [..DefaultWeaponSlots, .._weaponSlots.Value];
    public static ConfigEntry<List<EquipmentSlot>> GrenadeThrowSlots = null!;
    private static ConfigEntry<List<EquipmentSlot>> _meleeSlots = null!;
    public static HashSet<EquipmentSlot> AllAllowedMeleeSlots => [EquipmentSlot.Scabbard, .._meleeSlots.Value];
    public static ConfigEntry<List<EquipmentSlot>> FlareSlots = null!; 
    public static ConfigEntry<List<EquipmentSlot>> ReloadSlots = null!;
    public static ConfigEntry<List<EquipmentSlot>> MedsSlots = null!;
    public static ConfigEntry<List<EquipmentSlot>> FoodDrinkSlots = null!;
    public static ConfigEntry<List<EquipmentSlot>> AllOtherItems = null!; 
    
    public static ConfigEntry<bool> EnableSlotDelays = null!; 
    public static ConfigEntry<bool> ShowTimerPanel = null!; 
    public static ConfigEntry<bool> TimerSounds = null!;
    public static ConfigEntry<PendingUseMode> PendingItemUseBehavior = null!;
    public static ConfigEntry<KeyboardShortcut> ClearItemAccessDelay = null!;
    private static ConfigEntry<float> _additionalContainerNestingDelay = null!;
    
    public static ConfigEntry<bool> EnableQuickUseWheel = null!;
    public static ConfigEntry<KeyboardShortcut> QuickUseWheelKey = null!;
    public static ConfigEntry<bool> EnableWeaponDeviceWheel = null!;
    public static ConfigEntry<KeyboardShortcut> WeaponDeviceWheelKey = null!;
    public static ConfigEntry<bool> QuickUseTapLastItem = null!;
    public static ConfigEntry<float> QuickUseWheelHoldDuration = null!;
    public static ConfigEntry<bool> QuickUseWheelSounds = null!;
    public static ConfigEntry<int> QuickUseItemsPerPage = null!;
    public static ConfigEntry<bool> QuickUseGroupIdenticalItems = null!;
    public static ConfigEntry<GroupedItemSelectionMode> QuickUseGroupedItemSelection = null!;
    public static ConfigEntry<bool> QuickUseShowSourceSlot = null!;
    public static ConfigEntry<bool> QuickUseShowItemState = null!;
    internal static ConfigEntry<string> QuickUseFavoriteTemplateIds = null!;
    public static ConfigEntry<bool> QuickUseShowPrimAndSecWeapons = null!;
    public static ConfigEntry<bool> QuickUseShowMelee = null!;
    public static ConfigEntry<bool> QuickUseShowGrenades = null!;
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
                "Key tapped to reuse the last wheel item or held to open and select from the quick-use item wheel.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(EnableWeaponDeviceWheel = configFile.Bind(
            QuickUseWheel,
            "Enable Weapon Device Wheel",
            true,
            new ConfigDescription(
                "Configures whether or not the firearm-control wheel for fire modes and tactical devices is enabled.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(WeaponDeviceWheelKey = configFile.Bind(
            QuickUseWheel,
            "Weapon Device Wheel Key",
            new KeyboardShortcut(KeyCode.H, KeyCode.LeftAlt),
            new ConfigDescription(
                "Key held to select fire modes and control tactical devices on the firearm currently in hand.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(QuickUseTapLastItem = configFile.Bind(
            QuickUseWheel,
            "Tap Uses Last Item",
            true,
            new ConfigDescription(
                "Uses the last item template selected from the wheel when the wheel key is tapped.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(QuickUseWheelHoldDuration = configFile.Bind(
            QuickUseWheel,
            "Wheel Hold Duration",
            0.25f,
            new ConfigDescription(
                "Time, in seconds, the wheel key must be held before the quick-use wheel opens.",
                new AcceptableValueRange<float>(0.1f, 1f),
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(QuickUseWheelSounds = configFile.Bind(
            QuickUseWheel,
            "Wheel Sounds",
            true,
            new ConfigDescription(
                "Plays interface sounds when opening, navigating, or confirming the quick-use wheel.",
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

        ConfigEntries.Add(QuickUseGroupIdenticalItems = configFile.Bind(
            QuickUseWheel,
            "Group Identical Items",
            true,
            new ConfigDescription(
                "Groups consumables, grenades, and flares with the same item template into one wheel segment.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(QuickUseGroupedItemSelection = configFile.Bind(
            QuickUseWheel,
            "Grouped Item Selection",
            GroupedItemSelectionMode.LowestResourceFirst,
            new ConfigDescription(
                "Controls which usable item is selected from a group: preserve fuller items, use fuller items first, or use the item with the shortest access delay.",
                null,
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
        
        ConfigEntries.Add(QuickUseShowGrenades = configFile.Bind(
            QuickUseWheel,
            "Show Grenades",
            true,
            new ConfigDescription(
                "Configures whether or not to show grenades in the quick wheel.",
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
                "Configures which slots can supply melee weapons.",
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

        QuickUseFavoriteTemplateIds = configFile.Bind(
            QuickUseWheel,
            "Favorite Item Templates",
            string.Empty,
            new ConfigDescription(
                "Persisted item template identifiers favorited from the quick-use wheel.",
                null,
                new VersionChecker.ConfigurationManagerAttributes
                {
                    Browsable = false,
                }));

        ConfigEntries.Add(QuickUseShowItemState = configFile.Bind(
            QuickUseWheel,
            "Show Item State",
            true,
            new ConfigDescription(
                "Shows remaining resources, ammunition, stack size, or durability beneath each wheel item.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(TimerSounds = configFile.Bind(
            SlotAccessDelays,
            "Timer Sounds",
            true,
            new ConfigDescription(
                "Plays subtle interface sounds when an item-access delay completes or is cancelled.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(PendingItemUseBehavior = configFile.Bind(
            SlotAccessDelays,
            "Pending Item Use Behavior",
            PendingUseMode.CancelAndReplace,
            new ConfigDescription(
                "Controls item-use attempts made while an access delay is active: ignore them, cancel and use the latest item, queue the first extra item, or open the wheel to choose a replacement.",
                null,
                new VersionChecker.ConfigurationManagerAttributes())));

        ConfigEntries.Add(ClearItemAccessDelay = configFile.Bind(
            SlotAccessDelays,
            "Clear Item Access Delay",
            KeyboardShortcut.Empty,
            new ConfigDescription(
                "Key used to cancel the current pending access and clear the next queued item.",
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
    
    internal static bool TryGetItemAccessDelay(Inventory inventory, Item item, out ItemAccessDelayInfo delayInfo)
    {
        foreach (var (slot, delayConfiguration) in SlotAccessDelayConfigurations)
        {
            if (inventory.GetItemsInSlots([slot]).Contains(item))
            {
                var nestingDepth = slot == EquipmentSlot.Backpack
                    ? GetBackpackNestingDepth(inventory, item)
                    : 0;
                var nestingDelay = nestingDepth * _additionalContainerNestingDelay.Value;
                delayInfo = new ItemAccessDelayInfo(
                    delayConfiguration.Value + nestingDelay,
                    slot,
                    delayConfiguration.Value,
                    nestingDepth,
                    nestingDelay);
                return true;
            }
        }

        delayInfo = default;
        return false;
    }

    private static int GetBackpackNestingDepth(Inventory inventory, Item item)
    {
        var equippedBackpack = inventory.Equipment
            .GetSlot(EquipmentSlot.Backpack)
            .ContainedItem;

        if (equippedBackpack == null)
        {
            return 0;
        }

        var nestingDepth = 0;
        foreach (var parentItem in item.GetAllParentItems())
        {
            if (ReferenceEquals(parentItem, equippedBackpack))
            {
                return nestingDepth;
            }

            nestingDepth++;
        }

        return 0;
    }

    internal readonly struct ItemAccessDelayInfo
    {
        internal ItemAccessDelayInfo(
            float totalDelay,
            EquipmentSlot sourceSlot,
            float baseDelay,
            int nestingDepth,
            float nestingDelay)
        {
            TotalDelay = totalDelay;
            SourceSlot = sourceSlot;
            BaseDelay = baseDelay;
            NestingDepth = nestingDepth;
            NestingDelay = nestingDelay;
        }

        internal float TotalDelay { get; }
        internal EquipmentSlot SourceSlot { get; }
        internal float BaseDelay { get; }
        internal int NestingDepth { get; }
        internal float NestingDelay { get; }
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
