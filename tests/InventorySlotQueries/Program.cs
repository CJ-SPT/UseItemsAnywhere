using System.Reflection;
using System.Reflection.Emit;
using EFT.InventoryLogic;
using HarmonyLib;
using Mono.Cecil;
using UseItemsAnywhere;
using UseItemsAnywhere.Patches;

var checks = 0;
void Check(bool passed, string description)
{
    checks++;
    if (!passed) throw new InvalidOperationException(description);
}

var player = new InventoryController(15);
var bot = new InventoryController(14); // no ArmBand entry
foreach (var controller in new[] { player, bot })
foreach (var slot in new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest, EquipmentSlot.Backpack, EquipmentSlot.SecuredContainer })
{
    controller.Inventory.Equipment._cachedSlots[(int)slot] = new Slot();
}
player.Inventory.Equipment._cachedSlots[(int)EquipmentSlot.ArmBand] = new Slot();
var global = new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest, EquipmentSlot.ArmBand, (EquipmentSlot)99 };
Inventory.FastAccessSlots = global;
var originalValues = global.ToArray();
bot.Inventory.Equipment.GetSlot(EquipmentSlot.Pockets).Items.Add(new Magazine("bot-pocket"));
bot.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).Items.Add(new Magazine("bot-rig"));
var nativeFailed = false;
try { bot.GetReachableItemsOfTypeNonAlloc(new List<Magazine>(), null); }
catch (IndexOutOfRangeException) { nativeFailed = true; }
Check(nativeFailed, "Reproduces unchecked native slot indexing with an extended shared array");
var botMags = new List<Magazine>();
InventorySlotQueries.CollectNativeItems(bot, botMags, null);
Check(botMags.Select(item => item.Id).SequenceEqual(new[] { "bot-pocket", "bot-rig" }),
    "Bot safely collects valid native magazines while ignoring absent slots");

Configuration.ReloadSlots.Value = [EquipmentSlot.Backpack, EquipmentSlot.SecuredContainer, EquipmentSlot.ArmBand,
    EquipmentSlot.Pockets, EquipmentSlot.TacticalVest];
foreach (var slot in Configuration.ReloadSlots.Value)
{
    var container = player.Inventory.Equipment.GetSlot(slot);
    container.Items.Add(new Magazine(slot.ToString()));
    container.Items.Add(new Ammo(slot.ToString()));
    container.Items.Add(new ThrowWeap(slot.ToString()));
}
var playerMags = new List<Magazine>();
InventorySlotQueries.CollectReloadItems(player, playerMags, null);
Check(playerMags.Count == 5, "All five configured player slots work without truncating to shared-array length");
Check(playerMags.Select(item => item.Id).SequenceEqual(Configuration.ReloadSlots.Value.Select(slot => slot.ToString())),
    "Player search preserves configured ordering");
Check(InventorySlotQueries.ReloadItems<Ammo>(player, item => item.Id == "ArmBand").Single().Id == "ArmBand",
    "Allocated UI/launcher query retains predicates and item type");
Configuration.GrenadeThrowSlots.Value = [EquipmentSlot.ArmBand, EquipmentSlot.Backpack];
var grenades = new List<ThrowWeap>();
InventorySlotQueries.CollectGrenades(player, grenades, item => item.Id != "Backpack");
Check(grenades.Single().Id == "ArmBand", "Grenade search uses its own configuration and predicate");
Configuration.ReloadSlots.Value = [];
playerMags.Clear();
InventorySlotQueries.CollectReloadItems(player, playerMags, null);
Check(playerMags.Count == 0, "Empty configuration does not fall back to other slots");
Check(ReferenceEquals(global, Inventory.FastAccessSlots) && global.SequenceEqual(originalValues),
    "Player, UI, and bot queries never alter the shared array or its reference");

var weirdSlots = new[] { (EquipmentSlot)(-1), EquipmentSlot.Pockets, EquipmentSlot.Dogtag, EquipmentSlot.Pockets,
    EquipmentSlot.ArmBand, (EquipmentSlot)99, EquipmentSlot.TacticalVest };
Check(InventorySlotQueries.SelectExistingSlots(weirdSlots, bot.Inventory.Equipment._cachedSlots)
    .SequenceEqual(new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest }),
    "Negative, out-of-range, absent, and duplicate slots are filtered in order");
Check(InventorySlotQueries.SelectExistingSlots(weirdSlots, []).Length == 0, "Empty equipment has no eligible slots");

Configuration.ReloadSlots.Value = [EquipmentSlot.Backpack];
var nestedCalls = 0;
InventorySlotQueries.CollectReloadItems(player, playerMags, item =>
{
    var nestedBotMags = new List<Magazine>();
    InventorySlotQueries.CollectNativeItems(bot, nestedBotMags, null);
    Check(nestedBotMags.Count == 2, "Nested bot lookup cannot inherit player-only reload slots");
    nestedCalls++;
    return true;
});
Check(nestedCalls == 1, "Nested lookup exercised");
var threw = false;
try { InventorySlotQueries.CollectReloadItems<Magazine>(player, [], _ => throw new InvalidOperationException("predicate")); }
catch (InvalidOperationException error) when (error.Message == "predicate") { threw = true; }
Check(threw && ReferenceEquals(global, Inventory.FastAccessSlots) && global.SequenceEqual(originalValues),
    "Exceptions propagate without requiring shared-state restoration");
var sentinel = new Magazine("already-in-output");
botMags = [sentinel];
InventorySlotQueries.CollectNativeItems(bot, botMags, item => item.Id == "bot-rig");
Check(botMags.Count == 2 && ReferenceEquals(botMags[0], sentinel), "Nonalloc query appends and preserves caller-owned output");
Console.WriteLine("PASS: native failure reproduction, isolated slots, predicate behavior, nesting, exceptions, and bot bounds.");

// Check every targeted call against the real installed assembly, without running game code.
var assemblyPath = args.Length > 0 ? args[0] : Path.GetFullPath("../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll");
using var native = AssemblyDefinition.ReadAssembly(assemblyPath);
IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
    }
}
var nativeTypes = AllTypes(native.MainModule.Types).ToDictionary(type => type.FullName.Replace('/', '+'));
var patchedCallers = new HashSet<string>();
foreach (var target in InventoryQueryPatches.Targets)
{
    Check(nativeTypes.TryGetValue(target.Type, out var nativeType), $"Native type exists: {target.Type}");
    var nativeMethod = nativeType!.Methods.Single(method => method.Name == target.Method);
    var query = nativeMethod.Body.Instructions.Select(instruction => instruction.Operand).OfType<GenericInstanceMethod>()
        .Single(method => method.DeclaringType.FullName == typeof(InventoryController).FullName
            && method.Name.StartsWith("GetReachableItemsOfType", StringComparison.Ordinal));
    var itemType = typeof(Inventory).Assembly.GetType(query.GenericArguments.Single().FullName, true)!;
    var original = typeof(Inventory).Assembly.GetType(target.Type, true)!.GetMethod(target.Method)!;
    var called = typeof(InventoryController).GetMethod(query.Name)!.MakeGenericMethod(itemType);
    var instruction = new CodeInstruction(OpCodes.Callvirt, called);
    var label = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
    instruction.labels.Add(label);
    instruction.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
    var rewritten = InventoryQueryPatches.Transpiler([new CodeInstruction(OpCodes.Nop), instruction], original).ToArray();
    var replacement = (MethodInfo)rewritten[1].operand;
    Check(replacement.Name == target.Query && replacement.GetGenericArguments().Single() == itemType,
        $"Correct typed replacement for {target.Type}.{target.Method}");
    Check(replacement.ReturnType == called.ReturnType
        && replacement.GetParameters().Select(p => p.ParameterType)
            .SequenceEqual(new[] { typeof(InventoryController) }.Concat(called.GetParameters().Select(p => p.ParameterType))),
        "Replacement preserves stack signature including instance receiver");
    Check(rewritten[1].opcode == OpCodes.Call && rewritten[1].labels.Single().Equals(label)
        && rewritten[1].blocks.Count == 1 && rewritten[0].opcode == OpCodes.Nop,
        "Transpiler preserves branches, exception boundaries, and unrelated instructions");
    patchedCallers.Add(target.Type + "." + target.Method);
}
foreach (var type in nativeTypes.Values)
foreach (var method in type.Methods.Where(method => method.HasBody))
{
    if (!method.Body.Instructions.Any(instruction => instruction.Operand is GenericInstanceMethod called
        && called.DeclaringType.FullName == typeof(InventoryController).FullName
        && called.Name.StartsWith("GetReachableItemsOfType", StringComparison.Ordinal))) continue;
    if (type.FullName == typeof(InventoryController).FullName) continue; // generic allocating wrapper
    Check(patchedCallers.Contains(type.FullName.Replace('/', '+') + "." + method.Name),
        $"Native query caller covered: {type.FullName}.{method.Name}");
}
var missingRejected = false;
try { InventoryQueryPatches.Transpiler([], typeof(BotReload).GetMethod(nameof(BotReload.GetMagazineForReload))!); }
catch (InvalidOperationException) { missingRejected = true; }
Check(missingRejected, "Missing query is detected instead of silently shipping an incomplete patch");
Console.WriteLine($"PASS: all {patchedCallers.Count} installed query callers, typed transpilers, stack signatures, and patch mismatch detection.");
Console.WriteLine($"PASS: {checks} assertions.");
