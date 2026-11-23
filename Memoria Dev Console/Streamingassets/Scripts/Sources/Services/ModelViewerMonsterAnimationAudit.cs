using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Memoria.Assets;
using Memoria.Prime;

namespace Memoria.DevConsole
{
    internal sealed class MonsterAnimationAuditResult
    {
        internal String Csv, Summary;
    }

    internal static class ModelViewerMonsterAnimationAudit
    {
        private static String Cell(String value) { return "\"" + value.Replace("\"", "\"\"") + "\""; }

        internal static MonsterAnimationAuditResult Build(
            IEnumerable<KeyValuePair<Int32, String>> models,
            IEnumerable<KeyValuePair<Int32, String>> animations,
            IDictionary<String, String> mappings, HashSet<String> viewerNames)
        {
            var database = animations.ToList();
            var monsters = models.Where(model => model.Value.StartsWith("GEO_MON_", StringComparison.Ordinal))
                .GroupBy(model => model.Value).OrderBy(group => group.Key, StringComparer.Ordinal).ToList();
            var covered = new HashSet<String>(StringComparer.Ordinal);
            var extraNames = new HashSet<String>(StringComparer.Ordinal);
            var missingModels = new List<String>();
            Int32 expanded = 0;
            var csv = new StringBuilder("Model,ModelIDs,ListedInViewer,Aliases,OwnDistinctAnimations,AdditionalAliasAnimations,ExpectedDistinctAnimations\r\n");
            foreach (var monster in monsters)
            {
                String name = monster.Key;
                var aliases = ModelViewerAnimationAliases.Resolve(name, mappings);
                foreach (String alias in aliases) covered.Add(alias);
                var own = database.Where(clip => ModelViewerAnimationAliases.AnimationModel(clip.Value) == name).ToList();
                var ownNames = new HashSet<String>(own.Select(clip => clip.Value), StringComparer.Ordinal);
                var merged = ModelViewerAnimationAliases.Merge(own, database, aliases, clip => true);
                Int32 additional = merged.Count - ownNames.Count;
                if (additional > 0) expanded++;
                foreach (var clip in merged) if (!ownNames.Contains(clip.Value)) extraNames.Add(clip.Value);
                Boolean listed = viewerNames.Contains(name);
                if (!listed) missingModels.Add(name);
                csv.AppendLine(Cell(name) + "," + Cell(String.Join("|", monster.Select(model => model.Key.ToString(CultureInfo.InvariantCulture)).ToArray())) +
                    "," + listed.ToString() + "," + Cell(String.Join("|", aliases.OrderBy(alias => alias, StringComparer.Ordinal).ToArray())) +
                    "," + ownNames.Count + "," + additional + "," + merged.Count);
            }
            var orphaned = database.Select(clip => ModelViewerAnimationAliases.AnimationModel(clip.Value))
                .Where(name => name != null && name.StartsWith("GEO_MON_", StringComparison.Ordinal) && !covered.Contains(name))
                .Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
            return new MonsterAnimationAuditResult {
                Csv = csv.ToString(),
                Summary = "Registered distinct monster models: " + monsters.Count + "\r\n" +
                    "Missing monster model entries in viewer: " + missingModels.Count + "\r\n" +
                    "Monster models with additional alias animations: " + expanded + "\r\n" +
                    "Distinct animation names exposed through aliases: " + extraNames.Count + "\r\n" +
                    "Uncovered monster animation prefixes: " + orphaned.Count + "\r\n" +
                    "Missing model entries: " + String.Join(", ", missingModels.ToArray()) + "\r\n" +
                    "Uncovered prefixes: " + String.Join(", ", orphaned.ToArray()) + "\r\n\r\n" +
                    "Registry coverage audit. Clip counts are expected names, not a bulk asset-loading test. " +
                    "MDC checks additional clips actually load when that model is selected.\r\n"
            };
        }
    }

    public sealed partial class ModelViewerService
    {
        private Boolean _monsterAnimationAuditWritten;

        private void WriteMonsterAnimationAudit()
        {
            if (_monsterAnimationAuditWritten) return;
            _monsterAnimationAuditWritten = true;
            try
            {
                IList catalog = GetStaticFieldValue(typeof(ModelViewerScene), "geoList") as IList;
                if (catalog == null) { _monsterAnimationAuditWritten = false; return; }
                var names = new HashSet<String>(StringComparer.Ordinal);
                foreach (System.Object model in catalog)
                {
                    FieldInfo name = model.GetType().GetField("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (name != null) names.Add((String)name.GetValue(model));
                }
                // Copy game-owned collections on Unity's thread. The expensive
                // coverage scan and file writes use these detached snapshots only.
                var models = FF9BattleDB.GEO.ToArray();
                var animations = FF9DBAll.AnimationDB.ToArray();
                var mappings = new Dictionary<String, String>(ModelFactory.upscaleTable);
                String folder = Path.Combine(Environment.CurrentDirectory, "Memoria Dev Console/Asset Dump/ModelViewer_AnimationAudit");
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var report = ModelViewerMonsterAnimationAudit.Build(models, animations, mappings, names);
                        Directory.CreateDirectory(folder);
                        File.WriteAllText(Path.Combine(folder, "MonsterAnimationCoverage.csv"), report.Csv, new UTF8Encoding(false));
                        File.WriteAllText(Path.Combine(folder, "Summary.txt"), report.Summary, new UTF8Encoding(false));
                    }
                    catch (Exception ex) { ViewerDiagnostics.Warning("[Dev Console][Model Viewer] Monster coverage audit unavailable: " + ex.Message); }
                });
            }
            catch (Exception ex) { ViewerDiagnostics.Warning("[Dev Console][Model Viewer] Monster coverage audit unavailable: " + ex.Message); }
        }
    }
}
