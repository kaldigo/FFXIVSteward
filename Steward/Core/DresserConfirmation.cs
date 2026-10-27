namespace Steward.Core;

public static class DresserConfirmation
{
    // Ordinary dresser Yes/No has zero ParentId/HostId. ContextAddonId links it to the agent.
    public static bool Matches(ushort owner,ushort currentOwner,ushort confirmation,uint context,
        Stock source,uint item,uint container,int slot)
        =>owner!=0&&owner==currentOwner&&confirmation!=0&&context==confirmation&&
          source.ItemId==item&&source.Container==container&&source.Slot==slot;
    public static StepResult Acknowledge(Stock source,Stock? live,bool confirmationVisible,
        bool sourceLoaded,bool dresserKnown,bool dresserOwned)
    {
        if(live==source||confirmationVisible||!sourceLoaded||!dresserKnown)return StepResult.Waiting;
        if(!StockTransition.Removed(source,live,1,true))return StepResult.Failed;
        return dresserOwned?StepResult.Completed:StepResult.Waiting;
    }
}
