using System.Runtime.InteropServices;
using Steward.Services;

var memory=Marshal.AllocHGlobal(256);
try
{
    var body=memory+128;var caller=memory;var getter=memory+192;
    Marshal.WriteByte(caller,0xe8);
    Marshal.WriteInt32(caller+1,checked((int)(getter-caller-5)));
    Marshal.WriteByte(caller+22,0xe8);
    Marshal.WriteInt32(caller+23,checked((int)(body-caller-27)));
    var scanner=new Scanner([body],[caller]);
    // Reproduce Dalamud ScanText's leading-call semantics, which caused the live failure.
    scanner.TryScanText(NativeDresserStore.CallerSignature,out var followed);
    Check(followed==getter,"Single-match scanner follows leading call to GetInstance");
    var callsBefore=scanner.SingleCalls;
    Check(new NativeDresserStore(scanner,new()).OrdinaryAvailable,"Production binding resolves literal caller and matching body");
    Check(scanner.SingleCalls==callsBefore,"Production binding must not use the call-following scanner");
    Check(!new NativeDresserStore(new Scanner([], [caller]),new()).OrdinaryAvailable,"Missing body holds binding");
    Check(!new NativeDresserStore(new Scanner([body], []),new()).OrdinaryAvailable,"Missing caller holds binding");
    Check(!new NativeDresserStore(new Scanner([body,body+1],[caller]),new()).OrdinaryAvailable,"Ambiguous body holds binding");
    Check(!new NativeDresserStore(new Scanner([body],[caller,caller+1]),new()).OrdinaryAvailable,"Ambiguous caller holds binding");
    Marshal.WriteInt32(caller+23,checked((int)(getter-caller-27)));
    Check(!new NativeDresserStore(scanner,new()).OrdinaryAvailable,"Disagreeing target holds binding");
    Console.WriteLine("8 production dresser binding checks passed; no game calls executed.");
}
finally { Marshal.FreeHGlobal(memory); }
static void Check(bool value,string message){if(!value)throw new Exception(message);}

sealed class Scanner(nint[] bodies,nint[] callers):Dalamud.Plugin.Services.ISigScanner
{
    public int SingleCalls;
    public nint[] ScanAllText(string signature)=>signature==NativeDresserStore.BodySignature?bodies:callers;
    public bool TryScanText(string signature,out nint result)
    {
        SingleCalls++;
        var hits=ScanAllText(signature);result=hits.FirstOrDefault();
        if(result==0)return false;
        if(Marshal.ReadByte(result) is 0xe8 or 0xe9)result+=5+Marshal.ReadInt32(result+1);
        return true;
    }
}
// Only host API shapes are stubbed. The production binding implementation is compiled above.
namespace Dalamud.Plugin.Services
{
    public interface ISigScanner
    {
        nint[] ScanAllText(string signature);
        bool TryScanText(string signature,out nint result);
    }
}
namespace Steward
{
    public sealed class Configuration
    {
        public bool ExecutionEnabled;
        public bool ExecutionReviewRequired;
        public Features Features=new();
    }
    public sealed class Features { public bool DresserCollection; }
}
namespace Steward.Core
{
    public sealed record Stock(uint ItemId,bool Hq,uint Quantity,uint Container,ushort Slot);
}
namespace FFXIVClientStructs.FFXIV.Client.Game
{
    public enum InventoryType:uint { Invalid=9999 }
    public unsafe struct MirageManager
    {
        public bool PrismBoxLoaded;
        public bool StoreNewOutfit(uint target,InventoryType* containers,ushort* slots)=>throw new Exception("Native call forbidden in binding tests");
        public bool StoreExistingOutfit(uint target,InventoryType* containers,ushort* slots)=>throw new Exception("Native call forbidden in binding tests");
    }
}
