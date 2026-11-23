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
        private readonly Object _previewLock = new Object();
        private Int32 _previewGeneration;
        private DevSoundEntry _pendingPreviewEntry;
        private SoundProfile _readyPreviewProfile;
        private Boolean _previewLoadFinished;
        private SoundProfile _activePreviewProfile;
        private readonly Dictionary<String, String> _aliases = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

        private static String AliasPath
        {
            get { return Path.Combine(Path.Combine(Environment.CurrentDirectory, "Memoria Dev Console/FF9_Data/SoundData"), "SoundAliases.txt"); }
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
                    DevSoundEntry sound = CreateSfxEntry(pair.Key, i, runtimeSlot, pair.Value[i], false);
                    // Preserve the source filename/slot for accurate playback, but
                    // expose the named battle effect in the searchable entry identity.
                    // The same entry is shared by SFX Groups and Battle Effect Sounds.
                    sound.Identity = group.Name + " — sound " + (i + 1);
                    group.Sounds.Add(sound);
                }

                catalog.SfxGroups.Add(group);
            }

            BuildFieldGroups(catalog);
            ClassifySoundEffects(catalog);
            AddEmbeddedBattleEffects(catalog);

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
                        return BeginEmbeddedPreview(entry);
                }

                return "Playing " + BuildEntryTitle(entry) + ".";
            }
            catch (Exception ex)
            {
                return "Preview failed: " + ex.Message;
            }
        }

        private String BeginEmbeddedPreview(DevSoundEntry entry)
        {
            if (String.IsNullOrEmpty(entry.Path))
                return "Preview failed: selected sound has no resource path.";
            Int32 generation;
            lock (_previewLock)
            {
                generation = _previewGeneration;
                _pendingPreviewEntry = entry;
                _readyPreviewProfile = null;
                _previewLoadFinished = false;
            }
            // Load the exact selected resource, without replacing the game's SFX bank.
            // Loader callbacks may arrive later: only the newest request can play.
            SoundLib.LoadSoundEffect(entry.Path, delegate(SoundProfile profile, SoundDatabase database)
            {
                lock (_previewLock)
                {
                    if (generation != _previewGeneration) return;
                    _readyPreviewProfile = profile;
                    _previewLoadFinished = true;
                }
            }, 1f, 0f, 1f);
            return UpdatePreview() ?? ("Loading " + BuildEntryTitle(entry) + "; playback will start automatically.");
        }

        public String UpdatePreview()
        {
            DevSoundEntry entry;
            SoundProfile profile;
            lock (_previewLock)
            {
                if (_pendingPreviewEntry == null || !_previewLoadFinished) return null;
                entry = _pendingPreviewEntry;
                profile = _readyPreviewProfile;
                _pendingPreviewEntry = null;
                _readyPreviewProfile = null;
                _previewLoadFinished = false;
            }
            if (profile == null || profile.BankID == 0)
                return "Preview failed: could not load " + BuildEntryTitle(entry) + ".";
            try
            {
                SoundPlayer.StaticCreateSound(profile);
                SoundPlayer.StaticStartSound(profile, SoundLib.SoundEffectPlayer.Volume);
                if (profile.SoundID == 0)
                    return "Preview failed: could not start " + BuildEntryTitle(entry) + ".";
                _activePreviewProfile = profile;
                return "Playing " + BuildEntryTitle(entry) + ".";
            }
            catch (Exception ex) { return "Preview failed: " + ex.Message; }
        }

        public String StopAll()
        {
            lock (_previewLock)
            {
                _previewGeneration++;
                _pendingPreviewEntry = null;
                _readyPreviewProfile = null;
                _previewLoadFinished = false;
            }
            try
            {
                if (_activePreviewProfile != null)
                {
                    SoundPlayer.StaticStopSound(_activePreviewProfile);
                    _activePreviewProfile = null;
                }
                SoundLib.StopAllSfxSound();
                SoundLib.StopAllSoundEffects();
                SoundLib.StopAllSongs();
                SoundLib.StopMusic(0);

                if (!String.IsNullOrEmpty(_lastMovieName))
                {
                    if (SoundLib.MovieAudioPlayer != null)
                        SoundLib.MovieAudioPlayer.StopMusic(0);
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
            entry.Path = path ?? String.Empty;
            entry.Id = FindOrdinarySoundEffectId(entry.Path);
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

        // Embedded battle effect audio lives in the SFX-bank namespace, not the
        // ordinary SoundEffectIndex. Expose the existing entries in this view
        // without changing their category, bank/slot identity or preview route.
        private static void AddEmbeddedBattleEffects(DevSoundCatalog catalog)
        {
            HashSet<String> seen = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            foreach (DevSoundEntry entry in catalog.BattleEffectSounds)
                seen.Add(entry.Category + ":" + entry.Id);

            foreach (DevSfxSoundGroup group in catalog.SfxGroups)
            {
                foreach (DevSoundEntry entry in group.Sounds)
                {
                    String key = "SFX:" + entry.SfxId + ":" + entry.LocalSlot;
                    if (seen.Add(key))
                        catalog.BattleEffectSounds.Add(entry);
                }
            }

            foreach (DevSoundEntry entry in catalog.ResidentSfxSounds)
            {
                String key = "RESIDENT:" + entry.RuntimeSlot;
                if (seen.Add(key))
                    catalog.BattleEffectSounds.Add(entry);
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

// Viewer-style front end. The existing service owns catalog, playback and alias persistence.
namespace Memoria.DevConsole
{
    internal sealed class SoundBrowserView : IDisposable
    {
        private readonly SoundBrowserService _service;
        private readonly DevSoundCatalog _catalog;
        private readonly ViewerHoverHelp _help = new ViewerHoverHelp();
        private readonly List<DevSoundEntry> _entries = new List<DevSoundEntry>();
        private readonly List<DevFieldSoundGroup> _fields = new List<DevFieldSoundGroup>();
        private readonly List<DevSfxSoundGroup> _banks = new List<DevSfxSoundGroup>();
        private UnityEngine.Vector2 _scroll, _categoryScroll, _detailScroll;
        private UnityEngine.GUISkin _skin;
        private UnityEngine.GUIStyle _categoryRow, _selectedCategoryRow;
        private UnityEngine.GUIStyle _row, _selectedRow, _soundRow, _selectedSoundRow, _heading;
        private UnityEngine.Texture2D _selectedTexture;
        private readonly Boolean _savedCursor;
        private readonly UnityEngine.CursorLockMode _savedLock;
        private Int32 _category, _selected;
        private String _filter = "", _alias = "";
        private DevFieldSoundGroup _field;
        private DevSfxSoundGroup _bank;
        private DevSoundEntry _aliasEntry;
        private Boolean _reveal;
        private Boolean _categoryFocus = true;
        private Int32[] _categoryCounts;
        internal Boolean ExitRequested;
        internal String Status = "Choose a category, then select a sound to preview.";
        private static readonly String[] Categories = {
            "Field Sound Effects", "Enemy Battle Sounds", "Player Attack Sounds",
            "Battle Effect Sounds", "Known Other Sounds", "Music",
            "Song Samples / Cues", "Movie Audio", "SFX Sound Banks"
        };
        private static readonly String[] CategoryHelp = {
            "Browse vanilla field locations, then the sound effects referenced by each field.",
            "Enemy attack, appearance and death sounds identified by the battle catalog.",
            "Character attacks, weapons, swings and impacts identified by the battle catalog.",
            "Named battle effects, including embedded SFX sounds and resident sounds.",
            "Other sound effects with an identified name.",
            "Browse and preview background music by ID, name or known usage.",
            "Samples and cues played with SoundType=Song. These are separate from background music.",
            "Audio resources associated with movies.",
            "Browse effects by SFX ID, then audition their individual embedded sound files."
        };

        internal SoundBrowserView(SoundBrowserService service, DevSoundCatalog catalog)
        {
            _service = service; _catalog = catalog;
            _categoryCounts=new[] { catalog.FieldSoundGroups.Sum(g=>g.Sounds.Count),catalog.EnemyBattleSounds.Count,catalog.PlayerAttackSounds.Count,catalog.BattleEffectSounds.Count,catalog.KnownSoundEffects.Count,catalog.Music.Count,catalog.Songs.Count,catalog.MovieAudio.Count,catalog.SfxGroups.Sum(g=>g.Sounds.Count) };
            _savedCursor = UnityEngine.Cursor.visible;
            _savedLock = UnityEngine.Cursor.lockState;
            Refresh();
        }

        internal void Update()
        {
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.None;
        }

        private Boolean Groups { get { return (_category == 0 && _field == null) || (_category == 8 && _bank == null); } }
        private Int32 Count { get { return Groups ? (_category == 0 ? _fields.Count : _banks.Count) : _entries.Count; } }
        private DevSoundEntry Selected { get { return !Groups && _selected >= 0 && _selected < _entries.Count ? _entries[_selected] : null; } }

        private void Refresh()
        {
            _entries.Clear(); _fields.Clear(); _banks.Clear();
            String query = _filter.Trim().ToLowerInvariant();
            if (_category == 0 && _field == null)
                _fields.AddRange(_catalog.FieldSoundGroups.Where(g => g.SearchText.Contains(query)));
            else if (_category == 8 && _bank == null)
                _banks.AddRange(_catalog.SfxGroups.Where(g => g.SearchText.Contains(query)));
            else
            {
                IEnumerable<DevSoundEntry> source;
                switch (_category)
                {
                    case 0: source = _field.Sounds; break;
                    case 1: source = _catalog.EnemyBattleSounds; break;
                    case 2: source = _catalog.PlayerAttackSounds; break;
                    case 3: source = _catalog.BattleEffectSounds; break;
                    case 4: source = _catalog.KnownSoundEffects; break;
                    case 5: source = _catalog.Music; break;
                    case 6: source = _catalog.Songs; break;
                    case 7: source = _catalog.MovieAudio; break;
                    default: source = _bank.Sounds; break;
                }
                _entries.AddRange(source.Where(e => e.SearchText.Contains(query)));
            }
            _selected = 0; _scroll = UnityEngine.Vector2.zero; _detailScroll = UnityEngine.Vector2.zero;
            _aliasEntry = null;
        }

        private void Activate()
        {
            if (Count == 0) return;
            if (Groups)
            {
                if (_category == 0) _field = _fields[_selected];
                else _bank = _banks[_selected];
                _filter = ""; Refresh();
            }
            else Status = _service.Preview(Selected);
        }

        private void Back()
        {
            if (!_categoryFocus)
            {
                _categoryFocus = true;
                _field = null; _bank = null; _filter = ""; Refresh();
            }
            else ExitRequested = true;
        }
        private void Keyboard()
        {
            UnityEngine.Event ev = UnityEngine.Event.current;
            if (ev.type != UnityEngine.EventType.KeyDown) return;
            String focused = UnityEngine.GUI.GetNameOfFocusedControl();
            if (ev.keyCode == UnityEngine.KeyCode.Escape)
            {
                if (focused == "sound-search" || focused == "sound-alias") UnityEngine.GUI.FocusControl(null);
                else Back();
                ev.Use(); return;
            }
            if (focused == "sound-search" || focused == "sound-alias") return;
            if (_categoryFocus)
            {
                if (ev.keyCode == UnityEngine.KeyCode.UpArrow || ev.keyCode == UnityEngine.KeyCode.DownArrow)
                {
                    _category = (_category + (ev.keyCode == UnityEngine.KeyCode.UpArrow ? Categories.Length-1 : 1)) % Categories.Length;
                    _field = null; _bank = null; _filter = ""; Refresh();
                    _categoryScroll.y = Math.Max(0,_category*65-100);
                    ev.Use(); return;
                }
                if (ev.keyCode == UnityEngine.KeyCode.Return || ev.keyCode == UnityEngine.KeyCode.Space)
                { _categoryFocus = false; _reveal = true; ev.Use(); return; }
                return;
            }            Int32 step = 0;
            if (ev.keyCode == UnityEngine.KeyCode.UpArrow) step = -1;
            if (ev.keyCode == UnityEngine.KeyCode.DownArrow) step = 1;
            if (ev.keyCode == UnityEngine.KeyCode.LeftArrow || ev.keyCode == UnityEngine.KeyCode.PageUp) step = -12;
            if (ev.keyCode == UnityEngine.KeyCode.RightArrow || ev.keyCode == UnityEngine.KeyCode.PageDown) step = 12;
            if (step != 0)
            {
                if (Count > 0)
                {
                    _selected = ((_selected + step) % Count + Count) % Count;
                    _reveal = true;
                    // Preview stops existing playback and cancels pending loads first.
                    if (!Groups && (ev.keyCode == UnityEngine.KeyCode.UpArrow || ev.keyCode == UnityEngine.KeyCode.DownArrow))
                        Status = _service.Preview(Selected);
                }
                ev.Use();
            }
            else if (ev.keyCode == UnityEngine.KeyCode.Return || ev.keyCode == UnityEngine.KeyCode.Space) { Activate(); ev.Use(); }
            else if (ev.keyCode == UnityEngine.KeyCode.S || ev.keyCode == UnityEngine.KeyCode.X) { Status = _service.StopAll(); ev.Use(); }
            else if (ev.keyCode == UnityEngine.KeyCode.F) { UnityEngine.GUI.FocusControl("sound-search"); ev.Use(); }
            else if (ev.keyCode == UnityEngine.KeyCode.A && Selected != null) { UnityEngine.GUI.FocusControl("sound-alias"); ev.Use(); }
            else if (ev.keyCode == UnityEngine.KeyCode.C && (ev.control || ev.command)) { Copy(); ev.Use(); }
        }

        private void Copy()
        {
            if (Selected == null) return;
            UnityEngine.GUIUtility.systemCopyBuffer = Selected.SequenceCall;
            Status = "Copied sequence command: " + Selected.SequenceCall;
        }

        private void EnsureStyle()
        {
            if (_skin != null) return;
            _skin = UnityEngine.Object.Instantiate(UnityEngine.GUI.skin);
            foreach (UnityEngine.GUIStyle style in new[] { _skin.label, _skin.button, _skin.textField, _skin.box })
            { style.fontSize = 19; style.normal.textColor = UnityEngine.Color.white; }
            _skin.label.wordWrap = true;
            _skin.button.padding = new UnityEngine.RectOffset(12, 12, 7, 7);
            _skin.button.normal.textColor = ConsoleTheme.UnityAccent;
            _skin.button.hover.textColor = UnityEngine.Color.white;
            _skin.button.active.textColor = UnityEngine.Color.white;
            _row = new UnityEngine.GUIStyle(_skin.button) { alignment = UnityEngine.TextAnchor.MiddleLeft, clipping = UnityEngine.TextClipping.Clip, wordWrap = false, margin = new UnityEngine.RectOffset(0, 0, 0, 0) };
            _selectedTexture = new UnityEngine.Texture2D(1, 1);
            _selectedTexture.SetPixel(0, 0, ConsoleTheme.UnityBackground);
            _selectedTexture.Apply();
            _selectedRow = new UnityEngine.GUIStyle(_row);
            _selectedRow.normal.background = _selectedTexture;
            _selectedRow.hover.background = _selectedTexture;
            _selectedRow.active.background = _selectedTexture;
            _selectedRow.normal.textColor = ConsoleTheme.UnityAccent;
            _categoryRow = new UnityEngine.GUIStyle(_row) { wordWrap = true };
            _selectedCategoryRow = new UnityEngine.GUIStyle(_selectedRow) { wordWrap = true };
            _soundRow = new UnityEngine.GUIStyle(_row);
            _selectedSoundRow = new UnityEngine.GUIStyle(_selectedRow);
            foreach (UnityEngine.GUIStyle soundStyle in new[] { _soundRow, _selectedSoundRow })
            {
                soundStyle.normal.textColor = UnityEngine.Color.white;
                soundStyle.hover.textColor = UnityEngine.Color.white;
                soundStyle.active.textColor = UnityEngine.Color.white;
                soundStyle.focused.textColor = UnityEngine.Color.white;
            }
            _heading = new UnityEngine.GUIStyle(_skin.label) { fontSize = 21, fontStyle = UnityEngine.FontStyle.Bold };
            _heading.normal.textColor = ConsoleTheme.UnityAccent;
        }

        private Boolean Button(String text, String help, params UnityEngine.GUILayoutOption[] options)
        { return UnityEngine.GUILayout.Button(new UnityEngine.GUIContent(text, help), options); }

        internal void Draw()
        {
            EnsureStyle();
            UnityEngine.GUISkin previous = UnityEngine.GUI.skin;
            Int32 depth = UnityEngine.GUI.depth;
            UnityEngine.Color color = UnityEngine.GUI.color;
            try
            {
                UnityEngine.GUI.skin = _skin; UnityEngine.GUI.depth = -10000;
                UnityEngine.GUI.color = new UnityEngine.Color(.025f, .03f, .035f, 1);
                UnityEngine.GUI.DrawTexture(new UnityEngine.Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), UnityEngine.Texture2D.whiteTexture);
                UnityEngine.GUI.color = UnityEngine.Color.white;
                Keyboard();
                float width = UnityEngine.Screen.width, height = UnityEngine.Screen.height;
                float left = Math.Min(270, width * .22f), right = Math.Min(430, width * .32f);
                float middleX = left + 28, middleWidth = width - left - right - 56;
                UnityEngine.GUILayout.BeginArea(new UnityEngine.Rect(16, 12, width - 32, 48));
                UnityEngine.GUILayout.BeginHorizontal();
                UnityEngine.GUILayout.Label("SOUND BROWSER", _heading);
                if (Button("Back to MDC Menu  [Esc]", "Stop preview audio and return to the main MDC menu.", UnityEngine.GUILayout.Width(Math.Min(330, width * .4f))))
                    ExitRequested = true;
                UnityEngine.GUILayout.EndHorizontal();
                UnityEngine.GUILayout.EndArea();

                UnityEngine.GUILayout.BeginArea(new UnityEngine.Rect(16, 70, left, Math.Max(100, height - 86)));
                UnityEngine.GUILayout.Label(_categoryFocus ? "AUDIO CATEGORIES [Arrows]" : "AUDIO CATEGORIES", _heading);
                UnityEngine.GUILayout.Space(12);
                _categoryScroll = UnityEngine.GUILayout.BeginScrollView(_categoryScroll);
                for (Int32 i = 0; i < Categories.Length; i++)
                {
                    if (UnityEngine.GUILayout.Button(new UnityEngine.GUIContent(Categories[i]+" ["+_categoryCounts[i]+"]", CategoryHelp[i]), i == _category ? _selectedCategoryRow : _categoryRow, UnityEngine.GUILayout.Height(60)))
                    { _categoryFocus = true; _category = i; _field = null; _bank = null; _filter = ""; UnityEngine.GUI.FocusControl(null); Refresh(); }
                    UnityEngine.GUILayout.Space(5);
                }
                UnityEngine.GUILayout.EndScrollView();
                UnityEngine.GUILayout.EndArea();

                UnityEngine.GUILayout.BeginArea(new UnityEngine.Rect(middleX, 70, middleWidth, Math.Max(100, height - 86)));
                String title = _field != null ? _field.Field : _bank != null ? "SFX " + _bank.SfxId + ": " + _bank.Name : Categories[_category];
                UnityEngine.GUILayout.Label(title, _heading, UnityEngine.GUILayout.Height(48));
                UnityEngine.GUI.SetNextControlName("sound-search");
                String filter = UnityEngine.GUILayout.TextField(_filter, UnityEngine.GUILayout.Height(34));
                ViewerHoverHelp.Last("Search this list by ID, name, resource key, alias or known usage. F focuses search. Escape leaves the text box.");
                if (filter != _filter) { _categoryFocus = false; _filter = filter; Refresh(); }
                UnityEngine.GUILayout.BeginHorizontal();
                UnityEngine.GUILayout.Label(Count + " matches");
                if ((_field != null || _bank != null) && Button("Back to groups", "Return to the field or SFX bank list.")) { _field=null; _bank=null; _filter=""; Refresh(); }
                if (Button("Clear search", "Show every entry in the current list.")) { _filter = ""; Refresh(); }
                UnityEngine.GUILayout.EndHorizontal();
                float viewport = Math.Max(40, height - 285);
                if (_reveal)
                {
                    float top = _selected * 42;
                    if (top < _scroll.y) _scroll.y = top;
                    else if (top + 42 > _scroll.y + viewport) _scroll.y = top + 42 - viewport;
                    _reveal = false;
                }
                UnityEngine.Rect listRect = UnityEngine.GUILayoutUtility.GetRect(middleWidth, viewport);
                _scroll = UnityEngine.GUI.BeginScrollView(listRect, _scroll, new UnityEngine.Rect(0, 0, Math.Max(1, middleWidth - 24), Math.Max(viewport, Count * 42)));
                if (Count == 0) UnityEngine.GUI.Label(new UnityEngine.Rect(8, 8, Math.Max(1, middleWidth - 40), 70), "No matching sounds or groups. Try a different search.");
                // Only build visible rows; large battle catalogs must not stall playback.
                Boolean openGroup = false;
                Int32 first = Math.Max(0, Math.Min(Count, (Int32)(_scroll.y / 42)));
                Int32 end = Math.Min(Count, first + (Int32)(viewport / 42) + 3);

                for (Int32 i = first; i < end; i++)
                {
                    String text, help;
                    if (Groups)
                    {
                        text = _category == 0 ? _fields[i].Field + "  (" + _fields[i].Sounds.Count + ")" : _banks[i].SfxId + ": " + _banks[i].Name + "  (" + _banks[i].Sounds.Count + ")";
                        help = text + "\nClick to open this group's sounds.";
                    }
                    else
                    {
                        DevSoundEntry entry = _entries[i];
                        text = EntryTitle(entry);
                        help = text + "\n" + entry.Path + "\nClick to select and preview. Enter or Space replays.";
                    }
                    if (UnityEngine.GUI.Button(new UnityEngine.Rect(0, i * 42, Math.Max(1, middleWidth - 24), 38), new UnityEngine.GUIContent(text, help), i == _selected ? _selectedSoundRow : _soundRow))
                    { _categoryFocus = false; _selected = i; UnityEngine.GUI.FocusControl(null); if (Groups) openGroup = true; else Activate(); }
                }
                UnityEngine.GUI.EndScrollView();
                UnityEngine.GUILayout.Label(_categoryFocus ? "Up / Down: category   Enter: open list\nEsc: return to MDC" : "Up / Down: select + play   Enter / Space: replay\nEsc: audio categories   S / X: stop");
                UnityEngine.GUILayout.EndArea();

                UnityEngine.GUILayout.BeginArea(new UnityEngine.Rect(width - right - 16, 70, right, Math.Max(100, height - 86)));
                _detailScroll = UnityEngine.GUILayout.BeginScrollView(_detailScroll);
                UnityEngine.GUILayout.Label("PLAYBACK", _heading);
                UnityEngine.GUILayout.Space(12);
                UnityEngine.GUILayout.BeginHorizontal();
                Boolean enabled = UnityEngine.GUI.enabled;
                UnityEngine.GUI.enabled = Selected != null;
                if (Button("Play / Replay", "Play the selected sound using its original sound category. Enter or Space also plays.", UnityEngine.GUILayout.Height(46))) Activate();
                UnityEngine.GUI.enabled = enabled;
                if (Button("Stop", "Stop all browser playback, including pending embedded sound loads. Shortcut: S or X.", UnityEngine.GUILayout.Height(46))) Status = _service.StopAll();
                UnityEngine.GUILayout.EndHorizontal();
                UnityEngine.GUILayout.Space(12);
                UnityEngine.GUILayout.Label(Status);
                UnityEngine.GUILayout.Space(24);
                UnityEngine.GUILayout.Label("SELECTED SOUND", _heading);
                DevSoundEntry selected = Selected;
                if (selected == null) UnityEngine.GUILayout.Label("Open a group or select a sound to see its details.");
                else
                {
                    if (_aliasEntry != selected) { _aliasEntry = selected; _alias = selected.Identity ?? ""; _detailScroll = UnityEngine.Vector2.zero; }
                    UnityEngine.GUILayout.Label(EntryTitle(selected));
                    UnityEngine.GUILayout.Space(10);
                    UnityEngine.GUILayout.Label("Resource: " + selected.Path);
                    UnityEngine.GUILayout.Label("Sound type: " + selected.SoundType);
                    if (selected.LocalSlot >= 0)
                        UnityEngine.GUILayout.Label("Bank slot: local " + selected.LocalSlot + " / runtime " + selected.RuntimeSlot);
                    if (!String.IsNullOrEmpty(selected.Usage)) UnityEngine.GUILayout.Label("Vanilla use: " + selected.Usage);
                    UnityEngine.GUILayout.Space(18);
                    UnityEngine.GUILayout.Label("SEQUENCE COMMAND", _heading);
                    UnityEngine.GUILayout.Label(selected.SequenceCall);
                    if (Button("Copy SEQ command", "Copy the PlaySound command for this sound. Shortcut: Ctrl+C when not typing.")) Copy();
                    UnityEngine.GUILayout.Space(24);
                    UnityEngine.GUILayout.Label("CUSTOM NAME", _heading);
                    UnityEngine.GUI.SetNextControlName("sound-alias");
                    _alias = UnityEngine.GUILayout.TextField(_alias, UnityEngine.GUILayout.Height(34));
                    ViewerHoverHelp.Last("Give this sound a recognizable name. Saving writes SoundAliases.txt; it does not rename audio files. Clear the name and save to remove your alias.");
                    if (Button("Save name", "Save this custom name. An empty name removes the alias."))
                        Status = _service.SaveAlias(selected, _alias);
                }
                UnityEngine.GUILayout.EndScrollView();
                UnityEngine.GUILayout.EndArea();
                _help.Draw();
                if (openGroup) Activate();
            }
            finally { UnityEngine.GUI.skin = previous; UnityEngine.GUI.depth = depth; UnityEngine.GUI.color = color; }
        }

        private static String EntryTitle(DevSoundEntry entry)
        {
            String id = entry.Category == DevSoundCategory.SfxSound ? "SFX " + entry.SfxId + " / " + entry.RuntimeSlot :
                entry.Category == DevSoundCategory.ResidentSfxSound ? "Resident " + entry.RuntimeSlot : entry.Id.ToString();
            return id + ": " + (String.IsNullOrEmpty(entry.Identity) ? entry.Name : entry.Identity);
        }

        public void Dispose()
        {
            _service.StopAll();
            UnityEngine.Cursor.visible = _savedCursor; UnityEngine.Cursor.lockState = _savedLock;
            if (_skin != null) UnityEngine.Object.Destroy(_skin);
            if (_selectedTexture != null) UnityEngine.Object.Destroy(_selectedTexture);
        }
    }
}
