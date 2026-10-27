using Steward.Core;
using System.Text.Json;
static class NativePlateCacheChecks
{
    public static void Run(Action<bool,string> check)
    {
        var dir=Path.Combine(Path.GetTempPath(),"Steward-plates-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,"memory.json");var at=DateTimeOffset.UtcNow.AddMinutes(-1);
        try
        {
            using(var m=new InventoryMemory(path))
            {
                check(!m.TryNativePlates(1,out _,out _),"No saved plates means unknown, not an empty outfit");
                m.ObserveNativePlates(1,[42,43],at);m.ObserveNativePlates(2,[],at);m.Flush();
            }
            using(var m=new InventoryMemory(path))
            {
                check(m.TryNativePlates(1,out var ids,out var time)&&ids.SetEquals(new uint[]{42,43})&&time==at,"Native references persist by character across restart");
                ids.Clear();check(m.TryNativePlates(1,out ids,out _)&&ids.Count==2,"Callers cannot mutate cached references");
                check(m.TryNativePlates(2,out var empty,out _)&&empty.Count==0,"Observed empty plates are distinct from unknown plates");
                check(!m.TryNativePlates(3,out _,out _)&&!m.TryNativePlates(0,out _,out _),"No cross-character or logged-out fallback");
                m.ObserveNativePlates(1,[43,42],DateTimeOffset.UtcNow);m.TryNativePlates(1,out _,out time);
                check(time==at,"Unchanged live snapshots do not churn timestamps or writes");
                var e=new Evidence();m.Apply(1,e);
                check(!e.DresserKnown&&!e.ArmoireKnown&&!e.ReferencesReady,"Plate configuration never invents collection ownership or readiness");
                m.ObserveNativePlates(1,[],DateTimeOffset.UtcNow);m.Flush();
            }
            using(var m=new InventoryMemory(path))check(m.TryNativePlates(1,out var ids,out _)&&ids.Count==0,"Authoritative empty observation clears old protected references");
            File.WriteAllText(path,"{\"1\":{\"NativePlatesObserved\":true,\"NativePlatesObservedAt\":\"2026-01-01T00:00:00Z\"}}");
            using(var m=new InventoryMemory(path))check(!m.TryNativePlates(1,out _,out _),"Missing saved item set cannot masquerade as observed empty plates");
            File.WriteAllText(path,"invalid");using(var m=new InventoryMemory(path))check(!m.TryNativePlates(1,out _,out _),"Corrupt saved plates are unavailable");
            File.WriteAllText(path,"{\"1\":{}}");using(var m=new InventoryMemory(path))check(!m.TryNativePlates(1,out _,out _),"Legacy inventory cache cannot pretend plates were captured");
            File.WriteAllText(path,JsonSerializer.Serialize(new Dictionary<ulong,InventoryMemory.CharacterInventory>{[1]=new(){NativePlatesObserved=true,NativePlateItems=[42]}}));
            using(var m=new InventoryMemory(path))check(!m.TryNativePlates(1,out _,out _),"Incomplete plate snapshot is rejected");
        }
        finally{Directory.Delete(dir,true);}
    }
}
