namespace Steward.Core;

public sealed record MarketReference(uint World,uint? Competitor,uint? OwnMinimum,DateTimeOffset Observed,string Source,bool Provisional=false,bool SummaryEstimate=false,bool EmptyMarketAverage=false);
public sealed record ListingInput(ItemFacts Item,Stock Stock,Decision Decision,SaleRule? Rule,MarketReference? Reference);
public sealed record ListingChoice(uint UnitPrice,uint Quantity,string Reason)
{public ulong Total=>(ulong)UnitPrice*Quantity;public bool Eligible=>UnitPrice>0&&Quantity>0;}
public sealed record ListingProposal(Stock Source,int MarketSlot,uint Quantity,uint UnitPrice,MarketReference Reference,string? RuleName=null)
{public ulong Total=>(ulong)UnitPrice*Quantity;}
public sealed record ListingPlan(IReadOnlyList<ListingProposal> Listings,IReadOnlyList<string> Held,int FreeSlotsRemaining);

public static class ListingPlanner
{
    public static bool CanConsider(ItemFacts item,Stock stock,Decision decision,SaleRule? rule) =>
        !decision.SaleRejected && item.Id==stock.ItemId && stock.Quantity>0 && item.Marketable && rule!=null && rule.Matches(item,stock) &&
        (decision.Outcome is Outcome.Market or Outcome.Dispose ||
            decision is {Outcome:Outcome.Wait,Reason:"No fresh competing price; disposal is blocked"});
    public static ListingChoice Next(ListingInput input,PricingSettings pricing,uint world,int maxAgeMinutes,DateTimeOffset now)
    {
        if(!CanConsider(input.Item,input.Stock,input.Decision,input.Rule))return new(0,0,"Not eligible under protection and sale rules");
        var reference=input.Reference;var rule=input.Rule!;
        if(world==0||reference==null||reference.World!=world||reference.Competitor is null or 0||reference.Observed>now||
            !(reference.Provisional&&reference.SummaryEstimate)&&now-reference.Observed>TimeSpan.FromMinutes(Math.Clamp(maxAgeMinutes,1,1440)))return new(0,0,"No fresh competing price on the current world");
        var target=reference.EmptyMarketAverage
            ? (reference.Competitor.Value>=rule.MinimumUnit ? reference.Competitor : null)
            : Pricing.ListingTarget(reference.Competitor.Value,reference.OwnMinimum,pricing,rule.MinimumUnit);
        if(target==null)return new(0,0,"Undercut/own-price protection cannot meet the unit floor");
        var maximum=Math.Clamp(rule.MaximumStack,1,9999);
        if(pricing.UseMaxStackSize)maximum=Math.Min(maximum,Math.Clamp(pricing.MaximumStackSize,1,9999));
        var quantity=Math.Min(Math.Min(input.Stock.Quantity,input.Decision.MaximumRemovable??input.Stock.Quantity),(uint)maximum);
        if(quantity==0)return new(0,0,"Remaining quantity is reserved");
        if((ulong)target.Value*quantity<rule.MinimumTotal)return new(0,0,"Actual split listing falls below the total-value floor");
        return new(target.Value,quantity,reference.EmptyMarketAverage?"Confirmed empty market: use Universalis average without undercutting":reference.OwnMinimum==target?"Match own lowest price without undercutting it":"Undercut competing price");
    }
    public static ListingPlan Plan(IEnumerable<ListingInput> inputs,IReadOnlyList<int> freeSlots,PricingSettings pricing,
        uint world,int maxAgeMinutes,QualityMode quality,DateTimeOffset now)
    {
        if(freeSlots.Any(s=>s<0||s>=20)||freeSlots.Distinct().Count()!=freeSlots.Count)throw new InvalidDataException("Invalid market-slot snapshot");
        List<ListingProposal> proposals=[];List<string> held=[];
        var own=new Dictionary<(uint,bool),uint>();var seen=new HashSet<(uint,ushort)>();
        foreach(var original in inputs)
        {
            if(proposals.Count==freeSlots.Count)break;
            if(!seen.Add((original.Stock.Container,original.Stock.Slot)))throw new InvalidDataException("Conflicting source-slot snapshot");
            var stock=original.Stock;var decision=original.Decision;var key=(stock.ItemId,quality==QualityMode.Combined?false:stock.Hq);
            while(stock.Quantity>0&&proposals.Count<freeSlots.Count)
            {
                var reference=original.Reference;
                if(reference!=null&&own.TryGetValue(key,out var plannedOwn))
                    reference=reference with{OwnMinimum=reference.OwnMinimum is uint known?Math.Min(known,plannedOwn):plannedOwn};
                var input=original with{Stock=stock,Reference=reference,Decision=decision};
                var choice=Next(input,pricing,world,maxAgeMinutes,now);
                if(!choice.Eligible){held.Add($"{stock.ItemId} ({stock.Container}:{stock.Slot}) x{stock.Quantity}: {choice.Reason}");break;}
                proposals.Add(new(stock,freeSlots[proposals.Count],choice.Quantity,choice.UnitPrice,reference!,original.Rule!.Name));
                own[key]=choice.UnitPrice;stock=stock with{Quantity=stock.Quantity-choice.Quantity};
                if(decision.MaximumRemovable is uint remaining)decision=decision with{MaximumRemovable=remaining-choice.Quantity};
            }
        }
        return new(proposals,held,freeSlots.Count-proposals.Count);
    }
}

public sealed class ListingPass
{
    public bool RotationUsed { get; set; }
    private readonly HashSet<Stock> skipped=[];
    public Stock? Next(IEnumerable<Stock> candidates,int occupied)=>occupied>=20?null:candidates.FirstOrDefault(s=>!skipped.Contains(s));
    public void Rejected(Stock stock)=>skipped.Add(stock);
}
