using Steward;
using Steward.Services;
static class ReferenceRefreshChecks
{
    private sealed class Interface(string root):Dalamud.Plugin.IDalamudPluginInterface
    {public string GetPluginConfigDirectory()=>Path.Combine(root,"Steward");}
    public static void Run(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"steward-references-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"Glamaholic.json");
            File.WriteAllText(path,"""{"Version":1,"Plates":[{"Items":{"Head":{"ItemId":42}}}]}""");
            var config=new Configuration{ImportMarketBuddy=false};
            var view=new InventoryView{NativeReferencesKnown=true,NativeReferences=[99]};
            uint[]? designItems=[42,72,72];
            var installed=new HashSet<string>{"Glamaholic","Aetherloom"};
            var reads=0;
            var imports=new ExternalImports(new Interface(root),config,view,()=>{reads++;return designItems??throw new InvalidOperationException();},()=>installed);
            imports.Refresh();
            for(var i=0;i<5;i++)
            {
                view.Evidence.InvalidateLive();imports.RefreshReferences();
                check(view.Evidence.ReferencesReady&&view.Evidence.References.SetEquals(new uint[]{42,99}),"Between full imports, live refresh restores the exact selected reference union");
            }
            config.Rules.Glamour.Aetherloom=true;
            imports.RefreshReferences();
            check(view.Evidence.ReferencesReady && view.Evidence.References.SetEquals(new uint[]{42,72,99}),"Aetherloom adds unique references alongside Glamaholic and native plates");
            installed.Clear();
            var before=reads;
            imports.RefreshReferences();
            check(reads==before&&view.Evidence.ReferencesReady&&view.Evidence.References.SetEquals([99]),"Both absent: native-only protection, stale config ignored, no IPC call");
            installed.Add("Aetherloom");imports.RefreshReferences();
            check(view.Evidence.ReferencesReady&&view.Evidence.References.SetEquals([42,72,99])&&!view.Evidence.GlamaholicKnown,"Aetherloom alone protects its designs");
            installed.Clear();installed.Add("Glamaholic");imports.RefreshReferences();
            check(view.Evidence.ReferencesReady&&view.Evidence.References.SetEquals([42,99])&&view.Evidence.AetherloomReferences==null,"Glamaholic alone protects its outfits");
            installed.Add("Aetherloom");
            designItems=null;imports.RefreshReferences();
            check(!view.Evidence.ReferencesReady && !view.Evidence.References.Contains(72),"Unavailable selected Aetherloom invalidates previous results");
            designItems=[];imports.RefreshReferences();
            check(view.Evidence.ReferencesReady && view.Evidence.References.SetEquals(new uint[]{42,99}),"Empty Aetherloom library is a successful read");
            config.Rules.Glamour.Aetherloom=false;
            view.NativeReferencesKnown=false;imports.RefreshReferences();
            check(!view.Evidence.ReferencesReady,"Real native plate unavailability still holds actions");
            view.NativeReferencesCached=true;imports.RefreshReferences();
            check(view.Evidence.ReferencesReady&&view.Evidence.References.Contains(99),"Saved native plate configuration supplies references without asserting live native data");
            view.NativeReferencesCached=false;
            view.NativeReferencesKnown=true;File.WriteAllText(path,"invalid");imports.RefreshReferences();
            check(!view.Evidence.ReferencesReady&&!view.Evidence.GlamaholicKnown,"Malformed saved outfits cannot reuse ready cached imports");
            File.Delete(path);imports.RefreshReferences();
            check(!view.Evidence.ReferencesReady,"Missing selected outfit source stays unavailable");
            config.Rules.Glamour.Glamaholic=false;imports.RefreshReferences();
            check(view.Evidence.ReferencesReady&&view.Evidence.References.SetEquals(new uint[]{99}),"Disabling a source removes only its references");
        }
        finally{Directory.Delete(root,true);}
    }
}
