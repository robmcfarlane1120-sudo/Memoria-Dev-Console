using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Memoria.DevConsole
{
    // Runtime reader and classifier for the normalized tables produced by AudioMetaDumper.
    // Keeping the data outside the DLL lets a newer dump replace it without recompiling MDC.
    public static class MemoriaDevConsoleBattleAudioCatalog
    {
        private static readonly Dictionary<Int32, List<String>> SfxUses = new Dictionary<Int32, List<String>>();
        private static readonly Dictionary<Int32, List<String>> DirectSoundUses = new Dictionary<Int32, List<String>>();
        private static readonly HashSet<Int32> EnemySoundIds = new HashSet<Int32>();
        private static readonly HashSet<Int32> PlayerSoundIds = new HashSet<Int32>();
        private static readonly HashSet<Int32> BattleEffectSoundIds = new HashSet<Int32>();
        private static Boolean _loaded;
        private static String _status = "Battle audio identity data has not been loaded.";

        private static String DataRoot
        {
            get
            {
                String freshDump = Path.Combine(Environment.CurrentDirectory, "AudioMetaDump");
                if (File.Exists(Path.Combine(freshDump, "OrdinaryBattleSoundUsage.tsv"))) return freshDump;
                return Path.Combine(Path.Combine(Environment.CurrentDirectory, "Memoria Dev Console"), "SoundData");
            }
        }

        public static String Status { get { EnsureLoaded(); return _status; } }

        public static String GetSfxUsage(Int32 id)
        {
            EnsureLoaded();
            List<String> uses;
            return SfxUses.TryGetValue(id, out uses) ? Summarize("Battle uses", uses) : String.Empty;
        }

        public static String GetDirectSoundUsage(Int32 id)
        {
            EnsureLoaded();
            List<String> uses;
            return DirectSoundUses.TryGetValue(id, out uses) ? Summarize("Battle uses", uses) : String.Empty;
        }

        public static String GetDirectSoundName(Int32 id)
        {
            EnsureLoaded();
            List<String> uses;
            if (!DirectSoundUses.TryGetValue(id, out uses) || uses.Count == 0) return String.Empty;
            return uses[0] + (uses.Count > 1 ? " (+" + (uses.Count - 1) + " uses)" : String.Empty);
        }

        public static Boolean IsEnemySound(Int32 id)
        {
            EnsureLoaded();
            return EnemySoundIds.Contains(id);
        }

        public static Boolean IsPlayerSound(Int32 id)
        {
            EnsureLoaded();
            return PlayerSoundIds.Contains(id);
        }

        public static Boolean IsBattleEffectSound(Int32 id)
        {
            EnsureLoaded();
            return BattleEffectSoundIds.Contains(id);
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                Int32 attackRows = ReadAttacks(Path.Combine(DataRoot, "BattleAttackAudio.tsv"));
                Int32 ordinaryRows = ReadOrdinarySounds(Path.Combine(DataRoot, "OrdinaryBattleSoundUsage.tsv"));
                _status = "Identity loaded: " + attackRows + " attacks, " + ordinaryRows + " ordinary battle-sound uses, " +
                    SfxUses.Count + " SFX IDs and " + DirectSoundUses.Count + " ordinary SE IDs identified.";
            }
            catch (Exception ex)
            {
                _status = "Battle identity unavailable: " + ex.Message;
            }
        }

        private static Int32 ReadAttacks(String path)
        {
            if (!File.Exists(path)) throw new Exception("Missing SoundData/BattleAttackAudio.tsv: " + path);
            Int32 count = 0;
            foreach (String raw in File.ReadAllLines(path))
            {
                if (count++ == 0) continue;
                String[] c = raw.Split('\t');
                if (c.Length < 15) continue;
                String scene = Clean(c[1]);
                String enemy = Clean(c[3]);
                String attack = Clean(c[5]);
                String use = attack + " — " + enemy + " [" + scene + "]";

                Int32 sequenceSfx;
                if (Int32.TryParse(c[6], out sequenceSfx) && sequenceSfx >= 0)
                    Add(SfxUses, sequenceSfx, use);
                else
                {
                    Int32 tableVfx;
                    if (Int32.TryParse(c[8], out tableVfx) && tableVfx > 0)
                        Add(SfxUses, tableVfx, use + " (attack table)");
                }

            }
            return Math.Max(0, count - 1);
        }

        private static Int32 ReadOrdinarySounds(String path)
        {
            if (!File.Exists(path)) throw new Exception("Missing SoundData/OrdinaryBattleSoundUsage.tsv: " + path);
            Int32 count = 0;
            foreach (String raw in File.ReadAllLines(path))
            {
                if (count++ == 0) continue;
                String[] c = raw.Split('\t');
                if (c.Length < 9) continue;
                Int32 id;
                if (!Int32.TryParse(c[0], out id) || id < 0) continue;
                String type = Clean(c[2]);
                if (type == "enemy_death" || type == "enemy_appearance" || type == "attack_sequence_direct")
                    EnemySoundIds.Add(id);
                if (type == "weapon_swing_character" || type == "weapon_swing_model" ||
                    type == "weapon_impact_character" || type == "weapon_impact_model")
                    PlayerSoundIds.Add(id);
                if (type == "sfx_bank_member" || type == "sfx_bank_resident" ||
                    type == "sfx_sequence_sound" || type == "battle_system")
                    BattleEffectSoundIds.Add(id);
                Add(DirectSoundUses, id, FormatOrdinaryUse(c));
            }
            return Math.Max(0, count - 1);
        }

        private static String FormatOrdinaryUse(String[] c)
        {
            String type = Clean(c[2]);
            String owner = Clean(c[3]);
            String scene = Clean(c[5]);
            String attack = Clean(c[7]);
            if (type == "enemy_death") return "Enemy death — " + owner + SceneSuffix(scene);
            if (type == "enemy_appearance") return "Enemy appearance — " + owner + SceneSuffix(scene);
            if (type == "weapon_swing_character" || type == "weapon_swing_model") return "Weapon swing — " + owner;
            if (type == "weapon_impact_character" || type == "weapon_impact_model") return "Weapon impact — " + owner;
            if (type == "attack_sequence_direct") return "Attack sound — " + attack + " — " + owner + SceneSuffix(scene);
            if (type == "battle_system") return "Battle system — " + owner;
            if (type == "sfx_bank_member") return "SFX bank — " + owner + DetailSuffix(c);
            if (type == "sfx_bank_resident") return "Shared SFX bank — " + owner + DetailSuffix(c);
            if (type == "sfx_sequence_sound") return "SFX sequence — " + owner + DetailSuffix(c);
            return type.Replace('_', ' ') + " — " + owner;
        }

        private static String DetailSuffix(String[] columns)
        {
            if (columns == null || columns.Length < 9 || String.IsNullOrEmpty(columns[8])) return String.Empty;
            return " (" + columns[8] + ")";
        }

        private static String SceneSuffix(String scene)
        {
            return String.IsNullOrEmpty(scene) || scene == "Unknown" ? String.Empty : " [" + scene + "]";
        }

        private static void Add(Dictionary<Int32, List<String>> map, Int32 id, String use)
        {
            List<String> list;
            if (!map.TryGetValue(id, out list)) map.Add(id, list = new List<String>());
            if (!list.Contains(use)) list.Add(use);
        }

        private static String Clean(String value)
        {
            if (String.IsNullOrEmpty(value)) return "Unknown";
            StringBuilder result = new StringBuilder();
            Boolean inTag = false;
            foreach (Char ch in value)
            {
                if (ch == '[') { inTag = true; continue; }
                if (ch == ']' && inTag) { inTag = false; continue; }
                if (!inTag) result.Append(ch);
            }
            String clean = result.ToString().Trim();
            return clean.Length == 0 ? "Unknown" : clean;
        }

        private static String Summarize(String prefix, List<String> uses)
        {
            const Int32 shown = 6;
            String text = String.Join("; ", uses.Take(shown).ToArray());
            if (uses.Count > shown) text += "; +" + (uses.Count - shown) + " more";
            return prefix + ": " + text;
        }
    }
}
