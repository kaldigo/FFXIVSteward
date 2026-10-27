using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Steward.Services;

public sealed unsafe class NativeUi(IGameGui gui)
{
    public string Describe(string name)
    {
        var reference=gui.GetAddonByName(name);
        if(reference.IsNull)return name+": absent";
        var addon=(AtkUnitBase*)reference.Address;
        return $"{name}: id={addon->Id}, parent={addon->ParentId}, host={addon->HostId}, visible={addon->IsVisible}, ready={addon->IsReady}";
    }
    public bool Visible(string name){var addon=gui.GetAddonByName(name);return !addon.IsNull&&addon.IsVisible;}
    public AtkUnitBase* Get(string name)
    {var a=gui.GetAddonByName(name);if(a.IsNull||!a.IsVisible)return null;var native=(AtkUnitBase*)a.Address;return native->IsReady?native:null;}
    public bool Click(string name,uint id)
    {
        var addon=Get(name);if(addon==null)return false;
        var button=addon->GetComponentButtonById(id);
        return ClickButton(addon,button);
    }
    private static bool ClickButton(AtkUnitBase* addon,AtkComponentButton* button)
    {
        if(addon==null || button==null || !button->IsEnabled || button->OwnerNode==null)return false;
        var evt=button->OwnerNode->AtkResNode.AtkEventManager.Event;
        if(evt==null)return false;
        // Use the button's registered native event, as ECommons' ClickAddonButton does.
        addon->ReceiveEvent(evt->State.EventType,(int)evt->Param,evt);
        return true;
    }
    public bool CancelOwnedConfirmation(ushort owner)
    {
        var reference=gui.GetAddonByName("SelectYesno");
        if(reference.IsNull || !reference.IsVisible)return true;
        var addon=(FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno*)reference.Address;
        if(!addon->IsReady)return false;
        return Steward.Core.ConfirmationOwnership.Matches(owner,addon->ParentId,addon->HostId) && ClickButton(&addon->AtkUnitBase,addon->NoButton);
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
