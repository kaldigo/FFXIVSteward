using Steward.Core;
static class StarterEquipmentChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        foreach(var id in StarterEquipment.Items)
        {
            var item=new ItemFacts(id,"Starter",true,1,1,false,false,SourceKind.Ordinary,35,DesignedForGlamour:true,RepeatableStarter:true);
            var stock=new Stock(id,false,1,0,0);
            var r=new RuleSettings();
            Evidence Fresh()=>new(){ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,DresserFreeSlots=100};
            var e=Fresh();
            Decision Judge()=>Rules.Judge(item,stock,r,e,83,now);
            check(GlamourProtection.Match(item,r,e)==GlamourMatch.None,"Starter skips both level filters: "+id);
            check(Judge().Outcome==Outcome.Dispose,"Unreferenced starter follows normal rules: "+id);
            e.Dresser.Add(id);e.DresserCopyCounts[id]=1;
            check(StoragePolicy.WithoutDresserCopy(e,id,1,Judge).Outcome==Outcome.Dispose,"Unprotected starter can leave dresser: "+id);
            e.References.Add(id);
            check(StoragePolicy.WithoutDresserCopy(e,id,1,Judge).Outcome==Outcome.CollectDresser,"Saved outfit preserves last starter copy: "+id);
            e=Fresh();e.CuratedGlamour.Add(id);check(Judge().Outcome==Outcome.CollectDresser,"Common glamour preserves starter: "+id);
            e=Fresh();r.CollectionKeep.Add(id);check(Judge().Outcome==Outcome.CollectDresser,"Supplemental collection preserves starter: "+id);r.CollectionKeep.Clear();
            r.Keep.Add(id);check(Judge().Outcome==Outcome.Keep,"Keep preserves starter: "+id);r.Keep.Clear();
            e.Gearsets.Add(id);check(Judge().Outcome==Outcome.Keep,"Gearset preserves starter: "+id);
            e=Fresh();e.PendingCollection.Add(id);check(Judge().Outcome==Outcome.CollectDresser,"Pending hold preserves starter: "+id);
            e=Fresh();
            foreach(var source in new[]{SourceKind.Unknown,SourceKind.Special,SourceKind.AllianceRaid,SourceKind.SavageRaid,SourceKind.HighEnd})
                check(!StarterEquipment.ExcludeLevelFilters(item with{Source=source}),"Starter exception cannot override source: "+source);
            check(!StarterEquipment.ExcludeLevelFilters(item with{RepeatableStarter=false}),"Missing stock evidence retains level filters: "+id);
            check(!StarterEquipment.ExcludeLevelFilters(item with{Rarity=3}),"Blue protection unchanged: "+id);
        }
        check(StarterEquipment.ReviewedStock(2545,262730,0,0,false,0,100,69),"Reviewed weathered hatchet stock qualifies");
        check(!StarterEquipment.ReviewedStock(2545,999,0,0,false,0,100,69),"Unreviewed shop does not qualify");
        check(!StarterEquipment.ReviewedStock(2545,262730,1,0,false,0,100,69),"Festival does not qualify");
        check(!StarterEquipment.ReviewedStock(2545,262730,0,1,false,0,100,69),"Shop quest does not qualify");
        check(!StarterEquipment.ReviewedStock(2545,262730,0,0,true,0,100,69),"Item quest does not qualify");
        check(!StarterEquipment.ReviewedStock(2545,262730,0,0,false,1,100,69),"Achievement does not qualify");
        check(!StarterEquipment.ReviewedStock(2545,262730,0,0,false,0,0,69),"Unknown stock state does not qualify");
        check(!StarterEquipment.ReviewedStock(2545,262730,0,0,false,0,100,0),"Missing price does not qualify");
        foreach(var id in new uint[]{2996,2997,3307,3308,2220,2650,7540,2632,2637,366})
            check(!StarterEquipment.Items.Contains(id),"Cosmetics, special and legacy gear not starter exceptions: "+id);
    }
}
