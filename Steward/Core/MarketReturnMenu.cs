namespace Steward.Core;

public static class MarketReturnMenu
{
    public sealed record Entry(int Index,string Text,bool Enabled);
    public static int Find(IEnumerable<Entry> entries,string expected)
    {
        if(string.IsNullOrWhiteSpace(expected))return -1;
        var matches=entries.Where(e=>string.Equals(e.Text,expected,StringComparison.Ordinal)).ToArray();
        return matches.Length==1&&matches[0].Enabled&&matches[0].Index>=0?matches[0].Index:-1;
    }
}
