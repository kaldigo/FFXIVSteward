using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Steward.Services;

public sealed unsafe class NativeUi(IGameGui gui)
{
    public AtkUnitBase* Get(string name)
    {var a=gui.GetAddonByName(name);return a.IsNull||!a.IsVisible?null:(AtkUnitBase*)a.Address;}
    public bool Click(string name,uint id)
    {
        var addon=Get(name);if(addon==null)return false;
        var button=addon->GetComponentButtonById(id);
        if(button==null || !button->IsEnabled || button->OwnerNode==null)return false;
        var evt=button->OwnerNode->AtkResNode.AtkEventManager.Event;
        if(evt==null)return false;
        // Use the button's registered native event, as ECommons' ClickAddonButton does.
        addon->ReceiveEvent(evt->State.EventType,(int)evt->Param,evt);
        return true;
    }
    public bool Callback(string name,params int[] values)
    {
        var addon=Get(name);if(addon==null)return false;
        var data=stackalloc AtkValue[values.Length];
        for(var i=0;i<values.Length;i++)data[i]=new(){Type=AtkValueType.Int,Int=values[i]};
        return addon->FireCallback((uint)values.Length,data,true);
    }
    public void Close(string name){var a=Get(name);if(a!=null)a->Close(true);}
}
