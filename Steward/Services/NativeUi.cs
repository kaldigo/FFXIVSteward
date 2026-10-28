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
        // Addon listeners (including Allagan Market) read the event-data argument.
        // Supply an initialized payload rather than the optional null pointer.
        AtkEventData data=default;
        addon->ReceiveEvent(evt->State.EventType,(int)evt->Param,evt,&data);
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
    public bool SetRetainerSaleValue(int field,int value,out bool callbackResult)
    {
        callbackResult=false;
        if(field is not (2 or 3)||value<=0)return false;
        var addon=Get("RetainerSell");if(addon==null)return false;
        var data=stackalloc AtkValue[2];
        data[0]=new(){Type=AtkValueType.Int,Int=field};
        data[1]=new(){Type=AtkValueType.Int,Int=value};
        callbackResult=addon->FireCallback(2,data,true);
        // Dispatch is not acknowledgement. The caller must read back both the
        // agent and numeric controls before allowing the final sell click.
        return true;
    }
    public int DresserCategory()
    {
        var addon=(AddonMiragePrismPrismBoxCrystallize*)Get("MiragePrismPrismBoxCrystallize");
        var agent=AgentMiragePrismPrismBox.Instance();
        if(addon==null||addon->CategoryDropDown==null||agent==null||agent->Data==null)return -1;
        var category=agent->Data->CrystallizeCategory;
        return category is >=0 and <6&&addon->CategoryDropDown->GetSelectedItemIndex()==category?category:-1;
    }
    public bool AdvanceDresserCategory()
    {
        if(!DresserSelectionReady()||Visible("SelectYesno")||Visible("MiragePrismPrismSetConvert"))return false;
        var addon=(AddonMiragePrismPrismBoxCrystallize*)Get("MiragePrismPrismBoxCrystallize");
        return addon!=null&&ClickButton(&addon->AtkUnitBase,addon->CategoryNextButton);
    }
    public bool DresserSelectionReady()
    {
        var addon=(AddonMiragePrismPrismBoxCrystallize*)Get("MiragePrismPrismBoxCrystallize");
        var agent=AgentMiragePrismPrismBox.Instance();
        return addon!=null&&addon->ItemTreeList!=null&&agent!=null&&agent->Data!=null&&
            !agent->Data->IsPopulatingList&&agent->Data->IsPopulatingComplete&&
            !addon->ItemTreeList->LayoutRefreshPending&&!addon->ItemTreeList->IsUpdatePending;
    }
    public bool SelectDresserItem(Stock source,int agentIndex,out int treeIndex)
    {
        treeIndex=-1;
        if(!DresserSelectionReady()||DresserCategory()!=DresserSource.Category(source.Container))return false;
        var addon=(AddonMiragePrismPrismBoxCrystallize*)Get("MiragePrismPrismBoxCrystallize");
        var agent=AgentMiragePrismPrismBox.Instance();
        if(agentIndex<0||agentIndex>=agent->Data->CrystallizeItemCount)return false;
        var candidate=agent->Data->CrystallizeItems[agentIndex];
        if(candidate.ItemId%1000000!=source.ItemId||(uint)candidate.Inventory!=source.Container||candidate.Slot!=source.Slot)return false;
        var list=addon->ItemTreeList;
        List<DresserTreeRow> rows=[];
        for(var i=0;i<list->Items.Count;i++)
        {
            var entry=list->Items[i].Value;
            if(entry==null||entry->UIntValues.Count!=6)continue;
            rows.Add(new(i,entry->UIntValues[0],entry->UIntValues[1],entry->UIntValues[3],entry->UIntValues[4]));
        }
        var selected=DresserSource.Find(rows,agentIndex,source);
        if(selected<0)return false;
        var item=list->Items[selected].Value;
        if(item->IsHidden||(item->State&TreeListItemState.Disabled)!=0)return false;
        treeIndex=selected;
        // The native tree click translates to UInt callbacks [14, agentIndex, 0].
        // AtkComponentList.DispatchItemEvent omits ListItemData.ListItem and crashes this tree's handler.
        var arguments=DresserSource.SelectionArguments(agentIndex);
        var values=stackalloc AtkValue[arguments.Length];
        for(var i=0;i<arguments.Length;i++)values[i]=new(){Type=AtkValueType.UInt,UInt=arguments[i]};
        addon->FireCallback((uint)arguments.Length,values,true); // Match the game's own click-handler bridge.
        return true; // Submission only. Exact source and dialog ownership are verified before Yes.
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
