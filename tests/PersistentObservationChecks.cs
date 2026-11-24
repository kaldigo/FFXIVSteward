using System.Text.Json;
using Steward.Core;

static class PersistentObservationChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        var item=new ItemFacts(42,"Protected glamour",true,3,60,false,false,SourceKind.AllianceRaid,1);
        var stock=new Stock(42,false,1,10000,0);
        var evidence=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,Dresser=[42],DresserCopyCounts=new(){[42]=1}};
        using(var visits=new InventoryMemory())
        {
            visits.ObserveRetainer(100,101,[],onlyIfMissing:true);
            check(visits.ObservedBags(100,101)==null,"Empty third-party preview cannot suppress a first visit");
            visits.ObserveRetainer(100,101,[]);
            check(visits.ObservedBags(100,101)?.Count==0&&visits.ObservedBags(200,101)==null,"Verified empty is specific to character and retainer");
        }
        var rules=new RuleSettings();
        var directory=Path.Combine(Path.GetTempPath(),"Steward-observations-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"inventory.json");
        try
        {
            using(var memory=new InventoryMemory(path))
            {memory.ObserveCollection(100,evidence);memory.ObserveRetainer(100,101,[stock]);memory.Flush();}
            using(var memory=new InventoryMemory(path))
            {
                evidence.InvalidateLive();evidence.ReferencesReady=evidence.GearsetsReady=true;memory.Apply(100,evidence);
                check(memory.Stocks(100).Single().Stock==stock,"Retainer inventories survive restart without expiry");
                check(evidence.CachedDresser.Contains(42)&&!evidence.DresserKnown&&!evidence.ArmoireKnown,"Restored collection cache never asserts live readiness");
                check(Rules.Judge(item,stock,rules,evidence,83,now).Outcome==Outcome.Keep,"Cached dresser copy cannot release protected surplus for disposal");
                check(!StoragePolicy.CollectionConfirmed(item,evidence),"Cache cannot release pending collection hold");
                check(CollectionCopyEvidence.Capture(item,rules,evidence,true,false,now).CachedDresserCopy==true,"Logs expose cached copy separately");
                memory.Apply(200,evidence);check(evidence.CachedDresser.Count==0&&!evidence.CachedDresserKnown&&!memory.Stocks(200).Any(),"Character changes cannot inherit cached ownership or inventories");
                memory.Apply(100,evidence);evidence.DresserKnown=evidence.ArmoireKnown=true;evidence.Dresser=[];evidence.DresserCopyCounts=[];
                memory.ObserveCollection(100,evidence);
                check(!evidence.CachedDresser.Contains(42)&&Rules.Judge(item,stock,rules,evidence,83,now).Outcome==Outcome.CollectDresser,"Observed empty dresser replaces cache and protects missing last copy");
                evidence.Armoire=[42];memory.ObserveCollection(100,evidence);evidence.InvalidateLive();evidence.ReferencesReady=evidence.GearsetsReady=true;
                check(Rules.Judge(item with{ArmoireEligible=true},stock,rules,evidence,83,now).Outcome==Outcome.Keep,"Cached armoire copy cannot authorize disposal either");
                memory.ObserveRetainer(100,101,[],onlyIfMissing:true);check(memory.Stocks(100).Any(),"Imported observations cannot overwrite a saved native snapshot");
                memory.ObserveRetainer(100,101,[]);check(!memory.Stocks(100).Any(),"Complete native empty retainer read removes previous stocks");
            }
            using(var memory=new InventoryMemory(path))
            {memory.Apply(100,evidence);check(!evidence.CachedDresser.Contains(42)&&evidence.CachedArmoire.Contains(42)&&!memory.Stocks(100).Any(),"Observed collection changes and empty retainers persist after second restart");}
            File.WriteAllText(path,"{broken");using(var memory=new InventoryMemory(path))
            {memory.Apply(100,evidence);check(evidence.CachedDresser.Count==0&&memory.Status.Contains("unavailable"),"Corrupt optional cache is discarded without granting permissions");}
        }
        finally{Directory.Delete(directory,true);}
        string Summary(int count,string nq="1000",string velocity="0.1")=>"{\"worldID\":83,\"items\":{\"42\":{\"worldID\":83,\"itemID\":42,\"recentHistoryCount\":"+count+",\"averagePriceNQ\":"+nq+",\"nqSaleVelocity\":"+velocity+"}}}";
        var full=SaleStatistics.ParseCurrent(Summary(10),83,new HashSet<uint>{42,43},now);
        check(full.Count==4&&full.Single(s=>s.ItemId==42&&!s.Hq).RecentSales,"Compact summaries return valid world/quality data and explicit unknown missing IDs");
        check(full.Where(s=>s.ItemId==43).All(s=>s.Average==null),"Missing items cannot become zero-value disposal evidence");
        const string noSalesRow="""{"itemID":42,"worldID":83,"hasData":true,"recentHistoryCount":0,"averagePriceNQ":0,"averagePriceHQ":0,"nqSaleVelocity":0,"hqSaleVelocity":0}""";
        var noSales=SaleStatistics.ParseCurrent(noSalesRow,83,new HashSet<uint>{42},now);
        check(noSales.All(s=>s.NoSalesObserved&&s.Average==null),"A complete quiet window proves zero sales without inventing a price");
        const string onlyHq="""{"itemID":42,"worldID":83,"hasData":true,"recentHistoryCount":1,"averagePriceNQ":0,"averagePriceHQ":1000,"nqSaleVelocity":0,"hqSaleVelocity":0.035714}""";
        var mixed=SaleStatistics.ParseCurrent(onlyHq,83,new HashSet<uint>{42},now);
        check(mixed.Single(s=>!s.Hq).NoSalesObserved&&mixed.Single(s=>s.Hq).RecentSales,
            "Sales in one quality do not prove sales in the other");
        foreach(var response in new[]{
            noSalesRow.Replace("true","false"),
            noSalesRow.Replace("recentHistoryCount\":0","recentHistoryCount\":500"),
            noSalesRow.Replace("recentHistoryCount","missing"),
            noSalesRow.Replace("nqSaleVelocity","missing"),
            noSalesRow.Replace("recentHistoryCount\":0","recentHistoryCount\":1"),
            noSalesRow[..^1]+",\"unresolvedItems\":[42]}"})
            check(!SaleStatistics.ParseCurrent(response,83,new HashSet<uint>{42},now).First().NoSalesObserved,
                "Unresolved, inconsistent, partial and nonempty responses cannot prove zero sales");
        var noSalesEvidence=new Evidence{ReferencesReady=true,GearsetsReady=true,StatisticsRequired=true};
        foreach(var row in noSales)
            noSalesEvidence.SaleStatistics[(42,row.Hq)]=SaleStatistics.Merge(row,row with{Average=10000,ValueObserved=now.AddDays(-3),NoSalesObserved=false});
        var quietItem=item with{Rarity=1,Source=SourceKind.Ordinary,Marketable=true};
        var quietRules=new RuleSettings{Quality=QualityMode.Combined,Sales=[new(){MinimumUnit=1,MinimumTotal=1,MinimumDailySales=0.1}]};
        Decision JudgeQuiet()=>Rules.Judge(quietItem,stock,quietRules,noSalesEvidence,83,now);
        check(JudgeQuiet() is {Outcome:Outcome.Dispose,SaleRejected:true},"Verified no sales rejects the sale rule and allows ordinary disposal despite a retained old average");
        quietRules.Keep.Add(42);
        check(JudgeQuiet().Outcome==Outcome.Keep,"Verified no sales cannot override explicit keep");
        quietRules.Keep.Clear();
        check(Rules.Judge(quietItem with{Source=SourceKind.Unknown},stock,quietRules,noSalesEvidence,83,now).Outcome!=Outcome.Dispose,
            "Verified no sales cannot override uncertain acquisition");
        check(Rules.Judge(quietItem with{Equipment=false},stock,quietRules,noSalesEvidence,83,now).Outcome==Outcome.Keep,
            "Verified no sales still requires non-equipment permission");
        check(SaleStatistics.NoRecentSales(noSalesEvidence,42,false,QualityMode.Combined,83,now.AddDays(8),2)==null,
            "Stale no-sales evidence cannot authorize disposal");
        noSalesEvidence.SaleStatistics.Remove((42,true));
        check(SaleStatistics.NoRecentSales(noSalesEvidence,42,false,QualityMode.Combined,83,now,2)==null&&
            SaleStatistics.NoRecentSales(noSalesEvidence,42,false,QualityMode.Separate,83,now,2)!=null,
            "Combined requires both quality rows; separate uses the item's quality");
        var truncated=SaleStatistics.ParseCurrent(Summary(500),83,new HashSet<uint>{42},now).First();
        check(truncated.Average==1000&&truncated.DailySales==null&&!truncated.RecentSales&&truncated.HistoryTruncated,"Capped history remains a price estimate but cannot authorize disposal or velocity thresholds");
        evidence=new(){ReferencesReady=true,GearsetsReady=true,StatisticsRequired=true,SaleStatistics=new(){[(42,false)]=truncated}};
        var ordinary=item with{Rarity=1,Source=SourceKind.NormalDungeon,Marketable=true};
        check(Rules.Judge(ordinary,stock,rules,evidence,83,now).Outcome==Outcome.Wait,"Truncated low-value history cannot dispose ordinary gear");
        rules.Sales=[new(){MinimumUnit=1,MinimumTotal=1}];check(Rules.Judge(ordinary,stock,rules,evidence,83,now).Outcome==Outcome.Market,"Price estimate can propose a sale when older estimates are permitted");
        rules.Sales[0].MinimumDailySales=.01;check(Rules.Judge(ordinary,stock,rules,evidence,83,now).Outcome==Outcome.Wait,"Truncated velocity cannot pass enabled activity threshold");
        foreach(var bad in new[]{"0","-1","null","\"bad\""})check(SaleStatistics.ParseCurrent(Summary(1,bad),83,new HashSet<uint>{42},now).First().Average==null,"Invalid average isolated: "+bad);
        var wrong=false;try{SaleStatistics.ParseCurrent(Summary(1),84,new HashSet<uint>{42},now);}catch(InvalidDataException){wrong=true;}check(wrong,"Wrong-world summary is rejected");
        var single="{\"worldID\":83,\"itemID\":42,\"recentHistoryCount\":1,\"averagePriceNQ\":1000,\"nqSaleVelocity\":0.1}";
        check(SaleStatistics.ParseCurrent(single,83,new HashSet<uint>{42},now).First().Average==1000,"Single-item retry schema supported");
    }
}
