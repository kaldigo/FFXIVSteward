namespace Steward.Core;

public static class ExecutionJournal
{
    public static void Begin(string name,Action<string?> set,Action save)
    {
        set(name);
        save(); // Must succeed before the first operation tick can touch the game.
    }
    public static void Complete(string previous,Action<string?> set,Action save)
    {
        set(null);
        try{save();}catch{set(previous);throw;}
    }
}
