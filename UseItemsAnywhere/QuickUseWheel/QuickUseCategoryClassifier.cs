using EFT.InventoryLogic;

namespace UseItemsAnywhere.QuickUseWheel;

internal static class QuickUseCategoryClassifier
{
    internal static bool Matches(Item item, QuickUseCategory category) => category switch
    {
        QuickUseCategory.AllMedical => item is Meds,
        QuickUseCategory.Medkits => item is Meds && item.Template is MedKitTemplate,
        QuickUseCategory.LightBleedTreatment => CanTreat(item, EDamageEffectType.LightBleeding),
        QuickUseCategory.HeavyBleedTreatment => CanTreat(item, EDamageEffectType.HeavyBleeding),
        QuickUseCategory.FractureTreatment => CanTreat(item, EDamageEffectType.Fracture),
        QuickUseCategory.Surgery => CanTreat(item, EDamageEffectType.DestroyedPart),
        QuickUseCategory.Painkillers => CanTreat(item, EDamageEffectType.Pain),
        QuickUseCategory.Stimulants => item is Meds && item.Template is StimulatorTemplate,
        QuickUseCategory.FoodDrink => item is FoodDrink,
        QuickUseCategory.Grenades => item is ThrowWeap && !Configuration.FlareIds.Contains(item.TemplateId),
        QuickUseCategory.Guns => item is Weapon,
        QuickUseCategory.Melee => item.GetItemComponent<KnifeComponent>() != null,
        QuickUseCategory.Flares => Configuration.FlareIds.Contains(item.TemplateId),
        _ => false,
    };

    private static bool CanTreat(Item item, EDamageEffectType effect)
    {
        if (item is not Meds meds || meds.HealthEffectsComponent.DamageEffects is not { } effects
            || !effects.TryGetValue(effect, out var specification))
        {
            return false;
        }

        // Zero-cost single-use treatments need no medkit resource. General depletion
        // and action eligibility are checked by the inventory before classification.
        return specification.Cost <= 0 || meds.MedKitComponent is { } kit && kit.HpResource >= specification.Cost;
    }
}
