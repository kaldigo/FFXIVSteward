namespace Steward.Core;

public static class NonEquipmentProtection
{
    // Category and item disposal permissions are not evidence of replaceability.
    // These holds do not create a dresser destination or assume that housing ownership is known.
    public static string? Reason(ItemFacts item) => item.Equipment ? null : item.Source switch
    {
        SourceKind.Unknown => "Non-equipment acquisition is unresolved; preserve from sale and disposal",
        SourceKind.Special or SourceKind.AllianceRaid or SourceKind.SavageRaid or SourceKind.HighEnd =>
            "Special or difficult-to-replace non-equipment reward; preserve all copies",
        _ => null
    };
}
