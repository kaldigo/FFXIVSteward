using FFXIVClientStructs.FFXIV.Client.Game;
using Steward.Core;

namespace Steward.Services;

public sealed unsafe partial class MarketQueue
{
    private sealed record ObservedMarket(uint World,int Request,DateTimeOffset At,List<Offering> Offers,bool Complete=false);
    private readonly Dictionary<uint,ObservedMarket> observedMarkets=[];
    private uint observationWorld;
    private DateTimeOffset observationWorldSince;
    public sealed record RepriceProposal(Stock Source,uint Before,uint After,MarketReference Reference,string? RuleName=null);
    public sealed record MarketPreview(ulong Retainer,uint World,ListingPlan? Plan,IReadOnlyList<RepriceProposal> Reprices,string Status);

    private void ObserveMarket(uint item,DateTimeOffset at,int request,List<Offering> values)
    {
        var proxy=FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyItemSearch.Instance();
        if(view.World==0||at<observationWorldSince||proxy==null||proxy->SearchItemId%1000000!=item||proxy->CurrentRequestId!=request)return;
        if(observedMarkets.TryGetValue(item,out var old)&&old.World==view.World&&old.Request==request&&at-old.At<TimeSpan.FromSeconds(20))
            values=old.Offers.Concat(values).ToList();
        observedMarkets[item]=new(view.World,request,at,values.GroupBy(o=>o.ListingId).Select(g=>g.First()).ToList());
        if(observedMarkets.Count>128)observedMarkets.Remove(observedMarkets.MinBy(p=>p.Value.At).Key);
    }
    private void UpdateObservedMarkets()
    {
        foreach(var (id,snapshot) in observedMarkets.ToArray())
        {
            if(snapshot.Complete||snapshot.World!=view.World)continue;
            var result=(snapshot.At,snapshot.Request,snapshot.Offers);
            if(!ComparisonReady(id,snapshot.At,result))continue;
            observedMarkets[id]=snapshot with{Complete=true};RecordGameQuotes(id,result);
        }
    }
    private bool MarketSnapshot(out ulong retainer,out List<Stock> listings,bool requireWindow=true)
    {
        retainer=0;listings=[];
        if((requireWindow&&ui.Get("RetainerSellList")==null)||!InventoryView.Loaded((uint)InventoryType.RetainerMarket))return false;
        var rm=RetainerManager.Instance();if(rm==null)return false;
        retainer=rm->LastSelectedRetainerId;
        if(retainer==0||!view.Retainers.Contains(retainer)||view.World==0)return false;
        InventoryView.Read(InventoryType.RetainerMarket,listings);
        return listings.Count<=20&&listings.All(s=>s.Slot<20)&&listings.Select(s=>s.Slot).Distinct().Count()==listings.Count;
    }
    public IReadOnlyList<Stock> PriceCandidates()
    {
        return MarketSnapshot(out _,out var listings)?listings.Concat(AvailableSaleStock()).Distinct().ToArray():[];
    }
    private List<Stock> AvailableSaleStock()
    {
        var result=view.Items.Where(s=>s.Container<4).ToList();
        if(InventoryView.RetainerTypes().All(t=>InventoryView.Loaded((uint)t)))
            foreach(var type in InventoryView.RetainerTypes())InventoryView.Read(type,result);
        return result.Distinct().ToList();
    }
    private uint? OwnMinimum(uint id,bool hq)
    {
        if(!MarketSnapshot(out _,out var listings,false))return null;
        return listings.Where(s=>s.ItemId==id&&(config.Rules.Quality==QualityMode.Combined||s.Hq==hq))
            .Select(s=>(uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)s.Slot)).Where(p=>p>0).Select(p=>(uint?)p).Min();
    }
    private MarketReference ReferenceFromOffers(uint id,bool hq,DateTimeOffset at,IEnumerable<Offering> values)
    {
        var relevant=values.Where(o=>o.ItemId==id&&(config.Rules.Quality==QualityMode.Combined||o.Hq==hq)).ToArray();
        var own=OwnMinimum(id,hq);
        var observedOwn=relevant.Where(o=>config.OwnRetainers.Contains(o.RetainerId)).Select(o=>(uint?)o.UnitPrice).Min();
        if(observedOwn.HasValue)own=own.HasValue?Math.Min(own.Value,observedOwn.Value):observedOwn;
        var competitor=relevant.Any(o=>o.RetainerId==0)?null:relevant.Where(o=>!config.OwnRetainers.Contains(o.RetainerId)).Select(o=>(uint?)o.UnitPrice).Min();
        return new(view.World,competitor,own,at,"Complete in-game comparison");
    }
    private MarketReference? PreviewReference(uint id,bool hq)
    {
        if(observedMarkets.TryGetValue(id,out var snapshot)&&snapshot.Complete&&snapshot.World==view.World&&
            snapshot.At<=DateTimeOffset.UtcNow&&DateTimeOffset.UtcNow-snapshot.At<=TimeSpan.FromMinutes(config.Rules.MaximumPriceAgeMinutes))
            return ReferenceFromOffers(id,hq,snapshot.At,snapshot.Offers) with{Provisional=true};
        var now=DateTimeOffset.UtcNow;
        var quote=view.Evidence.Prices.Values.Where(p=>p.ItemId==id&&p.World==view.World&&!p.Own&&p.UnitPrice>0&&
            (config.Rules.Quality==QualityMode.Combined||p.Hq==hq)&&p.Observed<=now&&now-p.Observed<=TimeSpan.FromMinutes(config.Rules.MaximumPriceAgeMinutes))
            .OrderBy(p=>p.UnitPrice).FirstOrDefault();
        var own=OwnMinimum(id,hq);
        foreach(var price in view.Evidence.PublicOwnPrices.Values.Where(p=>p.ItemId==id&&p.World==view.World&&
            (config.Rules.Quality==QualityMode.Combined||p.Hq==hq)&&p.Observed<=now&&now-p.Observed<=TimeSpan.FromMinutes(config.Rules.MaximumPriceAgeMinutes)))
            own=own.HasValue?Math.Min(own.Value,price.UnitPrice):price.UnitPrice;
        if(quote!=null)return new(view.World,quote.UnitPrice,own,quote.Observed,quote.Source+" cache; own prices outside the current retainer may be incomplete",true);
        var summary=SaleStatistics.Select(view.Evidence,id,hq,config.Rules.Quality,view.World,now);
        return summary==null?null:new(view.World,summary.MinimumListing??(uint)Math.Floor(summary.Average!.Value),own,
            summary.ValueObserved!.Value,"Universalis summary estimate; seller identities unavailable, actual posting requires native comparison",true,true);
    }
    private ListingInput? SaleInput(Stock stock,bool live,MarketReference? reference=null)
    {
        if(!InventoryView.CanListStock(stock))return null;
        var item=view.Facts(stock.ItemId);if(item==null)return null;
        var decision=(live?judge:previewJudge)(stock);
        var rule=config.Rules.Sales.FirstOrDefault(r=>r.Matches(item,stock));
        return new(item,stock,decision,rule,reference??PreviewReference(stock.ItemId,stock.Hq));
    }
    public MarketPreview PreviewMarket()
    {
        if(!MarketSnapshot(out var retainer,out var listings))
            return new(0,view.World,null,[],"Open your retainer's selling list to load its actual free slots. Log-only does not open game menus or request in-game searches. Universalis may fetch price estimates in the background.");
        if(!marketPreferencesReady())return new(retainer,view.World,null,[],"Market preferences unavailable; listing proposals held.");
        if(!view.Evidence.ReferencesReady||!view.Evidence.GearsetsReady)
            return new(retainer,view.World,null,[],"Protection references unavailable; listing proposals held. Load the native glamour plate editor and Glamaholic references; gearset evidence must also be ready.");
        var free=Enumerable.Range(0,20).Except(listings.Select(s=>(int)s.Slot)).ToArray();
        var inputs=AvailableSaleStock().Select(s=>SaleInput(s,false)).OfType<ListingInput>()
            .Where(i=>ListingPlanner.CanConsider(i.Item,i.Stock,i.Decision,i.Rule)).ToArray();
        var plan=ListingPlanner.Plan(inputs,free,config.Pricing,view.World,config.Rules.MaximumPriceAgeMinutes,config.Rules.Quality,DateTimeOffset.UtcNow);
        List<RepriceProposal> reprices=[];
        foreach(var listing in listings)
        {
            var decision=previewJudge(listing);if(decision.Outcome is not (Outcome.Market or Outcome.Dispose))continue;
            var reference=PreviewReference(listing.ItemId,listing.Hq);if(reference?.Competitor is not uint competitor)continue;
            var before=(uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)listing.Slot);
            if(before==0||before<=competitor||reference.OwnMinimum is uint own&&own<=competitor)continue;
            var target=Pricing.Undercut(competitor,config.Pricing,decision.Rule?.MinimumUnit??1);
            if(target.HasValue&&MarketSubmission.CanCommit(listing,listing,decision,true,target.Value,listing.Quantity,(int)target.Value,(int)listing.Quantity,9999))
                reprices.Add(new(listing,before,target.Value,reference,decision.Rule?.Name));
        }
        return new(retainer,view.World,plan,reprices,$"{listings.Count}/20 slots occupied; {plan.Listings.Count} proposed new listings; {plan.FreeSlotsRemaining} slots remain. Estimates use cached summaries or manually observed prices; execution always rechecks the game market.{(config.Rules.Sales.Any(r=>r.Enabled)?"":" Add an enabled auto-sell rule in Rules to propose new listings.")}");
    }
    private void EnqueueListingPump(ulong retainer,bool coordinated,ListingPass pass)
    {
        Queue.Add(new("Fill free market slots",()=>
        {
            if(!config.ExecutionEnabled||config.ExecutionReviewRequired)return StepResult.Failed;
            refreshProtection();
            if(!marketPreferencesReady()||!MarketSnapshot(out var selected,out var listings)||selected!=retainer)return StepResult.Failed;
            if(listings.Count>=20){history("Retainer market slots are full.");return StepResult.Completed;}
            var candidates=AvailableSaleStock().Where(s=>SaleInput(s,true) is {} input&&ListingPlanner.CanConsider(input.Item,s,input.Decision,input.Rule));
            var next=pass.Next(candidates,listings.Count);
            if(next==null){history($"Listing pass finished: {20-listings.Count} market slot(s) remain; no further eligible source stacks.");return StepResult.Completed;}
            Enqueue(next,retainer,false,coordinated,(source,success)=>
            {if(!success)pass.Rejected(source);if(config.ExecutionEnabled&&!config.ExecutionReviewRequired)EnqueueListingPump(retainer,coordinated,pass);});
            return StepResult.Completed;
        }));
    }
}
