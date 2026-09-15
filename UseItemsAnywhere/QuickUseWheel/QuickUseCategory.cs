using System.ComponentModel;

namespace UseItemsAnywhere.QuickUseWheel;

internal enum QuickUseCategory
{
    Unassigned,
    [Description("All Medical")] AllMedical,
    Medkits,
    [Description("Light-Bleed Treatment")] LightBleedTreatment,
    [Description("Heavy-Bleed Treatment")] HeavyBleedTreatment,
    [Description("Fracture Treatment")] FractureTreatment,
    Surgery,
    Painkillers,
    Stimulants,
    [Description("Food/Drink")] FoodDrink,
    Grenades,
    Guns,
    Melee,
    Flares,
}

internal static class QuickUseCategoryNames
{
    internal static string DisplayName(this QuickUseCategory category) => category switch
    {
        QuickUseCategory.Unassigned => "Unassigned",
        QuickUseCategory.AllMedical => "All Medical",
        QuickUseCategory.LightBleedTreatment => "Light-Bleed Treatment",
        QuickUseCategory.HeavyBleedTreatment => "Heavy-Bleed Treatment",
        QuickUseCategory.FractureTreatment => "Fracture Treatment",
        QuickUseCategory.FoodDrink => "Food/Drink",
        _ => category.ToString(),
    };
}
