using EFT;
using EFT.InventoryLogic;
using UseItemsAnywhere;
using UseItemsAnywhere.QuickUseWheel;
using UseItemsAnywhere.UI;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}

void ResetSettings()
{
    foreach (var position in Configuration.QuickUseCategoryPositions) position.Value = QuickUseCategory.Unassigned;
    Configuration.QuickUseFavoriteTemplateIds.Value = "";
    Configuration.QuickUseShowMeds.Value = true;
    Configuration.QuickUseShowFlares.Value = true;
    Configuration.QuickUseGroupIdenticalItems.Value = true;
    Configuration.QuickUseGroupedItemSelection.Value = Configuration.GroupedItemSelectionMode.LowestResourceFirst;
    Configuration.MedsSlots.Value = [..Enum.GetValues<EquipmentSlot>()];
}

QuickUseWheelInventory Inventory()
{
    var inventory = new QuickUseWheelInventory();
    inventory.Initialize(new RuntimeUiService());
    inventory.LoadFavorites();
    return inventory;
}

Meds Kit(string id, string template = "salewa", float resource = 400, float delay = 0)
{
    var kit = new Meds(id, template, new MedKitTemplate(), resource) { AccessDelay = delay };
    kit.HealthEffectsComponent.DamageEffects![EDamageEffectType.LightBleeding] = new() { Cost = 45 };
    kit.HealthEffectsComponent.DamageEffects[EDamageEffectType.HeavyBleeding] = new() { Cost = 175 };
    return kit;
}

// Pagination must neither shift the fixed compass page nor lose/duplicate ordinary entries.
foreach (var size in Enumerable.Range(4, 9))
foreach (var ordinaryCount in new[] { 0, 1, 7, 8, 9, 12, 13, 25 })
foreach (var pinned in new[] { false, true })
{
    var total = ordinaryCount + (pinned ? 8 : 0);
    var pageCount = QuickUseWheelPages.Count(total, size, pinned);
    var visited = new List<int>();
    for (var page = 0; page < pageCount; page++)
    {
        var range = QuickUseWheelPages.Range(page, total, size, pinned);
        if (pinned && page == 0)
        {
            Check(range == (0, 8), "Category geometry must always have eight positions");
        }
        visited.AddRange(Enumerable.Range(range.Start, range.Count));
    }
    Check(visited.SequenceEqual(Enumerable.Range(0, total)), "Paging must preserve every entry exactly once");
    Check(QuickUseWheelPages.Range(999, total, size, pinned)
        == QuickUseWheelPages.Range(pageCount - 1, total, size, pinned), "Shrinking pages clamp safely");
}
Console.WriteLine("PASS: fixed geometry and ordinary pagination at every supported page size.");

ResetSettings();
Check(!Configuration.HasQuickUseCategorySlots, "Default settings do not introduce a page");
Configuration.QuickUseCategoryPositions[4].Value = QuickUseCategory.Medkits;
Check(Configuration.HasQuickUseCategorySlots, "One configured position enables the page");
var player = new Player();
var raid = new object();
var slots = new QuickUseCategorySlots<QuickUseWheelItem>();
slots.SetContext(raid, player);
var inventory = Inventory();
var slowFavorite = Kit("slow-favorite", "favorite", delay: 2);
var fast = Kit("fast", "ordinary", delay: 0);
player.InventoryController.Inventory.AllRealPlayerItems.AddRange([slowFavorite, fast]);
Configuration.QuickUseFavoriteTemplateIds.Value = "favorite";
inventory.LoadFavorites();
inventory.Populate(player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == slowFavorite,
    "Usable favorite beats a faster nonfavorite");
var fasterFavorite = Kit("faster-favorite", "favorite", delay: 0);
player.InventoryController.Inventory.AllRealPlayerItems.Add(fasterFavorite);
inventory.Populate(player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == slowFavorite,
    "New candidates do not displace a still-eligible reservation");
slots.SetContext(raid, player);
inventory.ClearItems(); // Closing a wheel clears display data, not session reservations.
inventory.Populate(player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == slowFavorite,
    "Reopening in the same raid preserves selection");
slowFavorite.MedKitComponent!.HpResource = 0;
inventory.Populate(player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == fasterFavorite,
    "Depletion refills with the fastest usable favorite");
fasterFavorite.Queued = true;
inventory.Populate(player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == fast,
    "Queued items cannot occupy usable category slots");
Check(inventory.Items.Any(item => item.Item == fasterFavorite && item.IsQueued),
    "Queued items remain available on ordinary pages for their existing controls");
fast.NextQueued = true;
inventory.Populate(player);
Check(!slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates).HasValue,
    "Next-queued items also vacate their category reservation");
Check(inventory.Items.Any(item => item.Item == fast && item.IsNextQueued), "Next-queued mapping survives");
fast.NextQueued = false;
inventory.Populate(player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == fast,
    "Removing an item from the queue makes it eligible again");
player.InventoryController.Inventory.AllRealPlayerItems.Remove(fast);
inventory.Populate(player);
Check(!slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates).HasValue,
    "Removing the last usable item leaves an empty reservation");
Console.WriteLine("PASS: favorites, sticky refill, depletion, removal, and both queue states.");

ResetSettings();
Configuration.QuickUseCategoryPositions[4].Value = QuickUseCategory.Medkits;
inventory = Inventory();
player = new Player();
var alpha = Kit("b", "alpha", delay: 1);
var alphaFirst = Kit("a", "alpha", delay: 1);
var beta = Kit("a-beta", "beta", delay: 1);
player.InventoryController.Inventory.AllRealPlayerItems.AddRange([beta, alpha, alphaFirst]);
slots.SetContext(raid, player);
inventory.Populate(player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == alphaFirst,
    "Ties use template then item identifiers, regardless of enumeration order");
Check(slots.Resolve(0, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == alphaFirst,
    "Duplicate category assignments may share one item");
beta.AccessDelay = 0;
inventory.Populate(player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == alphaFirst,
    "Access changes do not reorder an eligible reservation");
slots.SetContext(new object(), player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == beta,
    "A new raid resets reservations even with the same player object");
Check(!slots.Resolve(4, QuickUseCategory.Unassigned, inventory.CategoryCandidates).HasValue,
    "Unassigning clears a slot");
beta.AccessDelay = 3;
inventory.Populate(player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == alphaFirst,
    "Reassigning chooses fresh candidates");
beta.AccessDelay = 0;
inventory.Populate(player);
slots.SetContext(raid, new Player());
Check(slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)?.Value.Item == beta,
    "A new player resets reservations");
slots.SetContext(null, null);
slots.SetContext(raid, player);
Check(slots.Resolve(4, QuickUseCategory.Medkits, []) == null, "No inventory leaves a slot empty after teardown");
Console.WriteLine("PASS: deterministic choices, duplicate slots, reassignment, and raid/player lifecycle.");

// Native template subclasses and effect costs, rather than a list of individual template IDs.
var multi = Kit("multi");
multi.Template = new ModdedMedKitTemplate();
multi.HealthEffectsComponent.DamageEffects![EDamageEffectType.Fracture] = new() { Cost = 50 };
Check(QuickUseCategoryClassifier.Matches(multi, QuickUseCategory.Medkits), "Inherited medkit template matches");
Check(QuickUseCategoryClassifier.Matches(multi, QuickUseCategory.AllMedical), "Medkit is also all-medical");
Check(QuickUseCategoryClassifier.Matches(multi, QuickUseCategory.HeavyBleedTreatment), "Multifunction bleeding support");
Check(QuickUseCategoryClassifier.Matches(multi, QuickUseCategory.FractureTreatment), "Multifunction fracture support");
multi.MedKitComponent!.HpResource = 174;
Check(!QuickUseCategoryClassifier.Matches(multi, QuickUseCategory.HeavyBleedTreatment), "Insufficient treatment resource excluded");
Check(QuickUseCategoryClassifier.Matches(multi, QuickUseCategory.LightBleedTreatment), "Lower-cost treatment remains eligible");
multi.MedKitComponent.HpResource = 175;
Check(QuickUseCategoryClassifier.Matches(multi, QuickUseCategory.HeavyBleedTreatment), "Exact treatment cost qualifies");
var bandage = new Meds("bandage", "bandage", new ItemTemplate());
bandage.HealthEffectsComponent.DamageEffects![EDamageEffectType.LightBleeding] = new() { Cost = 0 };
Check(QuickUseCategoryClassifier.Matches(bandage, QuickUseCategory.LightBleedTreatment), "Single-use bandage needs no resource component");
Check(!QuickUseCategoryClassifier.Matches(bandage, QuickUseCategory.Medkits), "Bandage is not a healing medkit");
bandage.HealthEffectsComponent.DamageEffects[EDamageEffectType.HeavyBleeding] = new() { Cost = 1 };
Check(!QuickUseCategoryClassifier.Matches(bandage, QuickUseCategory.HeavyBleedTreatment), "Positive costs require a resource component");
foreach (var (category, effect) in new[] {
    (QuickUseCategory.Surgery, EDamageEffectType.DestroyedPart),
    (QuickUseCategory.Painkillers, EDamageEffectType.Pain) })
{
    var treatment = new Meds("treatment", "treatment", new ItemTemplate());
    treatment.HealthEffectsComponent.DamageEffects![effect] = new() { Cost = 0 };
    Check(QuickUseCategoryClassifier.Matches(treatment, category), $"Matches {category} effect");
}
var stim = new Meds("stim", "stim", new ModdedStimulatorTemplate());
Check(QuickUseCategoryClassifier.Matches(stim, QuickUseCategory.Stimulants), "Inherited stimulator template matches");
stim.HealthEffectsComponent.DamageEffects = null;
Check(!QuickUseCategoryClassifier.Matches(stim, QuickUseCategory.Painkillers), "Missing treatment data fails closed");
var melee = new Item("knife", "knife");
melee.Components.Add(new KnifeComponent());
foreach (var (item, category) in new (Item, QuickUseCategory)[] {
    (new Weapon("gun"), QuickUseCategory.Guns), (melee, QuickUseCategory.Melee),
    (new FoodDrink("food"), QuickUseCategory.FoodDrink),
    (new ThrowWeap("grenade"), QuickUseCategory.Grenades),
    (new ThrowWeap("flare", "flare"), QuickUseCategory.Flares) })
{
    Check(QuickUseCategoryClassifier.Matches(item, category), $"Matches {category}");
    Check(!QuickUseCategoryClassifier.Matches(item, QuickUseCategory.AllMedical), "Nonmedical stays out of medical slots");
}
Check(!QuickUseCategoryClassifier.Matches(new ThrowWeap("flare", "flare"), QuickUseCategory.Grenades),
    "Flares do not replace explosive grenades");
Console.WriteLine("PASS: medical overlap, inherited templates, treatment costs, and broad categories.");

ResetSettings();
Configuration.QuickUseCategoryPositions[4].Value = QuickUseCategory.Medkits;
Configuration.QuickUseCategoryPositions[5].Value = QuickUseCategory.HeavyBleedTreatment;
player = new Player();
inventory = Inventory();
var low = Kit("low", resource: 30, delay: 2);
var full = Kit("full", delay: 0);
player.InventoryController.Inventory.AllRealPlayerItems.AddRange([low, full]);
inventory.Populate(player);
Check(inventory.Items.Count == 1 && inventory.Items[0].GroupedItems.Count == 2, "Ordinary identical grouping preserved");
Check(inventory.CategoryCandidates.Count == 2, "Category resolution can choose individual members of a group");
slots.SetContext(raid, player);
var pin = slots.Resolve(4, QuickUseCategory.Medkits, inventory.CategoryCandidates)!.Value.Value;
Check(pin.Item == full && !pin.IsGrouped, "Pinned exact item follows access delay, not grouped resource preference");
Check(QuickUseWheelInventory.ResolveItemForUse(player, inventory.Items[0]) == low,
    "Ordinary grouped use retains its existing resource preference");
var entry = new QuickUseWheelEntry("Medkits", pin.FullName, pin.State, pin.SourceName,
    true, false, false, true, true, null, pin, QuickUseCategory.Medkits);
inventory.ToggleFavorite(entry.Item!.Value);
Check(Configuration.QuickUseFavoriteTemplateIds.Value == full.TemplateId, "Favorite action maps to the displayed template");
Check(inventory.Items.All(item => item.IsFavorite), "Ordinary group receives favorite update too");
Check(inventory.RevalidateCategoryItem(player, entry.Item.Value.Item, entry.Category) == full,
    "Confirmation uses the displayed item");
full.ActionAllowed = false;
Check(inventory.RevalidateCategoryItem(player, full, QuickUseCategory.Medkits) is null,
    "Confirmation cancels on invalid displayed item instead of substituting group member");
full.ActionAllowed = true;
full.MedKitComponent!.HpResource = 174;
Check(inventory.RevalidateCategoryItem(player, full, QuickUseCategory.HeavyBleedTreatment) is null,
    "Confirmation rechecks treatment cost changes");
full.MedKitComponent.HpResource = 400;
full.Source = EquipmentSlot.Backpack;
Configuration.MedsSlots.Value = [EquipmentSlot.Pockets];
Check(inventory.RevalidateCategoryItem(player, full, QuickUseCategory.Medkits) is null, "Confirmation rechecks allowed source slots");
Configuration.MedsSlots.Value.Add(EquipmentSlot.Backpack);
full.Examined = false;
inventory.Populate(player);
Check(inventory.CategoryCandidates.All(candidate => candidate.Value.Item != full), "Unexamined items excluded");
full.Examined = true;
full.Reachable = false;
inventory.Populate(player);
Check(inventory.CategoryCandidates.All(candidate => candidate.Value.Item != full), "Unreachable items excluded");
full.Reachable = true;
Configuration.QuickUseShowMeds.Value = false;
inventory.Populate(player);
Check(inventory.CategoryCandidates.Count == 0, "Visibility restrictions also apply to pins");
Configuration.QuickUseShowMeds.Value = true;
Configuration.QuickUseGroupIdenticalItems.Value = false;
inventory.Populate(player);
Check(inventory.Items.Count == 2 && inventory.CategoryCandidates.Count == 2, "Grouping-off remains compatible");
var placeholder = new QuickUseWheelEntry("Surgery", "Surgery", "No available item", "", false, false, false, true, false, null,
    category: QuickUseCategory.Surgery);
Check(placeholder.Item is null && !placeholder.IsUsable && !placeholder.IsBlank, "Empty assigned slot has no fake inventory item");
var blankEntry = new QuickUseWheelEntry("", "", "", "", false, false, false, false, false, null, isBlank: true);
Check(blankEntry.Item is null && blankEntry.IsBlank && !blankEntry.IsUsable, "Unassigned entry cannot activate");
ResetSettings();
inventory.Populate(player);
Check(!Configuration.HasQuickUseCategorySlots && inventory.CategoryCandidates.Count == 0 && inventory.Items.Count == 1,
    "Disabling all pins restores ordinary inventory behavior");

// Multiple native classifications must not bypass category-specific source restrictions.
Configuration.QuickUseCategoryPositions[1].Value = QuickUseCategory.Flares;
Configuration.FlareSlots.Value = [EquipmentSlot.Pockets];
var flare = new ThrowWeap("flare", "flare") { Source = EquipmentSlot.Backpack };
player.InventoryController.Inventory.AllRealPlayerItems.Add(flare);
inventory.Populate(player);
Check(inventory.Items.Any(item => item.Item == flare), "Ordinary inventory preserves existing grenade admission");
Check(inventory.CategoryCandidates.All(candidate => candidate.Value.Item != flare),
    "Grenade admission cannot bypass flare source restrictions");
Configuration.FlareSlots.Value.Add(EquipmentSlot.Backpack);
inventory.Populate(player);
Check(inventory.CategoryCandidates.Any(candidate => candidate.Value.Item == flare), "Allowed flare source supplies the pin");
Configuration.QuickUseShowFlares.Value = false;
inventory.Populate(player);
Check(inventory.CategoryCandidates.All(candidate => candidate.Value.Item != flare), "Grenade admission cannot bypass hidden flares");
Configuration.QuickUseShowFlares.Value = true;
flare.Queued = true;
Check(inventory.RevalidateCategoryItem(player, flare, QuickUseCategory.Flares) is null,
    "Confirmation rechecks queue state even between refreshes");
flare.Queued = false;
player.InventoryController.Inventory.AllRealPlayerItems.Remove(flare);
Check(inventory.RevalidateCategoryItem(player, flare, QuickUseCategory.Flares) is null,
    "Confirmation rejects an item that left the inventory");
Console.WriteLine("PASS: inventory integration, grouping, favorites, eligibility, exact-item confirmation, and placeholders.");
Console.WriteLine($"PASS: {checks} assertions.");
