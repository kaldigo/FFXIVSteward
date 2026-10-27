using Dalamud.Plugin.Services;
using Steward.Core;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
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
        =>CallbackCore(name,true,values);
    public bool DresserSelectionReady()
    {
        var addon=(AddonMiragePrismPrismBoxCrystallize*)Get("MiragePrismPrismBoxCrystallize");
        return addon!=null&&addon->ItemTreeList!=null&&!addon->ItemTreeList->LayoutRefreshPending&&!addon->ItemTreeList->IsUpdatePending;
    }
    public bool SelectDresserItem(string name,out int treeIndex)
    {
        treeIndex=-1;
        var addon=(AddonMiragePrismPrismBoxCrystallize*)Get("MiragePrismPrismBoxCrystallize");
        if(addon==null||addon->ItemTreeList==null)return false;
        var list=addon->ItemTreeList;
        if(list->LayoutRefreshPending||list->IsUpdatePending)return false;
        // Tree indices include headers: the recorded chestwrap was tree index 1, agent row 0.
        // Match a unique named leaf, then validate the exact native source before confirmation.
        var selected=-1;
        for(var i=0;i<list->Items.Count;i++)
        {
            var entry=list->Items[i].Value;
            if(entry==null||(entry->Type&(TreeListItemType.Group|TreeListItemType.SectionHeader))!=0||entry->StringValues.Count==0)continue;
            if(entry->StringValues[0].ToString()!=name)continue;
            if(selected>=0)return false;
            if(entry->IsHidden||(entry->State&TreeListItemState.Disabled)!=0)return false;
            selected=i;
        }
        if(selected<0)return false;
        treeIndex=selected;
        list->DispatchItemEvent(selected,AtkEventType.ListItemClick);
        return true; // Submission only. The queue verifies native selection and ownership next frame.
    }
    public bool OwnsDresserConfirmation(ushort owner,Stock source)
    {
        var window=Get("MiragePrismPrismBoxCrystallize");
        var confirm=Get("SelectYesno");
        var agent=AgentMiragePrismPrismBox.Instance();
        if(window==null||confirm==null||agent==null||agent->Data==null)return false;
        var selected=agent->Data->CrystallizeSelectedItem;
        return DresserConfirmation.Matches(owner,window->Id,confirm->Id,agent->ContextAddonId,
            source,selected.ItemId%1000000,(uint)selected.Inventory,selected.Slot);
    }
    public bool ClickDresserConfirmation(ushort owner,Stock source,bool accept)
    {
        if(!OwnsDresserConfirmation(owner,source))return false;
        var confirm=(AddonSelectYesno*)Get("SelectYesno");
        return ClickButton(&confirm->AtkUnitBase,accept?confirm->YesButton:confirm->NoButton);
    }
    public bool CancelDresserConfirmation(ushort owner,Stock source)
        =>!Visible("SelectYesno")||ClickDresserConfirmation(owner,source,false);
    private bool CallbackCore(string name,bool close,int[] values)
    {
        var addon=Get(name);if(addon==null)return false;
        var data=stackalloc AtkValue[values.Length];
        for(var i=0;i<values.Length;i++)data[i]=new(){Type=AtkValueType.Int,Int=values[i]};
        return addon->FireCallback((uint)values.Length,data,close);
    }
    public void Close(string name){var a=Get(name);if(a!=null)a->Close(true);}
}
