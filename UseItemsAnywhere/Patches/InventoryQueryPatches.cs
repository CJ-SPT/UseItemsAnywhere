using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT.InventoryLogic;
using HarmonyLib;

namespace UseItemsAnywhere.Patches;

// Patch the concrete callers, not the generic query itself: Mono can share generic
// method bodies. Each replacement retains the original item type and predicate.
internal static class InventoryQueryPatches
{
    private const string HarmonyId = "com.cj.useFromAnywhere.inventory-queries";

    internal static readonly (string Type, string Method, string Query)[] Targets =
    [
        ("EFT.FirearmHandsInputTranslator", "Reload", nameof(InventorySlotQueries.ReloadItems)),
        ("EFT.FirearmHandsInputTranslator", "ReloadBarrels", nameof(InventorySlotQueries.CollectReloadItems)),
        ("EFT.FirearmHandsInputTranslator", "ReloadRevolverDrum", nameof(InventorySlotQueries.CollectReloadItems)),
        ("EFT.FirearmHandsInputTranslator", "ReloadWithAmmo", nameof(InventorySlotQueries.CollectReloadItems)),
        ("EFT.FirearmHandsInputTranslator", "ReloadExternalMagazine", nameof(InventorySlotQueries.CollectReloadItems)),
        ("EFT.FirearmHandsInputTranslator+CG_LoadAmmoToChamber", "method_0", nameof(InventorySlotQueries.CollectReloadItems)),
        ("EFT.PlayerInputTranslator", "vmethod_1", nameof(InventorySlotQueries.CollectGrenades)),
        ("EFT.UI.ItemUiContext", "FindSuitableMagazine", nameof(InventorySlotQueries.ReloadItems)),
        ("EFT.UI.AmmoSelector+CG_FindAmmoForWeapon", "method_0", nameof(InventorySlotQueries.ReloadItems)),
        ("EFT.UI.AmmoSelector+CG_FindAmmoForWeapon", "method_6", nameof(InventorySlotQueries.ReloadItems)),
        ("BotReload", "GetMagazineForReload", nameof(InventorySlotQueries.CollectNativeItems)),
        ("BotReload", "AddAmmoToMagazines", nameof(InventorySlotQueries.CollectNativeItems)),
        ("BotReloadOnlyBarrel", "CanReload", nameof(InventorySlotQueries.CollectNativeItems)),
        ("BotReloadRevolver", "CanReload", nameof(InventorySlotQueries.CollectNativeItems)),
    ];

    internal static void Enable()
    {
        var harmony = new Harmony(HarmonyId);
        var transpiler = new HarmonyMethod(AccessTools.Method(typeof(InventoryQueryPatches), nameof(Transpiler)));
        // Resolve all targets first so a game-version mismatch cannot silently drop reload paths.
        var methods = Targets.Select(target =>
        {
            var type = typeof(Inventory).Assembly.GetType(target.Type, true)!;
            return AccessTools.DeclaredMethod(type, target.Method)
                ?? throw new MissingMethodException(target.Type, target.Method);
        }).ToArray();
        try
        {
            foreach (var method in methods) harmony.Patch(method, transpiler: transpiler);
        }
        catch
        {
            harmony.UnpatchSelf();
            throw;
        }
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var target = Targets.Single(entry => entry.Type == __originalMethod.DeclaringType!.FullName
            && entry.Method == __originalMethod.Name);
        var replacement = typeof(InventorySlotQueries).GetMethod(target.Query, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(InventorySlotQueries).FullName, target.Query);
        var changed = 0;
        var result = instructions.ToList();
        foreach (var instruction in result)
        {
            if ((instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
                || instruction.operand is not MethodInfo called
                || called.DeclaringType != typeof(InventoryController)
                || !called.IsGenericMethod
                || (called.Name != nameof(InventoryController.GetReachableItemsOfType)
                    && called.Name != nameof(InventoryController.GetReachableItemsOfTypeNonAlloc)))
            {
                continue;
            }
            var itemType = called.GetGenericArguments()[0];
            if (itemType != typeof(Ammo) && itemType != typeof(Magazine) && itemType != typeof(ThrowWeap))
            {
                throw new InvalidOperationException($"Unexpected inventory query type in {__originalMethod}: {itemType}");
            }
            var expectedParameters = target.Query == nameof(InventorySlotQueries.ReloadItems) ? 1 : 2;
            if (called.GetParameters().Length != expectedParameters)
            {
                throw new InvalidOperationException($"Inventory query signature changed in {__originalMethod}");
            }
            // Keep branch labels and exception boundaries on the original instruction.
            instruction.opcode = OpCodes.Call;
            instruction.operand = replacement.MakeGenericMethod(itemType);
            changed++;
        }
        if (changed != 1)
        {
            throw new InvalidOperationException($"Expected one inventory query in {__originalMethod}, found {changed}");
        }
        return result;
    }
}
