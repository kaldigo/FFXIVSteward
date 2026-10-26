using System.Text.Json;

namespace Steward.Core;

// Historical value is deliberately separate from an identified competing listing.
public sealed record SaleStatistic(uint World,uint ItemId,bool Hq,double? Average,double? DailySales,
    DateTimeOffset Fetched,DateTimeOffset? ValueObserved,uint? MinimumListing,bool RecentSales);

public static class SaleStatistics
{
    public static SaleStatistic? Select(Evidence evidence,uint item,bool hq,QualityMode quality,uint world,DateTimeOffset now)
    {
        var values=evidence.SaleStatistics.Values.Where(s=>s.ItemId==item&&s.World==world&&s.Fetched<=now&&
            s.ValueObserved<=now&&s.Average is >0&&double.IsFinite(s.Average.Value)&&
            (quality==QualityMode.Combined||s.Hq==hq)).ToArray();
        if(values.Length==0)return null;
        var selected=values.MinBy(s=>s.Average)!;
        // Combined compares both qualities conservatively; unknown velocity remains unknown.
        return selected with{DailySales=values.All(s=>s.DailySales.HasValue)?values.Sum(s=>s.DailySales!.Value):null,
            RecentSales=values.All(s=>s.RecentSales)};
    }
    public static SaleStatistic Merge(SaleStatistic incoming,SaleStatistic? previous)=>
        incoming.Average is >0||previous==null?incoming:incoming with
        {Average=previous.Average,ValueObserved=previous.ValueObserved,RecentSales=false};

    public static IReadOnlyList<SaleStatistic> Parse(string json,uint world,IReadOnlySet<uint> requested,DateTimeOffset fetched)
    {
        using var doc=JsonDocument.Parse(json);
        var root=doc.RootElement;
        if(world==0||!root.TryGetProperty("results",out var results)||results.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("Invalid aggregate envelope");
        List<SaleStatistic> values=[];HashSet<uint> seen=[];
        foreach(var row in results.EnumerateArray())
        {
            var id=row.GetProperty("itemId").GetUInt32();
            if(!requested.Contains(id)||!seen.Add(id))throw new InvalidDataException("Unexpected or duplicate aggregate item");
            foreach(var hq in new[]{false,true})
            {
                if(!row.TryGetProperty(hq?"hq":"nq",out var data)||data.ValueKind!=JsonValueKind.Object)continue;
                double? Number(string field,string member)
                {
                    if(!data.TryGetProperty(field,out var metric)||metric.ValueKind!=JsonValueKind.Object||!metric.TryGetProperty("world",out var scope)||
                        scope.ValueKind!=JsonValueKind.Object||!scope.TryGetProperty(member,out var value)||
                        value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var number)||!double.IsFinite(number)||number<0)return null;
                    if(scope.TryGetProperty("worldId",out var scopeWorld)&&(!scopeWorld.TryGetUInt32(out var idWorld)||idWorld!=world))return null;
                    return number;
                }
                var average=Number("averageSalePrice","price");if(average is <=0 or >999999999)average=null;
                var daily=Number("dailySaleVelocity","quantity");
                var minimum=Number("minListing","price");
                // Fetched is the aggregate observation time, never a listing upload timestamp.
                values.Add(new(world,id,hq,average,daily,fetched,average.HasValue?fetched:null,
                    minimum is >=1 and <=999999999?(uint)minimum.Value:null,average.HasValue&&daily is >0));
            }
        }
        foreach(var id in requested)
            foreach(var hq in new[]{false,true})
                if(!values.Any(s=>s.ItemId==id&&s.Hq==hq))values.Add(new(world,id,hq,null,null,fetched,null,null,false));
        return values;
    }
}
