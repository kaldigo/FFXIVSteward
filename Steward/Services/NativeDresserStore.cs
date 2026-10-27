using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Steward.Core;
using System.Runtime.InteropServices;

namespace Steward.Services;

public sealed unsafe class NativeDresserStore
{
    // Independently identified in the installed client; see docs/dresser-native-deposit.md.
    // Resolve both the function body and its exact container/ushort-slot caller. No fixed addresses.
    internal const string BodySignature="48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 48 83 EC 30 41 0F B7 F0 48 8B D9 44 8B C6 48 8D 0D ?? ?? ?? ?? 8B FA";
    internal const string CallerSignature="E8 ?? ?? ?? ?? 44 0F B7 83 94 AE 11 00 48 8B C8 8B 93 90 AE 11 00 E8 ?? ?? ?? ?? 84 C0";
    private readonly delegate* unmanaged<MirageManager*,InventoryType,ushort,byte> ordinary;
    private readonly Configuration config;
    public bool OrdinaryAvailable=>ordinary!=null;

    public NativeDresserStore(ISigScanner scanner,Configuration config)
    {
        this.config=config;
        if(scanner.TryScanText(BodySignature,out var body)&&scanner.TryScanText(CallerSignature,out var caller))
        {
            // GetInstance call (5), movzx r8d (8), mov rcx (3), mov edx (6), deposit call (5).
            var target=caller+27+Marshal.ReadInt32(caller+23);
            if(target==body)ordinary=(delegate* unmanaged<MirageManager*,InventoryType,ushort,byte>)body;
        }
    }

    public bool Submit(MirageManager* manager, Stock source, DresserDeposit plan)
    {
        if(!config.ExecutionEnabled||config.ExecutionReviewRequired||!config.Features.DresserCollection||
            manager==null||!manager->PrismBoxLoaded||DresserSource.Category(source.Container)<0||source.Quantity!=1)
            return false;
        if(plan.Kind==DresserDepositKind.Ordinary)
            return ordinary!=null&&ordinary(manager,(InventoryType)source.Container,source.Slot)!=0;
        // Native outfit methods take nine packed source slots, not an array indexed by equipment slot.
        // Unused entries terminate the source list. Submit one approved piece at a time.
        var containers=stackalloc InventoryType[9];
        var slots=stackalloc ushort[9];
        for(var i=0;i<9;i++){containers[i]=InventoryType.Invalid;slots[i]=0;}
        containers[0]=(InventoryType)source.Container;slots[0]=source.Slot;
        return plan.Kind==DresserDepositKind.NewOutfit
            ? manager->StoreNewOutfit(plan.Target,containers,slots)
            : manager->StoreExistingOutfit(plan.Target,containers,slots);
    }
}
