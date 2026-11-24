using Steward.Core;

static class RelicEquipmentChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        foreach(var (id,family) in new (uint,string)[]{
            (45597,"Cosmic tools"),(45614,"Cosmic tools"),(45688,"Cosmic tools"),
            (29612,"Skysteel tools"),(38715,"Splendorous tools"),(10054,"Zodiac weapons"),
            (13611,"Anima weapons"),(17817,"Eureka weapons"),(30228,"Resistance weapons"),
            (39144,"Manderville weapons"),(47006,"Phantom weapons")})
        {
            check(RelicEquipment.Family(id)==family,"Known original in relic index: "+id);
            var item=new ItemFacts(id,"Original",true,4,90,false,false,SourceKind.Ordinary,2){RelicFamily=RelicEquipment.Family(id)};
            var stock=new Stock(id,false,1,0,0);
            var rules=new RuleSettings();
            var evidence=new Evidence{GearsetsReady=true,ReferencesReady=true,ArmoireKnown=true,DresserKnown=true,DresserFreeSlots=100};
            evidence.Dresser.Add(id);
            var decision=Rules.Judge(item,stock,rules,evidence,83,now);
            check(decision.Outcome==Outcome.Keep&&StoragePolicy.EntrustCandidate(decision,true),"Relic remains eligible for native entrust, held from disposal even with a dresser copy");
            check(!StoragePolicy.NeedsCollectionVisit(item,stock,rules,evidence,false),"Relics do not trigger dresser collection");
            check(!StoragePolicy.CanDiscardSurplus(item,stock,decision,evidence),"Relic is ineligible for the surplus discard fallback");
        }
        foreach(var id in new uint[]{12126,2560,2524,43264,42588,49360})
            check(RelicEquipment.Family(id)==null,"Replica, ordinary tool or non-relic artifact keeps existing rules: "+id);
    }
}
