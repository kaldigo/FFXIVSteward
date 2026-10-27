// Minimal native-view boundary for exercising the actual import service without FFXIV.
namespace Dalamud.Plugin
{
    public interface IDalamudPluginInterface { string GetPluginConfigDirectory(); }
}
namespace Steward.Services
{
    public sealed class InventoryView
    {
        public HashSet<uint> NativeReferences { get; set; }=[];
        public bool NativeReferencesKnown { get; set; }
        public bool NativeReferencesCached { get; set; }
        public bool NativeReferencesAvailable=>NativeReferencesKnown||NativeReferencesCached;
        public Steward.Core.Evidence Evidence { get; }=new();
        public uint World { get; set; }=83;
    }
}
