using Dalamud.Configuration;
using Dalamud.Plugin;

namespace QuestNav
{
    public sealed class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

        public void Save() => pluginInterface?.SavePluginConfig(this);
    }
}
