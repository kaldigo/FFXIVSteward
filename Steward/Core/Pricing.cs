namespace Steward.Core;

public sealed class PricingSettings
{
    public int UndercutPrice { get; set; } = 1;
    public bool UndercutUsePercent { get; set; }
    public int UndercutPercent { get; set; } = 1;
    public bool EnablePriceRounding { get; set; }
    public int PriceRoundingMultiple { get; set; } = 5;
    public bool UseMaxStackSize { get; set; }
    public int MaximumStackSize { get; set; } = 99;
}
public sealed record Offering(uint ItemId, bool Hq, uint UnitPrice, uint Quantity, ulong RetainerId, ulong ListingId = 0);
public static class Pricing
{
    public sealed record Competition(uint? Price,int IgnoredCheapListings=0);
    // Call with a complete native comparison, already scoped to item and quality.
    // Count distinct listing rows, never units in a stack or our own listings.
    public static Competition SelectCompetition(IEnumerable<Offering> scoped,IReadOnlySet<ulong> ownRetainers)
    {
        var rows=scoped.Where(p=>p.UnitPrice>0).ToArray();
        if(rows.Any(p=>p.RetainerId==0))return new(null);
        var competitors=rows.Where(p=>!ownRetainers.Contains(p.RetainerId)).OrderBy(p=>p.UnitPrice).ToArray();
        if(competitors.Length==0)return new(null);
        var cheapest=competitors[0].UnitPrice;
        if(competitors.Any(p=>p.ListingId==0||p.Quantity==0)||competitors.Select(p=>p.ListingId).Distinct().Count()!=competitors.Length)
            return new(cheapest);
        for(var skipped=1;skipped<=3&&competitors.Length>=skipped+10;skipped++)
        {
            var baseline=competitors[skipped].UnitPrice;
            if((ulong)competitors[skipped-1].UnitPrice*4>baseline)continue;
            if((ulong)competitors[skipped+9].UnitPrice>(ulong)baseline*2)continue;
            return new(baseline,skipped);
        }
        return new(cheapest);
    }
    public static uint? ListingTarget(uint competitor,uint? ownMinimum,PricingSettings settings,uint floor)
    {
        if(competitor==0)return null;
        if(ownMinimum is uint own && own<=competitor)
            return own>=Math.Max(1,floor)&&own<=999999999?own:null;
        return Undercut(competitor,settings,floor);
    }
    public static uint? Undercut(uint competitor, PricingSettings settings, uint floor)
    {
        // Match Market Buddy's percent truncation and next-lower-multiple rounding.
        var price = settings.UndercutUsePercent ? (long)((float)competitor*(1f-settings.UndercutPercent/100f)) : (long)competitor-settings.UndercutPrice;
        if(settings.EnablePriceRounding)
        {
            var multiple = Math.Max(1, settings.PriceRoundingMultiple);
            price -= price % multiple;
            if(price >= competitor) price -= multiple;
        }
        if(price < Math.Max(1, floor) || price > 999999999) return null;
        return (uint)price;
    }

    public static uint? Target(uint item, bool hq, uint? ownCurrent, IEnumerable<Offering> offerings,
        IReadOnlySet<ulong> ownRetainers, QualityMode quality, PricingSettings settings, uint floor)
    {
        var relevant = offerings.Where(p => p.ItemId == item && p.UnitPrice > 0 &&
            (quality == QualityMode.Combined || p.Hq == hq)).ToArray();
        if(relevant.Any(p=>p.RetainerId==0))return null;
        var other = SelectCompetition(relevant,ownRetainers).Price;
        if(other == null) return null;
        var ownCheapest = relevant.Where(p => ownRetainers.Contains(p.RetainerId)).Select(p => (uint?)p.UnitPrice).Min();
        if(ownCheapest.HasValue && ownCheapest <= other.Value) return null;
        if(ownCurrent.HasValue && ownCurrent <= other.Value) return null;
        return Undercut(other.Value, settings, floor);
    }
}
