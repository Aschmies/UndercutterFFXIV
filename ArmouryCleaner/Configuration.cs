using Dalamud.Configuration;
using Dalamud.Plugin;
using System.Collections.Generic;

namespace ArmouryCleaner
{
    public sealed class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        // ── Filter settings ────────────────────────────────────────────────────────
        // Jobs: ClassJob abbreviation strings (e.g. "PLD", "WAR"). Empty = all jobs.
        public List<string> SelectedJobs { get; set; } = new();

        // Level range to delete (inclusive). Items WITHIN this range are candidates.
        public int MinLevel { get; set; } = 1;
        public int MaxLevel { get; set; } = 100;

        // Item level range to delete (inclusive). 0 = disabled (no iLvl filter).
        public bool FilterByIlvl { get; set; } = false;
        public int MinIlvl { get; set; } = 0;
        public int MaxIlvl { get; set; } = 999;

        // Only delete items that are NOT high quality
        public bool SkipHighQuality { get; set; } = true;

        // Only delete items that are NOT unique/untradeable (extra safety)
        public bool SkipUntradeable { get; set; } = false;

        // Skip by rarity colour (default: skip nothing)
        public bool SkipWhite  { get; set; } = false;
        public bool SkipGreen  { get; set; } = false;
        public bool SkipBlue   { get; set; } = false;
        public bool SkipPurple { get; set; } = false;
        public bool SkipPink   { get; set; } = false;

        // ── Safety ─────────────────────────────────────────────────────────────────
        // Require explicit confirmation before any deletion happens
        public bool RequireConfirmation { get; set; } = true;

        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

        public void Save() => pluginInterface?.SavePluginConfig(this);
    }
}
