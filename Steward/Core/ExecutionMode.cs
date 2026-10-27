namespace Steward.Core;

public static class ExecutionMode
{
    public static void Persist(Action save,Action hold)
    {
        try{save();}catch{hold();throw;}
    }
}
