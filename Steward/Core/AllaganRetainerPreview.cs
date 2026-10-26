namespace Steward.Core;

// Allagan sorts native pages into five 35-slot bags. Sorting cannot be reversed without the game's order data.
// Reserve non-native container IDs so these observations can never masquerade as actionable native slots.
public static class AllaganRetainerPreview
{
    public const uint FirstContainer=3000000;
    public static bool IsPreview(uint container)=>container is >=FirstContainer and <FirstContainer+5;
    public static string Location(Stock stock)=>IsPreview(stock.Container)
        ?$"Allagan bag {stock.Container-FirstContainer+1}, slot {stock.Slot}"
        :$"native container {stock.Container}, slot {stock.Slot}";
    public static IReadOnlyList<Stock> Parse(ulong owner,IEnumerable<ulong[]> rows)
    {
        if(owner==0)throw new InvalidDataException("Missing retainer identity");
        List<Stock> result=[];HashSet<(uint,ushort)> positions=[];
        foreach(var row in rows)
        {
            if(row.Length<25)throw new InvalidDataException("Unsupported Allagan numeric inventory schema");
            if(row[0] is <10000 or >10006)continue; // Exclude equipment, crystals and market listings.
            if(row[23]!=owner)throw new InvalidDataException("Allagan retainer identity mismatch");
            if(row[2]==0||row[3]==0)continue;
            if(row[0]>10004||row[1]>=35)throw new InvalidDataException("Unsupported Allagan sorted bag layout");
            var id=checked((uint)row[2])%1000000;
            if(id==0)throw new InvalidDataException("Invalid Allagan item");
            var stock=new Stock(id,(row[6]&1)!=0,checked((uint)row[3]),FirstContainer+(uint)row[0]-10000,(ushort)row[1]);
            if(!positions.Add((stock.Container,stock.Slot)))throw new InvalidDataException("Duplicate Allagan sorted slot");
            result.Add(stock);
        }
        return result;
    }
}
