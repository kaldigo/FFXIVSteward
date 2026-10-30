using FFXIVClientStructs.FFXIV.Client.Game;
using Steward.Core;

namespace Steward.Services;

public sealed unsafe partial class MarketQueue
{
    private string silverAlertFingerprint="";
    public IReadOnlyList<ReviewEntry> StrategyReviewEntries {get;private set;}=[];
    private SaleRule? MarketRule(ItemFacts item,Stock stock,ulong retainer)=>config.Silver.Owns(retainer)?
        config.Silver.Matches(retainer,stock.ItemId)?config.Silver.Rule():null:
        config.Rules.Sales.FirstOrDefault(r=>r.Matches(item,stock,retainer));
    private void SilverAlerts(SilverStrategy.Quote quote,DateTimeOffset observed)
    {
        StrategyReviewEntries=quote.Buyouts.Select(o=>new ReviewEntry(view.Character,$"Silver strategy competitor listing {o.ListingId}",o.ItemId,o.Quantity,o.Hq,"BuyoutAlert",
            $"Competitor at {o.UnitPrice:N0} gil per dye; full stack costs {(ulong)o.UnitPrice*o.Quantity:N0} gil. Manual purchase only. Verify availability in game.",
            o.UnitPrice,(ulong)o.UnitPrice*o.Quantity,view.World,o.RetainerId,"Observed in-game competitor listing",observed)).ToArray();
        var fingerprint=string.Join(";",quote.Buyouts.Select(o=>$"{o.ListingId}/{o.UnitPrice}/{o.Quantity}"));
        if(fingerprint==silverAlertFingerprint)return;
        silverAlertFingerprint=fingerprint;
        if(quote.Buyouts.Length>0)notify($"Silver dye: {quote.Buyouts.Length} cheaper competitor listing(s), {quote.Buyouts.Sum(o=>(long)o.Quantity)} dyes, total {quote.BuyoutCost:N0} gil. Strategy target: {quote.Price:N0} gil each. Manual buyout review only.");
    }
    private ListingChoice SilverChoice(Stock stock,ulong retainer,bool reprice,IEnumerable<Offering> offers,bool complete)
    {
        if(!config.Silver.Matches(retainer,stock.ItemId)||stock.Hq||marketJudge(stock,retainer,false).Outcome!=Outcome.Market||
            !MarketSnapshot(out var selected,out var listings,false)||selected!=retainer)return new(0,0,"Strategy source/protection unavailable");
        var quote=config.Silver.Price(offers,config.OwnRetainers,complete);
        if(quote==null)return new(0,0,"Complete identified competition required");
        SilverAlerts(quote,DateTimeOffset.UtcNow);
        var quantity=reprice?stock.Quantity:config.Silver.NextQuantity(listings,stock.Quantity);
        return quantity==0?new(0,0,"Target mix is satisfied, no free slot or insufficient stock"):new(quote.Price,quantity,quote.Reset?"Reset strategy price":"Undercut qualifying competitor by five gil");
    }
    private MarketPreview PreviewSilver(ulong retainer,List<Stock> listings)
    {
        StrategyReviewEntries=[];
        var mix=string.Join("; ",SilverStrategy.Mix.Select(m=>$"{listings.Count(s=>s.ItemId==SilverStrategy.ItemId&&!s.Hq&&s.Quantity==m.Quantity)}/{m.Count} stacks of {m.Quantity}"));
        var wrong=listings.Count(s=>s.ItemId!=SilverStrategy.ItemId||s.Hq||!SilverStrategy.Mix.Any(m=>m.Quantity==s.Quantity));
        var status=$"Silver strategy: {mix}. {wrong} unrelated or differently-sized listing(s). Other items are untouched; nonstandard dye stacks are repriced but never resized or withdrawn. ";
        SilverStrategy.Quote? quote;
        MarketReference reference;
        if(observedMarkets.TryGetValue(SilverStrategy.ItemId,out var observed)&&observed.Complete&&observed.World==view.World&&
            observed.At<=DateTimeOffset.UtcNow&&DateTimeOffset.UtcNow-observed.At<=TimeSpan.FromMinutes(config.Rules.MaximumPriceAgeMinutes)&&
            !view.Evidence.AllaganPrices.Any(q=>q.World==view.World&&q.ItemId==SilverStrategy.ItemId&&!q.Hq&&q.Observed>observed.At&&q.Observed<=DateTimeOffset.UtcNow))
        {
            quote=config.Silver.Price(observed.Offers,config.OwnRetainers,true);
            if(quote==null)return new(retainer,view.World,null,[],status+"Market identities are incomplete; strategy held.");
            SilverAlerts(quote,observed.At);
            reference=new(view.World,quote.Price,null,observed.At,"Silver strategy: complete observed comparison; shared live comparison before execution",true);
        }
        else
        {
            var cached=CachedRepricing.Decide(view.Evidence.AllaganPrices,view.World,SilverStrategy.ItemId,false,QualityMode.Separate,
                1,config.Pricing,config.Silver.MinimumPrice,DateTimeOffset.UtcNow,config.Rules.AllaganCacheHours,config.Silver);
            if(!cached.Known||cached.Target==null||cached.Quote==null)
                return new(retainer,view.World,null,[],status+cached.Reason+". No cache-based price proposal available.");
            var reset=cached.Quote.UnitPrice<config.Silver.MinimumPrice+config.Silver.Undercut;
            quote=new(cached.Target.Value,[],reset);
            reference=new(view.World,quote.Price,null,cached.Quote.Observed,
                "Allagan Market silver estimate; competitor identities and higher-priced alternatives require one shared comparison",true);
            if(reset)StrategyReviewEntries=[new(view.Character,"Silver strategy cached competitor",SilverStrategy.ItemId,0,false,"BuyoutAlert",
                $"Allagan Market records {cached.Quote.UnitPrice:N0} gil per dye below the strategy threshold. Provisional reset {quote.Price:N0}; higher qualifying competitors, quantities and buyout totals are unknown. Manual purchase only.",
                cached.Quote.UnitPrice,World:view.World,Retainer:retainer,PriceSource:cached.Quote.Source,PriceObserved:cached.Quote.Observed)];
        }
        List<RepriceProposal> reprices=[];List<ListingProposal> proposals=[];List<string> held=[];
        foreach(var listing in listings.Where(s=>config.Features.Repricing&&config.Silver.Matches(retainer,s.ItemId)&&!s.Hq))
        {
            var eligibility=marketJudge(listing,retainer,false);
            if(eligibility.Outcome!=Outcome.Market){held.Add("Existing dye listing held: "+eligibility.Reason);continue;}
            var before=(uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)listing.Slot);
            if(before>0&&before!=quote.Price)reprices.Add(new(listing,before,quote.Price,reference,config.Silver.Rule().Name));
        }
        foreach(var source in AvailableSaleStock().Where(s=>config.Silver.Matches(retainer,s.ItemId)))
        {
            var eligibility=marketJudge(source,retainer,false);
            if(eligibility.Outcome!=Outcome.Market)held.Add("Dye stock held: "+eligibility.Reason);
        }
        var simulated=listings.ToList();var free=new Queue<int>(Enumerable.Range(0,20).Except(listings.Select(s=>(int)s.Slot)));
        foreach(var source in AvailableSaleStock().Where(s=>config.Features.NewListings&&config.Silver.Matches(retainer,s.ItemId)&&!s.Hq&&marketJudge(s,retainer,false).Outcome==Outcome.Market))
        {
            var left=source.Quantity;
            while(free.Count>0&&config.Silver.NextQuantity(simulated,left) is uint quantity&&quantity>0)
            {
                var slot=free.Dequeue();proposals.Add(new(source,slot,quantity,quote.Price,reference,config.Silver.Rule().Name));
                simulated.Add(new(source.ItemId,false,quantity,(uint)InventoryType.RetainerMarket,(ushort)slot));left-=quantity;
            }
        }
        if(free.Count>0)held.Add("Remaining quotas require sufficient dye stock in player bags or this retainer. No other retainer is withdrawn from by this strategy.");
        return new(retainer,view.World,new(proposals,held.Distinct().ToArray(),free.Count),reprices,status+$"Target {quote.Price:N0} gil each; {(quote.Reset?"reset price":"five-gil undercut")}. {proposals.Count} proposed refill(s). Normal rotation and withdrawals disabled.");
    }
}
