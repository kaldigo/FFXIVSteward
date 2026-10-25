namespace Steward.Core;

public static class ExecutionMode
{
    public static void Persist(Action save,Action hold)
    {
        try{save();}catch{hold();throw;}
    }
    public static bool Enable(bool ready,Action<bool> set,Action save)
    {
        if(!ready)return false;
        set(true);
        try{save();return true;}
        catch{set(false);throw;}
    }
}
