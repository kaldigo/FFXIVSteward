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
            var imports=new ExternalImports(new Interface(root),config,view);
            imports.Refresh();
            for(var i=0;i<5;i++)
            {
                view.Evidence.InvalidateLive();imports.RefreshReferences();
                check(view.Evidence.ReferencesReady&&view.Evidence.References.SetEquals(new uint[]{42,99}),"Between full imports, live refresh restores the exact selected reference union");
            }
            view.NativeReferencesKnown=false;imports.RefreshReferences();
            check(!view.Evidence.ReferencesReady,"Real native plate unavailability still holds actions");
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
