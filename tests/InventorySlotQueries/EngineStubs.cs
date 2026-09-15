namespace EFT.InventoryLogic
{
    // Values match the installed 40743 enum, including ArmBand at index 14.
    public enum EquipmentSlot { Backpack = 4, SecuredContainer = 5, TacticalVest = 6, Pockets = 8, Dogtag = 13, ArmBand = 14 }
    public class Item(string id) { public string Id { get; } = id; }
    public sealed class Ammo(string id) : Item(id);
    public sealed class Magazine(string id) : Item(id);
    public sealed class ThrowWeap(string id) : Item(id);
    public sealed class Slot { public List<Item> Items { get; } = []; }
    public sealed class ContainerCollection;
    public sealed class InventoryEquipment(int size)
    {
        public Slot[] _cachedSlots = new Slot[size];
        public Slot GetSlot(EquipmentSlot slot) => _cachedSlots[(int)slot];
    }
    public sealed class Inventory(int size)
    {
        public static EquipmentSlot[] FastAccessSlots = [EquipmentSlot.Pockets, EquipmentSlot.TacticalVest];
        public InventoryEquipment Equipment { get; } = new(size);
    }
    public sealed class InventoryController(int size)
    {
        public Inventory Inventory { get; } = new(size);
        public void GetAcceptableItemsNonAlloc<TItem>(EquipmentSlot[] slots, IList<TItem> items,
            Predicate<TItem>? predicate, Predicate<ContainerCollection>? goDeeperPredicate) where TItem : Item
        {
            foreach (var slot in slots)
            foreach (var item in Inventory.Equipment.GetSlot(slot).Items.OfType<TItem>())
            {
                if (predicate?.Invoke(item) != false) items.Add(item);
            }
        }
        public IEnumerable<TItem> GetReachableItemsOfType<TItem>(Predicate<TItem>? predicate) where TItem : Item
        {
            var items = new List<TItem>();
            GetReachableItemsOfTypeNonAlloc(items, predicate);
            return items;
        }
        public void GetReachableItemsOfTypeNonAlloc<TItem>(IList<TItem> items, Predicate<TItem>? predicate) where TItem : Item =>
            GetAcceptableItemsNonAlloc(Inventory.FastAccessSlots, items, predicate, null);
    }
}

namespace UseItemsAnywhere
{
    internal sealed class Setting<T>(T value) { internal T Value = value; }
    internal static class Configuration
    {
        internal static Setting<List<EFT.InventoryLogic.EquipmentSlot>> ReloadSlots = new([]), GrenadeThrowSlots = new([]);
    }
}

// Only identities for the production transpiler's routing. Actual call sites and
// signatures are inspected separately in the installed assembly with Cecil.
namespace EFT
{
    public class FirearmHandsInputTranslator
    {
        public void Reload() { }
        public void ReloadBarrels() { }
        public void ReloadRevolverDrum() { }
        public void ReloadWithAmmo() { }
        public void ReloadExternalMagazine() { }
        public class CG_LoadAmmoToChamber { public void method_0() { } }
    }
    public class PlayerInputTranslator { public void vmethod_1() { } }
}
namespace EFT.UI
{
    public class ItemUiContext { public void FindSuitableMagazine() { } }
    public class AmmoSelector
    {
        public class CG_FindAmmoForWeapon
        {
            public void method_0() { }
            public void method_6() { }
        }
    }
}
public class BotReload { public void GetMagazineForReload() { } public void AddAmmoToMagazines() { } }
public class BotReloadOnlyBarrel { public void CanReload() { } }
public class BotReloadRevolver { public void CanReload() { } }
