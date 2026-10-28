using Steward.Core;

internal static class LowPriorityChecks
{
    public static void Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;
        var rule=new SaleRule{MinimumUnit=1000,MinimumTotal=5000,MaximumStack=30,AllowLowPriority=true,LowPriorityMinimumUnit=500,LowPriorityMinimumTotal=2500};
        var pricing=new PricingSettings{UndercutPrice=5};
        ListingInput Input(uint id,uint qty,uint value,ushort slot)=>new(
            new(id,"Material",false,1,1,true,false,SourceKind.Ordinary,45),new(id,false,qty,0,slot),
            new(Outcome.Market,"sale",rule,value),rule,new(83,value+5,null,now,"test"));
        ListingPlan Plan(params ListingInput[] inputs)=>ListingPlanner.Plan(inputs,[0,1,2],pricing,83,30,QualityMode.Combined,now);
        var low=Input(1,10,500,0);var high=Input(2,10,1000,1);
        var plan=Plan(low,high);
        check(plan.Listings.Select(x=>x.Source.ItemId).SequenceEqual(new uint[]{2,1}),"Normal priority wins even when low stock is first");
        check(plan.Listings.Select(x=>x.Priority).SequenceEqual(new[]{0,1}),"Preview labels both priority tiers");
        check(ListingPlanner.Next(low,pricing,83,30,now).Eligible,"500 each and 2500 stack material tier is eligible");
        check(!ListingPlanner.Next(Input(3,4,500,2),pricing,83,30,now).Eligible,"Low tier still requires minimum stack total");
        check(!ListingPlanner.Next(Input(3,30,499,2),pricing,83,30,now).Eligible,"Below low-tier unit floor is held");
        plan=Plan(Input(1,33,1000,0),high);
        check(plan.Listings.Select(x=>x.Source.ItemId).SequenceEqual(new uint[]{1,2,1}),"Small low-priority remainder waits behind other normal stock");
        check(MarketSubmission.CanCommit(low.Stock,low.Stock,low.Decision,false,500,10,500,10,30),"Final submit accepts valid low-tier floor");
        check(!MarketSubmission.CanCommit(low.Stock,low.Stock,low.Decision,false,499,10,499,10,30),"Final submit never breaches low floor");
        check(ListingPolicy.ReturnReason(low.Decision,rule,low.Stock,500,pricing)==null,"Valid low-tier listing is not withdrawn for missing normal floor");
        rule.AllowLowPriority=false;
        check(!ListingPlanner.Next(low,pricing,83,30,now).Eligible,"Disabled lower tier preserves original thresholds");
        rule.AllowLowPriority=true;
        var evidence=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,StatisticsRequired=true};
        var settings=new RuleSettings{Sales=[rule]};
        evidence.SaleStatistics[(1,false)]=new(83,1,false,500,1,now,now,500,true,false,28);
        var decision=Rules.Judge(low.Item,low.Stock,settings,evidence,83,now);
        check(decision.Outcome==Outcome.Market,"Lower-tier item is protected for sale rather than disposal");
        settings.NonEquipmentDisposalCategories.Add(45);
        check(Rules.Judge(low.Item,low.Stock,settings,evidence,83,now).Outcome==Outcome.Market,"Sale tier beats explicit material disposal permission");
        rule.MinimumDailySales=1d/14;
        evidence.SaleStatistics[(1,false)]=evidence.SaleStatistics[(1,false)] with{DailySales=.01};
        check(Rules.Judge(low.Item,low.Stock,settings,evidence,83,now).Outcome==Outcome.Dispose,"Lower tier does not bypass activity threshold");
        evidence.SaleStatistics[(1,false)]=evidence.SaleStatistics[(1,false)] with{DailySales=null};
        check(Rules.Judge(low.Item,low.Stock,settings,evidence,83,now).Outcome==Outcome.Wait,"Unknown activity remains a hold");
        evidence.SaleStatistics[(1,false)]=evidence.SaleStatistics[(1,false)] with{DailySales=1};
        settings.Keep.Add(1);
        check(Rules.Judge(low.Item,low.Stock,settings,evidence,83,now).Outcome==Outcome.Keep,"Explicit keep wins over lower-priority sale");
        settings.Keep.Clear();
        rule.MinimumUnit=rule.MinimumTotal=50000;rule.MaximumStack=1;
        rule.LowPriorityMinimumUnit=rule.LowPriorityMinimumTotal=20000;
        var general=Input(4,1,20000,3);
        check(ListingPlanner.Next(general,pricing,83,30,now).Eligible&&ListingPlanner.Priority(general,ListingPlanner.Next(general,pricing,83,30,now))==1,"General 20k tier is valid and low priority");
        var restored=System.Text.Json.JsonSerializer.Deserialize<SaleRule>(System.Text.Json.JsonSerializer.Serialize(rule))!;
        check(restored.AllowLowPriority&&restored.ListingMinimumUnit==20000&&restored.MinimumUnit==50000,"Tier settings survive restart without replacing primary floors");
    }
}
