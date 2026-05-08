using System;
using System.Collections.Generic;
using System.Linq;

namespace UndercutterFFXIV.Services
{
    internal static class WorldDataCatalog
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> WorldsByDataCenter =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Aether"] = new[] { "Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren" },
                ["Primal"] = new[] { "Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros" },
                ["Crystal"] = new[] { "Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera" },
                ["Dynamis"] = new[] { "Cuchulainn", "Golem", "Halicarnassus", "Kraken", "Maduin", "Marilith", "Rafflesia", "Seraph" },

                ["Chaos"] = new[] { "Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan" },
                ["Light"] = new[] { "Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark" },

                ["Elemental"] = new[] { "Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Tonberry", "Typhon" },
                ["Gaia"] = new[] { "Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima" },
                ["Mana"] = new[] { "Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan" },
                ["Meteor"] = new[] { "Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus" },

                ["Materia"] = new[] { "Bismarck", "Ravana", "Sephirot", "Sophia", "Zurvan" }
            };

        public static IReadOnlyList<string> GetDataCenters()
        {
            return WorldsByDataCenter.Keys
                .OrderBy(dc => dc, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<string> GetWorlds(string dataCenter)
        {
            if (string.IsNullOrWhiteSpace(dataCenter))
                return Array.Empty<string>();

            return WorldsByDataCenter.TryGetValue(dataCenter, out var worlds)
                ? worlds
                : Array.Empty<string>();
        }

        public static bool IsWorldInDataCenter(string dataCenter, string world)
        {
            if (string.IsNullOrWhiteSpace(dataCenter) || string.IsNullOrWhiteSpace(world))
                return false;

            return GetWorlds(dataCenter).Any(w => string.Equals(w, world, StringComparison.OrdinalIgnoreCase));
        }
    }
}
