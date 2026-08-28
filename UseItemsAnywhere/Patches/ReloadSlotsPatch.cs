using System;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace UseItemsAnywhere.Patches;

internal static class ReloadSlotScope
{
    private static readonly FieldInfo FastAccessSlotsField =
        AccessTools.Field(typeof(Inventory), nameof(Inventory.FastAccessSlots))
        ?? throw new MissingFieldException(typeof(Inventory).FullName, nameof(Inventory.FastAccessSlots));

    public static void Enter(out ReloadSlotState state)
    {
        var fastAccessSlots = (EquipmentSlot[])FastAccessSlotsField.GetValue(null)!;
        state = new ReloadSlotState(fastAccessSlots, [..fastAccessSlots]);

        // FastAccessSlots is static readonly, so Mono can retain the array reference.
        // Change that array in place instead of replacing the field reference.
        // Fill unused slots with dog-tags.
        Array.Fill(fastAccessSlots, EquipmentSlot.Dogtag);

        var reloadSlots = Configuration.ReloadSlots.Value;
        for (var index = 0; index < Math.Min(reloadSlots.Count, fastAccessSlots.Length); index++)
        {
            fastAccessSlots[index] = reloadSlots[index];
        }
    }

    public static Exception? Exit(Exception? exception, ReloadSlotState? state)
    {
        if (state != null)
        {
            Array.Copy(state.PreviousSlots, state.FastAccessSlots, state.FastAccessSlots.Length);
        }

        return exception;
    }
}

internal sealed class ReloadSlotState
{
    public ReloadSlotState(EquipmentSlot[] fastAccessSlots, EquipmentSlot[] previousSlots)
    {
        FastAccessSlots = fastAccessSlots;
        PreviousSlots = previousSlots;
    }

    public EquipmentSlot[] FastAccessSlots { get; }
    public EquipmentSlot[] PreviousSlots { get; }
}

internal sealed class ReloadSlotsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(FirearmHandsInputTranslator),
            nameof(FirearmHandsInputTranslator.Reload),
            Type.EmptyTypes);
    }

    [PatchPrefix]
    private static void Prefix(out ReloadSlotState __state)
    {
        ReloadSlotScope.Enter(out __state);
    }

    [PatchFinalizer]
    private static Exception? Finalizer(Exception? __exception, ReloadSlotState? __state)
    {
        return ReloadSlotScope.Exit(__exception, __state);
    }
}

internal sealed class QuickReloadSlotsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(FirearmHandsInputTranslator),
            nameof(FirearmHandsInputTranslator.QuickReload),
            Type.EmptyTypes);
    }

    [PatchPrefix]
    private static void Prefix(out ReloadSlotState __state)
    {
        ReloadSlotScope.Enter(out __state);
    }

    [PatchFinalizer]
    private static Exception? Finalizer(Exception? __exception, ReloadSlotState? __state)
    {
        return ReloadSlotScope.Exit(__exception, __state);
    }
}
