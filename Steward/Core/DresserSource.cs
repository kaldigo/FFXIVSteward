namespace Steward.Core;

public readonly record struct DresserTreeRow(int Index,uint Type,uint AgentIndex,uint Container,uint Slot);
public static class DresserSource
{
    // Native source-category table: inventory, weapons, head/body/hands, legs/feet, ears/neck, wrists/rings.
    public static int Category(uint container)=>container switch
    {
        <=3=>0,
        3500 or 3200=>1,
        3201 or 3202 or 3203=>2,
        3205 or 3206=>3,
        3207 or 3208=>4,
        3209 or 3300=>5,
        _=>-1,
    };
    // The six native integer fields are type, agent index, icon, container, slot, flags.
    // Names are formatted display data, not inventory identity. Headers do not map to an item.
    public static int Find(IEnumerable<DresserTreeRow> rows,int agentIndex,Stock source)
    {
        if(agentIndex<0)return -1;
        var selected=-1;
        foreach(var row in rows)
        {
            if((row.Type&6)!=0||row.AgentIndex!=agentIndex||row.Container!=source.Container||row.Slot!=source.Slot)continue;
            if(selected>=0)return -1;
            selected=row.Index;
        }
        return selected;
    }
}
