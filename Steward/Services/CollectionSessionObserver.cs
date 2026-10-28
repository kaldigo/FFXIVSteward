using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;

namespace Steward.Services;

public sealed class CollectionSessionObserver : IDisposable
{
    private readonly IAddonLifecycle lifecycle;
    private readonly InventoryView view;
    private readonly Func<ulong> character;
    private readonly Func<bool> uncertain;
    private readonly Action<string> record;
    private static readonly AddonEvent[] reopen=[AddonEvent.PreSetup,AddonEvent.PreOpen,AddonEvent.PreShow];
    public CollectionSessionObserver(IAddonLifecycle lifecycle,InventoryView view,Func<ulong> character,Func<bool> uncertain,Action<string> record)
    {
        this.lifecycle=lifecycle;this.view=view;this.character=character;this.uncertain=uncertain;this.record=record;
        foreach(var e in reopen)lifecycle.RegisterListener(e,"MiragePrismPrismBox",Open);
        lifecycle.RegisterListener(AddonEvent.PreClose,"MiragePrismPrismBox",Close);
    }
    private void Open(AddonEvent e,AddonArgs args)=>view.SessionDresser.Invalidate();
    private void Close(AddonEvent e,AddonArgs args)
    {
        view.SessionDresser.Invalidate();
        try
        {
            var cid=character();if(cid==0)return;
            view.Refresh(cid);
            view.SessionDresser.Seal(cid,view.Evidence.DresserCopyCounts,view.Evidence.DresserKnown,uncertain());
            record(view.SessionDresser.Read(cid)!=null
                ?$"Dresser session ownership verified at close: {view.Evidence.DresserCopyCounts.Count} exact item IDs; valid until next dresser opening or session end"
                :"Dresser session ownership not sealed: native contents unavailable or an operation is uncertain; surplus disposal remains held");
        }
        catch(Exception ex){view.SessionDresser.Invalidate();record("Dresser session ownership could not be verified: "+ex.Message);}
    }
    public void Dispose()
    {
        foreach(var e in reopen)lifecycle.UnregisterListener(e,"MiragePrismPrismBox",Open);
        lifecycle.UnregisterListener(AddonEvent.PreClose,"MiragePrismPrismBox",Close);
        view.SessionDresser.Invalidate();
    }
}
