// Only the native boundary is substituted. Tests compile the production inventory,
// classification, reservation, entry, and pagination code unchanged.
using EFT.InventoryLogic;
using UseItemsAnywhere.QuickUseWheel;

namespace UnityEngine
{
    public static class Mathf
    {
        public static int CeilToInt(float value) => (int)Math.Ceiling(value);
        public static int RoundToInt(float value) => (int)Math.Round(value);
    }
}

namespace EFT
{
    public sealed class Player
    {
        public InventoryController InventoryController { get; } = new();
        public HealthController HealthController { get; } = new();
        public static implicit operator bool(Player? player) => player is not null;
    }
    public sealed class HealthController { public bool IsAlive = true; }
    public sealed class InventoryController
    {
        public Inventory Inventory { get; } = new();
        public bool Examined(Item item) => item.Examined;
        public bool IsAtReachablePlace(Item item) => item.Reachable;
    }
}

namespace EFT.InventoryLogic
{
    public enum EquipmentSlot
    {
        FirstPrimaryWeapon, SecondPrimaryWeapon, Holster, Scabbard,
        Pockets, TacticalVest, ArmBand, Backpack, SecuredContainer,
    }
    public enum EDamageEffectType { HeavyBleeding, LightBleeding, Fracture, DestroyedPart, Pain }
    public class ItemTemplate;
    public class MedKitTemplate : ItemTemplate;
    public class StimulatorTemplate : ItemTemplate;
    public sealed class ModdedMedKitTemplate : MedKitTemplate;
    public sealed class ModdedStimulatorTemplate : StimulatorTemplate;
    public class Item(string id, string templateId)
    {
        public string Id { get; } = id;
        public string TemplateId { get; } = templateId;
        public ItemTemplate Template = new();
        public int StackObjectsCount = 1;
        public int StackMaxSize = 1;
        public bool Examined = true, Reachable = true, ActionAllowed = true;
        public bool Queued, NextQueued;
        public float AccessDelay;
        public EquipmentSlot Source = EquipmentSlot.Pockets;
        public List<object> Components { get; } = [];
        public T? GetItemComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
        public ActionResult CheckAction(object? _) => new(ActionAllowed);
    }
    public readonly record struct ActionResult(bool Succeeded);
    public sealed class Meds : Item
    {
        public HealthEffectsComponent HealthEffectsComponent { get; } = new();
        public MedKitComponent? MedKitComponent { get; }
        public Meds(string id, string templateId, ItemTemplate template, float? resource = null) : base(id, templateId)
        {
            Template = template;
            if (resource.HasValue)
            {
                MedKitComponent = new() { HpResource = resource.Value, MaxHpResource = 400 };
                Components.Add(MedKitComponent);
            }
        }
    }
    public sealed class DamageEffectSpecification { public int Cost; }
    public sealed class HealthEffectsComponent
    {
        public Dictionary<EDamageEffectType, DamageEffectSpecification>? DamageEffects { get; set; } = [];
    }
    public sealed class MedKitComponent { public float HpResource; public int MaxHpResource; }
    public sealed class FoodDrinkComponent { public float HpPercent = 100, MaxResource = 100; }
    public sealed class ResourceComponent { public float Value = 1, MaxResource = 1; }
    public sealed class KnifeComponent;
    public sealed class RepairableComponent { public float Durability = 100, MaxDurability = 100; }
    public sealed class Weapon(string id) : Item(id, "gun")
    {
        public int GetMaxMagazineCount() => 30;
        public int GetCurrentMagazineCount() => 30;
        public RepairableComponent? Repairable { get; } = new();
    }
    public sealed class Magazine(string id) : Item(id, "magazine") { public int Count = 30, MaxCount = 30; }
    public sealed class FoodDrink(string id) : Item(id, "food");
    public sealed class ThrowWeap(string id, string templateId = "grenade") : Item(id, templateId);
    public sealed class Inventory
    {
        public List<Item> AllRealPlayerItems { get; } = [];
        public IEnumerable<Item> GetItemsInSlots(IEnumerable<EquipmentSlot> slots) =>
            AllRealPlayerItems.Where(item => slots.Contains(item.Source));
    }
}

namespace EFT.UI.DragAndDrop { public sealed class ItemIcon; }

namespace UseItemsAnywhere.UI
{
    public sealed class RuntimeUiService
    {
        public void SetItemCachePlayer(EFT.Player player) { }
        public string GetItemDisplayName(Item item, int length) => item.TemplateId;
        public string GetItemName(Item item) => item.TemplateId;
        public EFT.UI.DragAndDrop.ItemIcon? GetItemIcon(Item item) => null;
        public static string GetSlotName(EquipmentSlot slot) => slot.ToString();
    }
}

namespace UseItemsAnywhere.Patches
{
    internal static class ItemAccessDelayPatch
    {
        public static bool IsQueuedForAccess(EFT.Player player, Item item) => item.Queued || item.NextQueued;
        public static bool IsNextQueuedItem(EFT.Player player, Item item) => item.NextQueued;
        public static bool TryGetEffectiveDelay(EFT.Player player, Item item, out Configuration.ItemAccessDelayInfo delay)
        {
            delay = new(item.AccessDelay);
            return true;
        }
    }
}

namespace UseItemsAnywhere
{
    internal sealed class Setting<T>(T value) { public T Value = value; }
    internal static class Configuration
    {
        internal enum GroupedItemSelectionMode { LowestResourceFirst, HighestResourceFirst, FastestAccessFirst }
        internal readonly record struct ItemAccessDelayInfo(float TotalDelay);
        internal static Setting<bool> QuickUseShowPrimAndSecWeapons = new(true), QuickUseShowMelee = new(true),
            QuickUseShowGrenades = new(true), QuickUseShowMeds = new(true), QuickUseShowFoodDrink = new(true),
            QuickUseShowFlares = new(true), QuickUseGroupIdenticalItems = new(true);
        internal static Setting<GroupedItemSelectionMode> QuickUseGroupedItemSelection = new(GroupedItemSelectionMode.LowestResourceFirst);
        internal static Setting<string> QuickUseFavoriteTemplateIds = new("");
        internal static Setting<QuickUseCategory>[] QuickUseCategoryPositions = Enumerable.Range(0, 8)
            .Select(_ => new Setting<QuickUseCategory>(QuickUseCategory.Unassigned)).ToArray();
        internal static bool HasQuickUseCategorySlots => QuickUseCategoryPositions.Any(entry => entry.Value != QuickUseCategory.Unassigned);
        internal static HashSet<string> FlareIds = ["flare"];
        internal static HashSet<EquipmentSlot> AllAllowedWeaponSlots = [..Enum.GetValues<EquipmentSlot>()];
        internal static HashSet<EquipmentSlot> AllAllowedMeleeSlots = [..Enum.GetValues<EquipmentSlot>()];
        internal static Setting<List<EquipmentSlot>> MedsSlots = new([..Enum.GetValues<EquipmentSlot>()]),
            GrenadeThrowSlots = new([..Enum.GetValues<EquipmentSlot>()]),
            FoodDrinkSlots = new([..Enum.GetValues<EquipmentSlot>()]),
            FlareSlots = new([..Enum.GetValues<EquipmentSlot>()]);
    }
}
