using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Memoria.DevConsole.EntranceJson;

namespace Memoria.DevConsole
{
    internal enum LogDisplayMode { Normal, Hidden, Cyan, Gold }
    internal sealed class FilteredLogLine
    {
        internal String Text;
        internal Boolean Cyan; internal Boolean Gold;
    }
    internal sealed class LiveLogFilters
    {
        internal const String Core = "Memoria / Unattributed";
        private const String Settings = "Memoria Dev Console/FF9_Data/LiveLogFilters.json";
        private readonly Dictionary<String, LogDisplayMode> modes = new Dictionary<String, LogDisplayMode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, HashSet<String>> tags = new Dictionary<String, HashSet<String>>(StringComparer.OrdinalIgnoreCase);
        internal readonly List<String> Sources = new List<String>();
        private static readonly Regex Tag = new Regex(@"\[([^\]\r\n]+)\]");
        private static readonly Regex Header = new Regex(@"^\d{2}\.\d{2}\.\d{4}\s.*\|[A-Z]\|",RegexOptions.IgnoreCase);
        private static readonly Regex SourceTag = new Regex("(?:Log\\.(?:Message|Warning|Error)|Debug\\.Log(?:Warning|Error)?)\\s*\\(\\s*\\$?\"\\s*\\[([^\\]\\r\\n]+)\\]");

        internal LiveLogFilters()
        {
            try
            {
                if (File.Exists(Settings))
                    foreach (JProperty property in JObject.Parse(File.ReadAllText(Settings)).Properties())
                    {
                        String value = (String)property.Value;
                        foreach (LogDisplayMode mode in Enum.GetValues(typeof(LogDisplayMode)))
                            if (String.Equals(value,mode.ToString(),StringComparison.OrdinalIgnoreCase)) modes[property.Name] = mode;
                    }
            }
            catch { /* A bad preference file must not break Live Log. */ }
        }
        internal LogDisplayMode Mode(String source)
        { LogDisplayMode mode; return modes.TryGetValue(source,out mode) ? mode : LogDisplayMode.Normal; }
        internal void Cycle(String source, Int32 delta)
        {
            modes[source] = (LogDisplayMode)(((Int32)Mode(source) + delta + 4) % 4);
            Save();
        }
        internal void Reset()
        { modes.Clear(); Save(); }
        private void Save()
        {
            var json = new JObject();
            foreach (var entry in modes) json[entry.Key] = entry.Value.ToString();
            EntranceJsonStore.Write(Path.GetFullPath(Settings),json);
        }
        private static String Normalize(String value)
        { return Regex.Replace(value.ToLowerInvariant(),@"[^a-z0-9]",""); }
        private void AddTag(String tag, String owner)
        {
            String key = Normalize(tag);
            if (key.Length < 3) return;
            HashSet<String> owners;
            if (!tags.TryGetValue(key,out owners)) tags[key] = owners = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            owners.Add(owner);
        }
        internal void RefreshSources()
        {
            Sources.Clear(); tags.Clear(); Sources.Add(Core);
            foreach (String directory in Directory.GetDirectories(Environment.CurrentDirectory).OrderBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase))
            {
                String metadata = Path.Combine(directory,"ModDescription.xml");
                if (!File.Exists(metadata)) continue;
                String owner = Path.GetFileName(directory);
                Sources.Add(owner); AddTag(owner,owner);
                try
                {
                    Match name = Regex.Match(File.ReadAllText(metadata),@"<Name>\s*([^<]+)\s*</Name>",RegexOptions.IgnoreCase);
                    if (name.Success) AddTag(name.Groups[1].Value,owner);
                    if (owner.Equals("Memoria Dev Console",StringComparison.OrdinalIgnoreCase)) { AddTag("MDC",owner); AddTag("Dev Console",owner); }
                    String source = Path.Combine(directory,"StreamingAssets/Scripts/Sources");
                    if (!Directory.Exists(source)) continue;
                    foreach (String file in Directory.GetFiles(source,"*.cs",SearchOption.AllDirectories))
                        foreach (Match match in SourceTag.Matches(File.ReadAllText(file))) AddTag(match.Groups[1].Value,owner);
                }
                catch { /* Names remain usable when optional source files cannot be read. */ }
            }
        }
        private Boolean omitViewerDiagnosticContinuation;
        private String Owner(String line, String previous)
        {
            // Untimestamped continuation lines follow the previous log entry.
            if (!Header.IsMatch(line) && !line.TrimStart().StartsWith("[",StringComparison.Ordinal)) return previous ?? Core;
            Match tag = Tag.Match(line);
            if (!tag.Success) return Core;
            HashSet<String> owners;
            return tags.TryGetValue(Normalize(tag.Groups[1].Value),out owners) && owners.Count == 1 ? owners.First() : Core;
        }
        internal List<FilteredLogLine> Filter(String text, ref String previous)
        {
            var result = new List<FilteredLogLine>();
            String normalized = text.Replace("\r\n","\n").Replace('\r','\n');
            String[] lines = normalized.Split('\n');
            for (Int32 i = 0; i < lines.Length; i++)
            {
                if (i == lines.Length - 1 && lines[i].Length == 0) break;
                // These animation catalogs are emitted by the engine, not MDC.
                // Hide only the known informational entries and their continuations.
                if (Header.IsMatch(lines[i]) || lines[i].TrimStart().StartsWith("[",StringComparison.Ordinal))
                    omitViewerDiagnosticContinuation =
                        (lines[i].Contains("[ModelViewerScene] Animation set:") || lines[i].Contains("[ModelViewerScene] Change model:")) &&
                        !lines[i].Contains("|W|") && !lines[i].Contains("|E|");
                if (omitViewerDiagnosticContinuation) continue;
                previous = Owner(lines[i],previous);
                LogDisplayMode mode = Mode(previous);
                if (mode != LogDisplayMode.Hidden) result.Add(new FilteredLogLine {Text=lines[i] + (i < lines.Length - 1 ? "\r\n" : ""),Cyan=mode == LogDisplayMode.Cyan,Gold=mode == LogDisplayMode.Gold});
            }
            return result;
        }
    }
}
