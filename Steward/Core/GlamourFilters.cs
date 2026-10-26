namespace Steward.Core;

public sealed class GlamourFilters
{
    public bool NativePlates { get; set; }=true;
    public bool Glamaholic { get; set; }=true;
    public bool EorzeaCollection { get; set; }=true;
    public bool LevelOne { get; set; }=true;
    public bool GlamourDesign { get; set; }=true;
    public bool NonmarketableBlue { get; set; }=true;
    public bool HigherRarity { get; set; }=true;
    public bool SpecialRewards { get; set; }=true;
    public bool AllianceRaids { get; set; }=true;
    public bool SavageRaids { get; set; }=true;
    public bool HighEndDuties { get; set; }=true;
    public bool NormalRaids { get; set; }
    public bool NormalDungeons { get; set; }
    public HashSet<uint> Categories { get; set; }=[];
    public HashSet<uint> ExcludedCategories { get; set; }=[];
}
[Flags]
public enum GlamourMatch
{
    None=0,Armoire=1,Pending=2,Supplemental=4,SavedOutfit=8,Common=16,LevelOne=32,Design=64,
    Blue=128,Higher=256,Special=512,Alliance=1024,Savage=2048,HighEnd=4096,NormalRaid=8192,Dungeon=16384,Unknown=32768
}
public static class GlamourProtection
{
    public static bool ReferencesReady(GlamourFilters f,bool native,bool saved)=>(!f.NativePlates||native)&&(!f.Glamaholic||saved);
    public static GlamourMatch Match(ItemFacts item,RuleSettings rules,Evidence e)
    {
        var f=rules.Glamour;var result=item.ArmoireEligible?GlamourMatch.Armoire:GlamourMatch.None;
        if(!item.Equipment)return result;
        if(e.PendingCollection.Contains(item.Id))result|=GlamourMatch.Pending;
        if(rules.CollectionKeep.Contains(item.Id))result|=GlamourMatch.Supplemental;
        if((f.NativePlates||f.Glamaholic)&&e.References.Contains(item.Id))result|=GlamourMatch.SavedOutfit;
        // Unidentified acquisition is a safety hold, not a user-selectable assumption of easy replacement.
        if(item.Source==SourceKind.Unknown)result|=GlamourMatch.Unknown;
        if((f.Categories.Count>0&&!f.Categories.Contains(item.Category))||f.ExcludedCategories.Contains(item.Category))return result;
        if(f.EorzeaCollection&&(rules.CommonGlamour.Contains(item.Id)||e.CuratedGlamour.Contains(item.Id)))result|=GlamourMatch.Common;
        if(f.LevelOne&&item.EquipLevel==1)result|=GlamourMatch.LevelOne;
        if(f.GlamourDesign&&item.DesignedForGlamour)result|=GlamourMatch.Design;
        if(f.NonmarketableBlue&&item.Rarity==3&&!item.Marketable&&item.Source is not (SourceKind.NormalDungeon or SourceKind.NormalRaid))result|=GlamourMatch.Blue;
        if(f.HigherRarity&&item.Rarity>3)result|=GlamourMatch.Higher;
        result|=item.Source switch
        {
            SourceKind.Special when f.SpecialRewards=>GlamourMatch.Special,
            SourceKind.AllianceRaid when f.AllianceRaids=>GlamourMatch.Alliance,
            SourceKind.SavageRaid when f.SavageRaids=>GlamourMatch.Savage,
            SourceKind.HighEnd when f.HighEndDuties=>GlamourMatch.HighEnd,
            SourceKind.NormalRaid when f.NormalRaids=>GlamourMatch.NormalRaid,
            SourceKind.NormalDungeon when f.NormalDungeons=>GlamourMatch.Dungeon,
            _=>GlamourMatch.None
        };
        return result;
    }
    public static IEnumerable<string> Describe(GlamourMatch matches)
    {
        foreach(var (flag,label) in Labels)if(matches.HasFlag(flag))yield return label;
    }
    private static readonly (GlamourMatch,string)[] Labels=
    [
        (GlamourMatch.Armoire,"Armoire eligible"),(GlamourMatch.Pending,"Pending collection hold"),
        (GlamourMatch.Supplemental,"Supplemental glamour collection"),(GlamourMatch.SavedOutfit,"Saved glamour outfit"),
        (GlamourMatch.Common,"Common glamour"),(GlamourMatch.LevelOne,"Equip level 1"),(GlamourMatch.Design,"Glamour design"),
        (GlamourMatch.Blue,"Nonmarketable blue"),(GlamourMatch.Higher,"Higher rarity"),(GlamourMatch.Special,"Special rewards"),
        (GlamourMatch.Alliance,"Alliance raid"),(GlamourMatch.Savage,"Savage raid"),(GlamourMatch.HighEnd,"High-end duty"),
        (GlamourMatch.NormalRaid,"Normal raid"),(GlamourMatch.Dungeon,"Normal dungeon"),(GlamourMatch.Unknown,"Unknown acquisition")
    ];
}
