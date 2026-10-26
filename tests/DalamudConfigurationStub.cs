// Compile the real persisted settings model without loading the game-only Dalamud assembly.
namespace Dalamud.Configuration;
public interface IPluginConfiguration { int Version { get; set; } }
