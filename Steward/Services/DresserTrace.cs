using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Steward.Services;

// Opt-in observation only. No callbacks, clicks, native writes or automation are performed here.
public sealed unsafe class DresserTrace(IAddonLifecycle lifecycle,IGameGui gui,Action<string> record,Func<uint,string> inventoryEvidence) : IDisposable
{
    private static readonly string[] names=["MiragePrismPrismBox","MiragePrismPrismBoxCrystallize","MiragePrismPrismSetConvert","MiragePrismPrismSetConvertC","SelectYesno","ContextMenu"];
    private readonly NativeUi ui=new(gui);
    private long until;
    private int count;
    private string last="";
    private uint observedItem;
    public bool Active { get; private set; }
    public void Start()
    {
        Stop();count=0;last="";observedItem=0;until=Environment.TickCount64+120000;Active=true;
        lifecycle.RegisterListener(AddonEvent.PreReceiveEvent,names,Receive);
        Emit("Started: two-minute manual observation; automation must remain off");
    }
    private void Emit(string text)
    {
        if(count++>=200){Stop();return;}
        record("Dresser trace: "+text);
    }
    private void Receive(AddonEvent type,AddonArgs args)
    {
        if(!Active||args is not AddonReceiveEventArgs e)return;
        var name=e.AtkEventType.ToString();
        if(name.Contains("MouseOver")||name.Contains("MouseOut")||name.Contains("MouseMove")||name.Contains("RollOver")||name.Contains("RollOut")||name.Contains("Timer")||name.Contains("Timeline"))return;
        try
        {
            var payload=e.AtkEventData==0?"none":Convert.ToHexString(new ReadOnlySpan<byte>((void*)e.AtkEventData,Math.Min(sizeof(AtkEventData),32)));
            Emit($"UI {args.AddonName}: event={e.AtkEventType}, param={e.EventParam}, data={payload}; {Selection()}");
        }
        catch(Exception ex){record("Dresser trace observation failed: "+ex.Message);Stop();}
    }
    private string Selection()
    {
        var agent=AgentMiragePrismPrismBox.Instance();
        if(agent==null||agent->Data==null)return "selection unavailable";
        var row=agent->Data->CrystallizeSelectedItem;
        if(row.ItemId!=0)observedItem=row.ItemId%1000000;
        var convert=AgentMiragePrismPrismSetConvert.Instance();
        var conversion=convert==null||convert->Data==null?"none":$"{convert->Data->SourceItemId}/{convert->Data->SourceContainer}/{convert->Data->SourceSlot},state={convert->Data->State},confirm={convert->Data->ConfirmationAddonId}";
        return $"selected={row.ItemId}/{row.Inventory}/{row.Slot}, row={agent->Data->CrystallizeItemIndex}, count={agent->Data->CrystallizeItemCount}, context={agent->ContextAddonId}, conversion={conversion}";
    }
    public void Tick(bool allowed)
    {
        if(!Active)return;
        if(!allowed||Environment.TickCount64>=until){Stop();return;}
        try
        {
            var state=Selection()+"; "+string.Join("; ",names.Select(ui.Describe));
            if(observedItem!=0)state+="; "+inventoryEvidence(observedItem);
            if(state!=last){last=state;Emit(state);}
        }
        catch(Exception ex){record("Dresser trace observation failed: "+ex.Message);Stop();}
    }
    public void Stop()
    {
        if(!Active)return;
        Active=false;lifecycle.UnregisterListener(AddonEvent.PreReceiveEvent,names,Receive);
        record("Dresser trace: stopped; existing execution hold retained");
    }
    public void Dispose()=>Stop();
}
