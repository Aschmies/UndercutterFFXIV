using Dalamud.Configuration;
using Dalamud.Plugin;

namespace QuestNav
{
    public sealed class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        // Arrow overlay
        public bool ShowArrow { get; set; } = true;
        public float ArrowBgOpacity { get; set; } = 0.55f;  // 0 = fully transparent, 1 = opaque
        public uint NavQuestId { get; set; } = 0;  // 0 = no target selected

        // Quest list settings
        public bool CompactMode { get; set; } = false;
        public bool AutoRefresh { get; set; } = true;

        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

        public void Save() => pluginInterface?.SavePluginConfig(this);
    }
}
