using System.Globalization;
using System.Text.Json;

namespace Steward.Core;

public sealed record PublicMarketListing(bool Hq,uint UnitPrice,uint Quantity,ulong? Retainer,bool Mannequin);
public sealed record PublicMarketHold(uint ItemId,string Reason);
public sealed record PublicMarketBatch(IReadOnlyList<PublicMarketItem> Items,IReadOnlyList<PublicMarketHold> Held);
public sealed record PublicMarketItem(uint ItemId,uint World,DateTimeOffset Uploaded,IReadOnlyList<PublicMarketListing> Listings)
{
    public IReadOnlyList<Quote> OwnQuotes(IReadOnlySet<ulong> own,DateTimeOffset now,int maximumAgeMinutes) =>
        Uploaded>now||now-Uploaded>TimeSpan.FromMinutes(Math.Clamp(maximumAgeMinutes,1,1440))?[]:
        Listings.Where(l=>!l.Mannequin&&l.Retainer is ulong id&&own.Contains(id)).GroupBy(l=>l.Hq)
            .Select(g=>new Quote(ItemId,g.Key,World,g.Min(l=>l.UnitPrice),Uploaded,"Universalis own listings (preview only)",true)).ToArray();
    public IReadOnlyList<Quote> Quotes(IReadOnlySet<ulong> own,DateTimeOffset now,int maximumAgeMinutes)
    {
        if(own.Count==0||Uploaded>now||now-Uploaded>TimeSpan.FromMinutes(Math.Clamp(maximumAgeMinutes,1,1440)))return [];
        List<Quote> result=[];
        foreach(var group in Listings.Where(l=>!l.Mannequin).GroupBy(l=>l.Hq))
        {
            // Unidentified sellers cannot be assumed to compete with this account.
            if(group.Any(l=>l.Retainer==null))continue;
            var competitors=group.Where(l=>!own.Contains(l.Retainer!.Value)).ToArray();
            if(competitors.Length!=0)result.Add(new(ItemId,group.Key,World,competitors.Min(l=>l.UnitPrice),Uploaded,"Universalis current listings (cached upload)"));
        }
        return result;
    }
}

public static class UniversalisData
{
    public static IReadOnlyList<PublicMarketItem> Parse(string json,uint world,IReadOnlySet<uint> requested)
    {
        var batch=ParseBatch(json,world,requested);
        if(batch.Held.Count!=0)throw new InvalidDataException(string.Join("; ",batch.Held.Select(h=>$"Item {h.ItemId}: {h.Reason}")));
        return batch.Items;
    }
    public static PublicMarketBatch ParseBatch(string json,uint world,IReadOnlySet<uint> requested)
    {
        if(world==0||requested.Count is 0 or >50||requested.Contains(0))throw new InvalidDataException("Invalid Universalis request scope");
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.GetProperty("worldID").GetUInt32()!=world)throw new InvalidDataException("Universalis response is not for the requested world");
        List<PublicMarketItem> result=[];List<PublicMarketHold> held=[];HashSet<uint> seen=[];
        void Item(uint expected,JsonElement node)
        {
            if(!requested.Contains(expected)||!seen.Add(expected))
                throw new InvalidDataException("Universalis item response does not match request");
            try
            {
            var id=node.GetProperty("itemID").GetUInt32();
            if(id!=expected)throw new InvalidDataException("Universalis item identity mismatch");
            if(node.TryGetProperty("worldID",out var w)&&w.GetUInt32()!=world)throw new InvalidDataException("Universalis item world mismatch");
            var entries=node.GetProperty("listings");
            if(entries.GetArrayLength()==0){held.Add(new(id,"No current listings; no disposal valuation is available"));return;}
            var uploaded=DateTimeOffset.FromUnixTimeMilliseconds(node.GetProperty("lastUploadTime").GetInt64());
            if(uploaded<=DateTimeOffset.UnixEpoch)throw new InvalidDataException("Universalis upload time is unavailable");
            List<PublicMarketListing> listings=[];
            foreach(var listing in entries.EnumerateArray())
            {
                if(listing.TryGetProperty("worldID",out var lw)&&lw.GetUInt32()!=world)throw new InvalidDataException("Universalis listing world mismatch");
                var price=listing.GetProperty("pricePerUnit").GetUInt32();var quantity=listing.GetProperty("quantity").GetUInt32();
                if(price is 0 or >999999999||quantity is 0 or >9999)throw new InvalidDataException("Invalid Universalis listing values");
                ulong? retainer=null;
                if(listing.TryGetProperty("retainerID",out var seller)&&seller.ValueKind==JsonValueKind.String&&
                    ulong.TryParse(seller.GetString(),NumberStyles.None,CultureInfo.InvariantCulture,out var value)&&value!=0)retainer=value;
                listings.Add(new(listing.GetProperty("hq").GetBoolean(),price,quantity,retainer,
                    listing.TryGetProperty("onMannequin",out var mannequin)&&mannequin.GetBoolean()));
            }
            result.Add(new(id,world,uploaded,listings));
            }
            catch(Exception ex)when(ex is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentOutOfRangeException or OverflowException)
            {held.Add(new(expected,ex.Message));}
        }
        if(root.TryGetProperty("items",out var items))
        {
            foreach(var item in items.EnumerateObject())
            {
                if(!uint.TryParse(item.Name,NumberStyles.None,CultureInfo.InvariantCulture,out var id))throw new InvalidDataException("Invalid Universalis item key");
                Item(id,item.Value);
            }
        }
        else Item(requested.Count==1?requested.Single():root.GetProperty("itemID").GetUInt32(),root);
        foreach(var id in requested.Except(seen))held.Add(new(id,"No data returned for this item"));
        return new(result,held);
    }
}
