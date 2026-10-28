namespace Steward.Core;

public sealed record MarketReference(uint World,uint? Competitor,uint? OwnMinimum,DateTimeOffset Observed,string Source,bool Provisional=false,bool SummaryEstimate=false,bool EmptyMarketAverage=false);
public sealed record ListingInput(ItemFacts Item,Stock Stock,Decision Decision,SaleRule? Rule,MarketReference? Reference);
public sealed record ListingChoice(uint UnitPrice,uint Quantity,string Reason)
{public ulong Total=>(ulong)UnitPrice*Quantity;public bool Eligible=>UnitPrice>0&&Quantity>0;}
public sealed record ListingProposal(Stock Source,int MarketSlot,uint Quantity,uint UnitPrice,MarketReference Reference,string? RuleName=null,int Priority=0)
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
            ? (reference.Competitor.Value>=rule.ListingMinimumUnit ? reference.Competitor : null)
            : Pricing.ListingTarget(reference.Competitor.Value,reference.OwnMinimum,pricing,rule.ListingMinimumUnit);
        if(target==null)return new(0,0,"Undercut/own-price protection cannot meet the unit floor");
        var maximum=Math.Clamp(rule.MaximumStack,1,9999);
        if(pricing.UseMaxStackSize)maximum=Math.Min(maximum,Math.Clamp(pricing.MaximumStackSize,1,9999));
        var quantity=Math.Min(Math.Min(input.Stock.Quantity,input.Decision.MaximumRemovable??input.Stock.Quantity),(uint)maximum);
        if(quantity==0)return new(0,0,"Remaining quantity is reserved");
        if((ulong)target.Value*quantity<rule.ListingMinimumTotal)return new(0,0,"Actual split listing falls below the total-value floor");
        return new(target.Value,quantity,reference.EmptyMarketAverage?"Confirmed empty market: use Universalis average without undercutting":reference.OwnMinimum==target?"Match own lowest price without undercutting it":"Undercut competing price");
    }
    public static int Priority(ListingInput input,ListingChoice choice)=>!choice.Eligible?2:
        Math.Max(input.Rule?.Priority(input.Decision.UnitPrice,choice.Quantity)??0,input.Rule?.Priority(choice.UnitPrice,choice.Quantity)??0);
    public static ListingPlan Plan(IEnumerable<ListingInput> inputs,IReadOnlyList<int> freeSlots,PricingSettings pricing,
        uint world,int maxAgeMinutes,QualityMode quality,DateTimeOffset now)
    {
        if(freeSlots.Any(s=>s<0||s>=20)||freeSlots.Distinct().Count()!=freeSlots.Count)throw new InvalidDataException("Invalid market-slot snapshot");
        List<ListingProposal> proposals=[];List<string> held=[];
        var own=new Dictionary<(uint,bool),uint>();var seen=new HashSet<(uint,ushort)>();
        var pending=inputs.ToList();
        foreach(var original in pending)
            if(!seen.Add((original.Stock.Container,original.Stock.Slot)))throw new InvalidDataException("Conflicting source-slot snapshot");
        while(pending.Count>0&&proposals.Count<freeSlots.Count)
        {
            ListingInput WithOwn(ListingInput input)
            {
                var key=(input.Stock.ItemId,quality==QualityMode.Combined?false:input.Stock.Hq);
                if(input.Reference is {} reference&&own.TryGetValue(key,out var plannedOwn))
                    return input with{Reference=reference with{OwnMinimum=reference.OwnMinimum is uint known?Math.Min(known,plannedOwn):plannedOwn}};
                return input;
            }
            var choices=pending.Select((p,index)=>(Input:WithOwn(p),Index:index))
                .Select(p=>(p.Input,p.Index,Choice:Next(p.Input,pricing,world,maxAgeMinutes,now)))
                .OrderBy(p=>Priority(p.Input,p.Choice)).ToArray();
            foreach(var rejected in choices.Where(p=>!p.Choice.Eligible))
            {
                var source=rejected.Input.Stock;
                var reason=$"{source.ItemId} ({source.Container}:{source.Slot}) x{source.Quantity}: {rejected.Choice.Reason}";
                if(!held.Contains(reason))held.Add(reason);
            }
            var next=choices.FirstOrDefault(p=>p.Choice.Eligible);
            if(next.Input==null)break;
            var stock=next.Input.Stock;var decision=next.Input.Decision;var choice=next.Choice;
            proposals.Add(new(stock,freeSlots[proposals.Count],choice.Quantity,choice.UnitPrice,next.Input.Reference!,next.Input.Rule!.Name,Priority(next.Input,choice)));
            own[(stock.ItemId,quality==QualityMode.Combined?false:stock.Hq)]=choice.UnitPrice;
            stock=stock with{Quantity=stock.Quantity-choice.Quantity};
            if(decision.MaximumRemovable is uint remaining)decision=decision with{MaximumRemovable=remaining-choice.Quantity};
            if(stock.Quantity==0)pending.RemoveAt(next.Index);
            else pending[next.Index]=next.Input with{Stock=stock,Decision=decision};
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
