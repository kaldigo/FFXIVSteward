using System.Text.Json;
using Lumina.Excel.Sheets;
if(args.Length!=2)throw new ArgumentException("Usage: Export <game sqpack directory> <metadata JSON output>");
using var game=new Lumina.GameData(args[0]);
var crafted=game.Excel.GetSheet<Recipe>().Where(r=>r.ItemResult.RowId!=0).Select(r=>r.ItemResult.RowId).ToHashSet();
var cabinet=game.Excel.GetSheet<Cabinet>().Where(r=>r.Item.RowId!=0).Select(r=>r.Item.RowId).ToHashSet();
var jobs=game.Excel.GetSheet<ClassJob>(Lumina.Data.Language.English).Where(r=>r.RowId!=0&&r.Abbreviation.ToString()!="").Select(r=>r.Abbreviation.ToString()).ToHashSet();
var flags=typeof(ClassJobCategory).GetProperties().Where(p=>p.PropertyType==typeof(bool)&&jobs.Contains(p.Name)).ToArray();
var items=game.Excel.GetSheet<Item>(Lumina.Data.Language.English).Where(r=>r.EquipSlotCategory.RowId!=0&&r.Name.ToString()!="").Select(r=>new {
 id=r.RowId,name=r.Name.ToString(),rarity=r.Rarity,level=r.LevelEquip,ilvl=r.LevelItem.RowId,
 marketable=!r.IsUntradable&&r.ItemSearchCategory.RowId!=0,pvp=r.IsPvP,armoire=cabinet.Contains(r.RowId),crafted=crafted.Contains(r.RowId),
 slot=r.EquipSlotCategory.RowId,category=r.ItemUICategory.RowId,
 jobs=flags.Where(p=>(bool)p.GetValue(r.ClassJobCategory.Value)!).Select(p=>p.Name).ToArray()
}).ToArray();
File.WriteAllText(args[1],JsonSerializer.Serialize(items));
Console.WriteLine($"Exported {items.Length} equipment records with slot/job, acquisition and existing-protection facts.");
