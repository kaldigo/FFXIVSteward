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
public sealed record Offering(uint ItemId, bool Hq, uint UnitPrice, uint Quantity, ulong RetainerId);
public static class Pricing
{
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
        var other = relevant.Where(p => !ownRetainers.Contains(p.RetainerId)).OrderBy(p => p.UnitPrice).FirstOrDefault();
        if(other == null) return null;
        var ownCheapest = relevant.Where(p => ownRetainers.Contains(p.RetainerId)).Select(p => (uint?)p.UnitPrice).Min();
        if(ownCheapest.HasValue && ownCheapest <= other.UnitPrice) return null;
        if(ownCurrent.HasValue && ownCurrent <= other.UnitPrice) return null;
        return Undercut(other.UnitPrice, settings, floor);
    }
}
