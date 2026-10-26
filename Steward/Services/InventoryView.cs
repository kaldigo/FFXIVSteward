using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Steward.Core;
using Sheets = Lumina.Excel.Sheets;

namespace Steward.Services;

public sealed unsafe class InventoryView
{
    private readonly IDataManager data;
    private readonly Configuration config;
    private readonly Dictionary<uint,uint[]> outfits;
    private readonly (uint Row,uint Item)[] cabinet;
    private readonly HashSet<uint> crafted;
    private readonly IReadOnlyDictionary<uint,SourceKind> acquisitionSources=AcquisitionSources.Load();
    public Evidence Evidence { get; } = new();
    public List<Stock> Items { get; } = [];
    public HashSet<uint> NativeReferences { get; private set; } = [];
    public bool NativeReferencesKnown { get; private set; }
    public ulong Character { get; private set; }
    public HashSet<ulong> Retainers { get; } = [];
    public uint World { get; set; }
    public InventoryView(IDataManager data,Configuration config)
    {
        this.data=data;this.config=config;
        cabinet=data.GetExcelSheet<Sheets.Cabinet>().Where(r=>r.Item.RowId!=0).Select(r=>(r.RowId,r.Item.RowId)).ToArray();
        outfits=data.GetExcelSheet<Sheets.MirageStoreSetItem>().ToDictionary(r=>r.RowId,r=>new[]{r.MainHand.RowId,r.OffHand.RowId,r.Head.RowId,r.Body.RowId,r.Hands.RowId,r.Legs.RowId,r.Feet.RowId,r.Earrings.RowId,r.Necklace.RowId,r.Bracelets.RowId,r.Ring.RowId});
        crafted=data.GetExcelSheet<Sheets.Recipe>().Where(r=>r.ItemResult.RowId!=0).Select(r=>r.ItemResult.RowId).ToHashSet();
    }
    public ItemFacts? Facts(uint id)
    {
        id%=1000000;
        if(!data.GetExcelSheet<Sheets.Item>().TryGetRow(id,out var row))return null;
        var source=config.Sources.GetValueOrDefault(id,crafted.Contains(id)?SourceKind.Ordinary:SourceKind.Unknown);
        source=AcquisitionSources.Merge(source,acquisitionSources.GetValueOrDefault(id));
        source=AcquisitionSources.Merge(source,config.DutySources.GetValueOrDefault(id));
        if(row.IsPvP)source=SourceKind.Special;
        // IsGlamorous means dresser-compatible (almost all equipment), not cosmetic-purpose gear.
        return new(id,row.Name.ToString(),row.EquipSlotCategory.RowId!=0,row.Rarity,row.LevelEquip,
            !row.IsUntradable && row.ItemSearchCategory.RowId!=0,cabinet.Any(c=>c.Item==id),source,row.ItemUICategory.RowId,row.PriceLow,row.LevelItem.RowId==1);
    }
    public void Refresh(ulong character)
    {
        Evidence.InvalidateLive();
        Evidence.DresserFreeSlots=null;
        Evidence.PendingCollection=CollectionHolds.ForCharacter(config.PendingCollection,character);
        NativeReferencesKnown=false;
        if(Character!=character)
        {
            Character=character;Evidence.Dresser.Clear();Evidence.Armoire.Clear();Evidence.Prices.Clear();Evidence.PublicOwnPrices.Clear();Evidence.References.Clear();
            Evidence.DresserKnown=Evidence.ArmoireKnown=Evidence.GearsetsReady=Evidence.ReferencesReady=false;
            NativeReferencesKnown=false;NativeReferences.Clear();Retainers.Clear();
        }
        Items.Clear();Evidence.Gearsets.Clear();Evidence.GearsetsReady=false;
        if(character==0)return;
        var im=InventoryManager.Instance();if(im==null)return;
        foreach(var type in InventoryTypes())Read(type,Items);
        var gear=RaptureGearsetModule.Instance();
        var equippedInventory=im->GetInventoryContainer(InventoryType.EquippedItems);
        if(gear!=null && equippedInventory!=null && equippedInventory->IsLoaded)
        {
            foreach(var set in gear->Entries)
                if(set.Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                    foreach(var item in set.Items){if(item.ItemId!=0)Evidence.Gearsets.Add(item.ItemId%1000000);}
            List<Stock> equipped=[];Read(InventoryType.EquippedItems,equipped);
            foreach(var slot in equipped)Evidence.Gearsets.Add(slot.ItemId);
            Evidence.GearsetsReady=true;
        }
        var mm=MirageManager.Instance();
        if(mm!=null && mm->PrismBoxLoaded)
        {
            var empty=0;foreach(var id in mm->PrismBoxItemIds)if(id==0)empty++;
            Evidence.DresserFreeSlots=empty;
            var collected=new HashSet<uint>();var copies=new Dictionary<uint,int>();
            void AddCollected(uint id){collected.Add(id);copies[id]=copies.GetValueOrDefault(id)+1;}
            for(var i=0;i<mm->PrismBoxItemIds.Length;i++)
            {
                var id=mm->PrismBoxItemIds[i]%1000000;if(id==0)continue;
                if(outfits.TryGetValue(id,out var pieces))
                {for(var p=0;p<pieces.Length;p++)if(pieces[p]!=0 && mm->IsSetSlotUnlocked((uint)i,p))AddCollected(pieces[p]);}
                else AddCollected(id);
            }
            Evidence.Dresser=collected;Evidence.DresserCopyCounts=copies;Evidence.DresserKnown=true;
        }
        if(mm!=null && mm->GlamourPlatesLoaded)
        {
            NativeReferences=[];
            foreach(var plate in mm->GlamourPlates)foreach(var id in plate.ItemIds)if(id!=0)NativeReferences.Add(id%1000000);
            NativeReferencesKnown=true;
        }
        var ui=UIState.Instance();
        if(ui!=null && ui->Cabinet.IsCabinetLoaded())
        {
            Evidence.Armoire=cabinet.Where(c=>ui->Cabinet.IsItemInCabinet(c.Row)).Select(c=>c.Item).ToHashSet();Evidence.ArmoireKnown=true;
        }
        var rm=RetainerManager.Instance();
        if(rm!=null && rm->IsReady)
            foreach(var r in rm->Retainers)if(r.RetainerId!=0){Retainers.Add(r.RetainerId);config.OwnRetainers.Add(r.RetainerId);}
    }
    public static InventoryType[] InventoryTypes() => Enum.GetValues<InventoryType>().Where(t=>t is >= InventoryType.Inventory1 and <= InventoryType.Inventory4 || t.ToString().StartsWith("Armory",StringComparison.Ordinal)).ToArray();
    public static InventoryType[] RetainerTypes() => [InventoryType.RetainerPage1,InventoryType.RetainerPage2,InventoryType.RetainerPage3,InventoryType.RetainerPage4,InventoryType.RetainerPage5,InventoryType.RetainerPage6,InventoryType.RetainerPage7];
    public static void Read(InventoryType type,List<Stock> target)
    {
        var im=InventoryManager.Instance();if(im==null)return;var inv=im->GetInventoryContainer(type);
        if(inv==null || !inv->IsLoaded)return;
        for(var i=0;i<inv->Size;i++)
        {
            var item=inv->GetInventorySlot(i);if(item==null||item->ItemId==0||item->Quantity<=0)continue;
            target.Add(new(item->ItemId%1000000,item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality),(uint)item->Quantity,(uint)type,(ushort)i));
        }
    }
    public static Stock? ReadSlot(uint container,ushort slot)
    {List<Stock> items=[];Read((InventoryType)container,items);return items.FirstOrDefault(i=>i.Slot==slot);}
    public static bool Loaded(uint type)
    {
        var manager=InventoryManager.Instance();if(manager==null)return false;
        var container=manager->GetInventoryContainer((InventoryType)type);return container!=null&&container->IsLoaded;
    }
    public static bool PlayerLoaded()=>InventoryTypes().Where(t=>t!=InventoryType.ArmoryWaist).All(t=>Loaded((uint)t));
    public static bool CanListStock(Stock stock)
    {
        if(ReadSlot(stock.Container,stock.Slot)!=stock)return false;
        var manager=InventoryManager.Instance();if(manager==null)return false;
        var inventory=manager->GetInventoryContainer((InventoryType)stock.Container);
        if(inventory==null||!inventory->IsLoaded||stock.Slot>=inventory->Size)return false;
        var item=inventory->GetInventorySlot(stock.Slot);
        return item!=null&&item->SpiritbondOrCollectability==0&&item->GlamourId==0&&!item->Flags.HasFlag(InventoryItem.ItemFlags.Collectable);
    }
    public static int FreeBagSlots()
    {
        var manager=InventoryManager.Instance();if(manager==null)return 0;
        var count=0;
        foreach(var type in new[]{InventoryType.Inventory1,InventoryType.Inventory2,InventoryType.Inventory3,InventoryType.Inventory4})
        {
            var bag=manager->GetInventoryContainer(type);if(bag==null||!bag->IsLoaded)return 0;
            for(var i=0;i<bag->Size;i++)if(bag->GetInventorySlot(i)->ItemId==0)count++;
        }
        return count;
    }
    public uint[]? OutfitPieces(uint id)=>outfits.GetValueOrDefault(id);
    public int DresserCopies(uint id)
    {
        var mm=MirageManager.Instance();if(mm==null||!mm->PrismBoxLoaded)return 0;
        var count=0;
        for(var slot=0;slot<mm->PrismBoxItemIds.Length;slot++)
        {
            var raw=mm->PrismBoxItemIds[slot]%1000000;
            if(outfits.TryGetValue(raw,out var pieces))
            {for(var p=0;p<pieces.Length;p++)if(pieces[p]==id&&mm->IsSetSlotUnlocked((uint)slot,p))count++;}
            else if(raw==id)count++;
        }
        return count;
    }
}
