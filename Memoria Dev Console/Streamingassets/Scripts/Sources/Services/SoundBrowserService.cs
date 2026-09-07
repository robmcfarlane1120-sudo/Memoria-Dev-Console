using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Memoria.Assets;
using Memoria.Data;

namespace Memoria.DevConsole
{
    public enum DevSoundCategory
    {
        SoundEffect,
        Music,
        Song,
        SfxSound,
        ResidentSfxSound,
        MovieAudio
    }

    public sealed class DevSoundEntry
    {
        public DevSoundCategory Category;
        public Int32 Id;
        public String Path;
        public String Name;
        public String Identity;
        public String Usage;
        public Int32 SfxId = -1;
        public Int32 LocalSlot = -1;
        public Int32 RuntimeSlot = -1;
        public Boolean Resident;
        public String AliasKey;
        public String SoundType
        {
            get
            {
                switch (Category)
                {
                    case DevSoundCategory.Music: return "Music";
                    case DevSoundCategory.Song: return "Song";
                    case DevSoundCategory.MovieAudio: return "MovieAudio";
                    case DevSoundCategory.SfxSound:
                    case DevSoundCategory.ResidentSfxSound: return "SoundEffect";
                    default: return "SoundEffect";
                }
            }
        }

        public String SequenceCall
        {
            get
            {
                if ((Category == DevSoundCategory.SfxSound || Category == DevSoundCategory.ResidentSfxSound) && Id < 0)
                    return "PlaySound: Sound=" + Path + " ; SoundType=Default";
                if (Category == DevSoundCategory.MovieAudio)
                    return "PlaySound: Sound=" + Name + " ; SoundType=MovieAudio";
                return "PlaySound: Sound=" + Id + " ; SoundType=" + SoundType;
            }
        }

        public String SearchText
        {
            get
            {
                return ((Id.ToString() + " " + Path + " " + Name + " " + Identity + " " + Usage + " " + SoundType) ?? String.Empty).ToLowerInvariant();
            }
        }
    }

    public sealed class DevFieldSoundGroup
    {
        public String Field;
        public readonly List<DevSoundEntry> Sounds = new List<DevSoundEntry>();

        public String SearchText
        {
            get { return (Field ?? String.Empty).ToLowerInvariant(); }
        }
    }

    public sealed class DevSfxSoundGroup
    {
        public Int32 SfxId;
        public String Name;
        public readonly List<DevSoundEntry> Sounds = new List<DevSoundEntry>();

        public String SearchText
        {
            get { return (SfxId + " " + (Name ?? String.Empty)).ToLowerInvariant(); }
        }
    }

    public sealed class DevSoundCatalog
    {
        public readonly List<DevSoundEntry> SoundEffects = new List<DevSoundEntry>();
        public readonly List<DevSoundEntry> EnemyBattleSounds = new List<DevSoundEntry>();
        public readonly List<DevSoundEntry> PlayerAttackSounds = new List<DevSoundEntry>();
        public readonly List<DevSoundEntry> BattleEffectSounds = new List<DevSoundEntry>();
        public readonly List<DevSoundEntry> KnownSoundEffects = new List<DevSoundEntry>();
        public readonly List<DevSoundEntry> UnknownSoundEffects = new List<DevSoundEntry>();
        public readonly List<DevSoundEntry> Music = new List<DevSoundEntry>();
        public readonly List<DevSoundEntry> Songs = new List<DevSoundEntry>();
        public readonly List<DevSoundEntry> MovieAudio = new List<DevSoundEntry>();
        public readonly List<DevSoundEntry> ResidentSfxSounds = new List<DevSoundEntry>();
        public readonly List<DevSfxSoundGroup> SfxGroups = new List<DevSfxSoundGroup>();
        public readonly List<DevFieldSoundGroup> FieldSoundGroups = new List<DevFieldSoundGroup>();
    }

    public sealed class SoundBrowserService
    {
        private String _lastMovieName;
        private Int32 _loadedSfxId = -1;
        private readonly Dictionary<String, String> _aliases = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

        private static String AliasPath
        {
            get { return Path.Combine(Path.Combine(Environment.CurrentDirectory, "Memoria Dev Console"), "SoundAliases.txt"); }
        }

        public DevSoundCatalog BuildCatalog()
        {
            if (SoundMetaData.SoundEffectIndex.Count == 0 &&
                SoundMetaData.MusicIndex.Count == 0 &&
                SoundMetaData.SfxSoundIndex.Count == 0)
            {
                SoundMetaData.LoadMetaData();
            }

            DevSoundCatalog catalog = new DevSoundCatalog();

            AddStandardEntries(catalog.SoundEffects, DevSoundCategory.SoundEffect,
                SoundMetaData.SoundEffectIndex, AudioUsageCatalog.GetSoundEffectUsage);
            AddStandardEntries(catalog.Music, DevSoundCategory.Music,
                SoundMetaData.MusicIndex, AudioUsageCatalog.GetMusicUsage);
            AddStandardEntries(catalog.Songs, DevSoundCategory.Song,
                SoundMetaData.SongIndex, null);
            AddStandardEntries(catalog.MovieAudio, DevSoundCategory.MovieAudio,
                SoundMetaData.MovieAudioIndex, null);

            List<String> residentPaths;
            if (!SoundMetaData.ResidentSfxSoundIndex.TryGetValue(0, out residentPaths))
                residentPaths = new List<String>();

            for (Int32 i = 0; i < residentPaths.Count; i++)
                catalog.ResidentSfxSounds.Add(CreateSfxEntry(-1, i, i, residentPaths[i], true));

            foreach (KeyValuePair<Int32, List<String>> pair in SoundMetaData.SfxSoundIndex.OrderBy(p => p.Key))
            {
                DevSfxSoundGroup group = new DevSfxSoundGroup();
                group.SfxId = pair.Key;
                group.Name = GetSfxName(pair.Key);

                for (Int32 i = 0; i < pair.Value.Count; i++)
                {
                    Int32 runtimeSlot = residentPaths.Count + i;
                    group.Sounds.Add(CreateSfxEntry(pair.Key, i, runtimeSlot, pair.Value[i], false));
                }

                catalog.SfxGroups.Add(group);
            }

            BuildFieldGroups(catalog);
            ClassifySoundEffects(catalog);

            return catalog;
        }

        public String LoadSfxGroup(Int32 sfxId)
        {
            try
            {
                StopAll();

                if (!SoundMetaData.SfxSoundIndex.ContainsKey(sfxId))
                    return "No embedded-sound metadata exists for SFX " + sfxId + ".";

                SoundLib.LoadSfxSoundData(sfxId);
                _loadedSfxId = sfxId;
                return "Loaded SFX " + sfxId + " - " + GetSfxName(sfxId) + ". Select a sound to preview.";
            }
            catch (Exception ex)
            {
                return "Could not load SFX " + sfxId + ": " + ex.Message;
            }
        }

        public String Preview(DevSoundEntry entry)
        {
            if (entry == null)
                return "No sound is selected.";

            try
            {
                StopAll();

                switch (entry.Category)
                {
                    case DevSoundCategory.SoundEffect:
                        SoundLib.PlaySoundEffect(entry.Id, 1f, 0f, 1f);
                        break;

                    case DevSoundCategory.Music:
                        SoundLib.PlayMusic(entry.Id);
                        break;

                    case DevSoundCategory.Song:
                        SoundLib.PlaySong(entry.Id, 1f, 0f, 1f);
                        break;

                    case DevSoundCategory.MovieAudio:
                        _lastMovieName = Path.GetFileName(entry.Path);
                        SoundLib.PlayMovieMusic(_lastMovieName, 0);
                        break;

                    case DevSoundCategory.SfxSound:
                    case DevSoundCategory.ResidentSfxSound:
                        if (entry.Category == DevSoundCategory.ResidentSfxSound)
                        {
                            if (SoundLib.PlaySfxSound(entry.RuntimeSlot, 1f, 0f, 1f) == null)
                                return "That resident SFX sound is still loading. Press Preview again.";
                            break;
                        }

                        if (_loadedSfxId != entry.SfxId)
                        {
                            SoundLib.LoadSfxSoundData(entry.SfxId);
                            _loadedSfxId = entry.SfxId;
                            return "SFX " + entry.SfxId + " was still loading. Press Preview again.";
                        }

                        if (SoundLib.PlaySfxSound(entry.RuntimeSlot, 1f, 0f, 1f) == null)
                            return "That SFX sound is still loading. Press Preview again.";
                        break;
                }

                return "Playing " + BuildEntryTitle(entry) + ".";
            }
            catch (Exception ex)
            {
                return "Preview failed: " + ex.Message;
            }
        }

        public String StopAll()
        {
            try
            {
                SoundLib.StopAllSfxSound();
                SoundLib.StopAllSoundEffects();
                SoundLib.StopAllSongs();
                SoundLib.StopMusic();

                if (!String.IsNullOrEmpty(_lastMovieName))
                {
                    SoundLib.StopMovieMusic(_lastMovieName, true);
                    _lastMovieName = null;
                }

                return "Preview stopped.";
            }
            catch (Exception ex)
            {
                return "Could not stop preview: " + ex.Message;
            }
        }

        private void AddStandardEntries(
            List<DevSoundEntry> destination,
            DevSoundCategory category,
            Dictionary<Int32, String> source,
            Func<Int32, String> usageLookup)
        {
            foreach (KeyValuePair<Int32, String> pair in source.OrderBy(p => p.Key))
            {
                DevSoundEntry entry = new DevSoundEntry();
                entry.Category = category;
                entry.Id = pair.Key;
                entry.Path = pair.Value ?? String.Empty;
                entry.Name = GetResourceKey(entry.Path);
                entry.Identity = GetFriendlyIdentity(entry.Path);
                entry.Usage = usageLookup != null ? usageLookup(pair.Key) : String.Empty;
                if (category == DevSoundCategory.SoundEffect)
                {
                    String battleName = MemoriaDevConsoleBattleAudioCatalog.GetDirectSoundName(pair.Key);
                    if (!String.IsNullOrEmpty(battleName)) entry.Identity = battleName;
                    String battleUsage = MemoriaDevConsoleBattleAudioCatalog.GetDirectSoundUsage(pair.Key);
                    if (!String.IsNullOrEmpty(battleUsage))
                        entry.Usage = String.IsNullOrEmpty(entry.Usage) ? battleUsage : entry.Usage + " | " + battleUsage;
                }
                entry.AliasKey = GetAliasKey(entry);
                destination.Add(entry);
            }
        }

        private DevSoundEntry CreateSfxEntry(
            Int32 sfxId,
            Int32 localSlot,
            Int32 runtimeSlot,
            String path,
            Boolean resident)
        {
            DevSoundEntry entry = new DevSoundEntry();
            entry.Category = resident ? DevSoundCategory.ResidentSfxSound : DevSoundCategory.SfxSound;
            entry.Id = FindOrdinarySoundEffectId(entry.Path);
            entry.Path = path ?? String.Empty;
            entry.Name = GetResourceKey(entry.Path);
            entry.Identity = GetFriendlyIdentity(entry.Path);
            entry.SfxId = sfxId;
            entry.LocalSlot = localSlot;
            entry.RuntimeSlot = runtimeSlot;
            entry.Resident = resident;
            entry.AliasKey = resident ? "RESIDENT:" + runtimeSlot : "SFX:" + sfxId + ":" + runtimeSlot;
            return entry;
        }

        private static Int32 FindOrdinarySoundEffectId(String resource)
        {
            foreach (KeyValuePair<Int32, String> pair in SoundMetaData.SoundEffectIndex)
                if (String.Equals(pair.Value, resource, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            return -1;
        }

        public String SaveAlias(DevSoundEntry entry, String alias)
        {
            if (entry == null || String.IsNullOrEmpty(entry.AliasKey))
                return "No sound is selected.";

            alias = (alias ?? String.Empty).Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            if (alias.Length == 0)
                _aliases.Remove(entry.AliasKey);
            else
                _aliases[entry.AliasKey] = alias;

            try
            {
                String directory = Path.GetDirectoryName(AliasPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                List<String> lines = new List<String>();
                lines.Add("# Memoria Dev Console sound aliases. Format: key<TAB>name");
                foreach (KeyValuePair<String, String> pair in _aliases.OrderBy(p => p.Key))
                    lines.Add(pair.Key + "\t" + pair.Value);
                File.WriteAllLines(AliasPath, lines.ToArray(), new System.Text.UTF8Encoding(false));

                entry.Identity = alias.Length == 0 ? GetInferredIdentity(entry) : alias;
                return alias.Length == 0 ? "Removed the custom sound name." : "Saved custom name: " + alias;
            }
            catch (Exception ex)
            {
                return "Could not save SoundAliases.txt: " + ex.Message;
            }
        }

        private void LoadAliases()
        {
            _aliases.Clear();
            try
            {
                if (!File.Exists(AliasPath))
                    return;
                foreach (String rawLine in File.ReadAllLines(AliasPath))
                {
                    String line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;
                    Int32 separator = line.IndexOf('\t');
                    if (separator > 0 && separator < line.Length - 1)
                        _aliases[line.Substring(0, separator)] = line.Substring(separator + 1).Trim();
                }
            }
            catch
            {
            }
        }

        private static String GetAliasKey(DevSoundEntry entry)
        {
            return entry.Category.ToString().ToUpperInvariant() + ":" + entry.Id;
        }

        private void ApplyAlias(DevSoundEntry entry)
        {
            String alias;
            if (!String.IsNullOrEmpty(entry.AliasKey) && _aliases.TryGetValue(entry.AliasKey, out alias))
                entry.Identity = alias;
        }

        private static String GetInferredIdentity(DevSoundEntry entry)
        {
            if (entry.Category == DevSoundCategory.SoundEffect)
            {
                String battleName = MemoriaDevConsoleBattleAudioCatalog.GetDirectSoundName(entry.Id);
                if (!String.IsNullOrEmpty(battleName)) return battleName;
            }
            return GetFriendlyIdentity(entry.Path);
        }

        private static void ClassifySoundEffects(DevSoundCatalog catalog)
        {
            foreach (DevSoundEntry entry in catalog.SoundEffects)
            {
                String identity = (entry.Identity ?? String.Empty).ToLowerInvariant();
                String usage = (entry.Usage ?? String.Empty).ToLowerInvariant();

                Boolean enemy = MemoriaDevConsoleBattleAudioCatalog.IsEnemySound(entry.Id) ||
                    ContainsAny(identity,
                        "enemy attack", "enemy death", "enemy appearance", "enemy appear",
                        "monster attack", "monster death", "monster appearance");

                Boolean player = MemoriaDevConsoleBattleAudioCatalog.IsPlayerSound(entry.Id) ||
                    ContainsAny(identity,
                        "weapon attack", "weapon swing", "weapon impact", "player attack",
                        "character attack", "staff attack", "flute attack") ||
                    ContainsAny(usage, "weapon swing", "weapon impact");

                Boolean battleEffect = MemoriaDevConsoleBattleAudioCatalog.IsBattleEffectSound(entry.Id);

                if (enemy) catalog.EnemyBattleSounds.Add(entry);
                if (player) catalog.PlayerAttackSounds.Add(entry);
                if (battleEffect) catalog.BattleEffectSounds.Add(entry);
                if (!enemy && !player && !battleEffect)
                {
                    // A known location/reference does not identify what the sound actually is.
                    // Only an inferred or manually assigned identity belongs in Known Other.
                    if (!String.IsNullOrEmpty(entry.Identity))
                        catalog.KnownSoundEffects.Add(entry);
                    else
                        catalog.UnknownSoundEffects.Add(entry);
                }
            }
        }

        private static Boolean ContainsAny(String value, params String[] terms)
        {
            if (String.IsNullOrEmpty(value)) return false;
            foreach (String term in terms)
                if (value.Contains(term)) return true;
            return false;
        }

        private void BuildFieldGroups(DevSoundCatalog catalog)
        {
            Dictionary<String, DevFieldSoundGroup> groups = new Dictionary<String, DevFieldSoundGroup>();
            foreach (DevSoundEntry entry in catalog.SoundEffects)
            {
                foreach (String field in AudioUsageCatalog.GetSoundEffectFields(entry.Id))
                {
                    DevFieldSoundGroup group;
                    if (!groups.TryGetValue(field, out group))
                    {
                        group = new DevFieldSoundGroup();
                        group.Field = field;
                        groups.Add(field, group);
                    }
                    group.Sounds.Add(entry);
                }
            }
            catalog.FieldSoundGroups.AddRange(groups.Values.OrderBy(g => g.Field));
        }

        private static String GetResourceKey(String path)
        {
            return String.IsNullOrEmpty(path) ? "Unknown" : Path.GetFileName(path);
        }

        private static String GetFriendlyIdentity(String path)
        {
            if (String.IsNullOrEmpty(path)) return String.Empty;
            String directory;
            String file;
            String labelled;
            if (AudioResources.TryAppendDisplayName(path, out directory, out file, out labelled))
            {
                String labelledFile = Path.GetFileName(labelled);
                if (!String.IsNullOrEmpty(labelledFile))
                {
                    String rawFile = Path.GetFileName(path);
                    return String.Equals(labelledFile, rawFile, StringComparison.OrdinalIgnoreCase) ? String.Empty : labelledFile;
                }
            }
            return String.Empty;
        }

        private static String GetSfxName(Int32 sfxId)
        {
            try
            {
                String name = ((SpecialEffect)sfxId).ToString();
                return name == sfxId.ToString() ? "Unknown SFX" : name.Replace("__", " ").Replace('_', ' ');
            }
            catch
            {
                return "Unknown SFX";
            }
        }

        private static String BuildEntryTitle(DevSoundEntry entry)
        {
            if (entry.Category == DevSoundCategory.SfxSound)
                return "SFX " + entry.SfxId + " runtime sound slot " + entry.RuntimeSlot + " - " + entry.Name + FormatIdentity(entry);

            return entry.Id + " - " + entry.Name + FormatIdentity(entry);
        }

        private static String FormatIdentity(DevSoundEntry entry)
        {
            return String.IsNullOrEmpty(entry.Identity) ? String.Empty : " — " + entry.Identity;
        }
    }
}
