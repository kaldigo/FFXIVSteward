using System.Text.Json;

namespace Steward.Core;

public sealed record ReviewEntry(ulong Character,string Location,uint ItemId,uint Quantity,bool Hq,string Action,string Reason,
    uint? UnitPrice=null,ulong? Total=null,uint? World=null,ulong? Retainer=null,string? PriceSource=null,DateTimeOffset? PriceObserved=null,
    CollectionCopyEvidence? Collection=null,string? RuleName=null);

public sealed class ReviewJournal
{
    private Dictionary<string,ReviewEntry> previous=[];
    public IReadOnlyList<ReviewEntry> Snapshot(string path,IEnumerable<ReviewEntry> entries)
    {
        var current=entries.Distinct().ToDictionary(e=>$"{e.Character}/{e.Location}/{e.ItemId}/{e.Action}");
        // Observation time alone must not rewrite every unchanged proposal on every five-second scan.
        static ReviewEntry Comparable(ReviewEntry e)=>e.Collection==null?e:e with{Collection=e.Collection with{ObservedAt=default}};
        var changed=current.Where(p=>!previous.TryGetValue(p.Key,out var old)||Comparable(old)!=Comparable(p.Value)).Select(p=>p.Value).ToArray();
        if(changed.Length>0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllLines(path,changed.Select(e=>JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,mode="LogOnly",proposal=e})));
        }
        previous=current; // Never suppress a record whose write failed.
        return changed;
    }
    public void Reset()=>previous.Clear();
}
