using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Memoria.BattleSceneLayouts;
using Memoria.BattleSceneLayouts.Json;

namespace Memoria.DevConsole
{
    internal static class BattleLayoutInstaller
    {
        private const String Data = "FF9_Data/BattleSceneLayouts";
        private const String Source = "StreamingAssets/Scripts/Sources/BattleSceneLayouts";
        private static readonly String[] Names = { "BattleLayoutData.cs", "BattleLayoutRuntime.cs", "LayoutJson.cs", "BattleLayoutBootstrap.cs" };
        internal static void IncludeCameraHooks(String modRoot)
        {
            String source = Path.GetFullPath("Memoria Dev Console/StreamingAssets/Scripts/QuickRuntime");
            String destination = Path.Combine(modRoot, "StreamingAssets/Scripts/QuickRuntime");
            foreach (String name in new[] { "0Harmony.dll", "Harmony-LICENSE.txt" })
            {
                String file = Path.Combine(destination, name);
                if (File.Exists(file)) continue;
                Directory.CreateDirectory(destination);
                File.Copy(Path.Combine(source, name), file);
            }
        }
        internal static Boolean Install(String mod, BattleLayout layout, String templates)
        {
            String target = WorldEntranceInstaller.Target(mod);
            BattleLayout.Parse(layout.ToJson().ToString());
            String owner = target;
            foreach (String active in Memoria.Configuration.Mod.FolderNames)
                if (WorldEntranceInstaller.Mods().Contains(active) && File.Exists(Path.Combine(WorldEntranceInstaller.Target(active), Source + "/BattleLayoutBootstrap.cs")))
                { owner = WorldEntranceInstaller.Target(active); break; }
            String ownerName = Path.GetFileName(owner);
            String dll = Path.Combine(owner, "StreamingAssets/Scripts/Memoria.Scripts." + ownerName + ".dll");
            String sources = Path.Combine(owner, "StreamingAssets/Scripts/Sources");
            if (File.Exists(Path.Combine(owner, "StreamingAssets/Scripts/Memoria.Scripts.dll")))
                throw new InvalidOperationException("Legacy script DLL target must be migrated before installing a runtime.");
            if (File.Exists(dll) && (!Directory.Exists(sources) || Directory.GetFiles(sources, "*.cs", SearchOption.AllDirectories).Length == 0))
                throw new InvalidOperationException("Target has a compiled DLL but no complete source tree; installing sources could discard its existing features. Export instead.");
            Dictionary<String, String> runtime = new Dictionary<String, String>();
            foreach (String name in Names) runtime[Path.Combine(owner, Source + "/" + name)] = File.ReadAllText(Path.Combine(templates, name + ".txt"));
            String sourceHash = Hash(Encoding.UTF8.GetBytes(String.Concat(Names.Select(n => runtime[Path.Combine(owner, Source + "/" + n)]).ToArray())));
            String manifestPath = Path.Combine(owner, Data + "/RuntimeInstall.json");
            JObject previous = File.Exists(manifestPath) ? JObject.Parse(File.ReadAllText(manifestPath)) : new JObject();
            Boolean changed = runtime.Any(p => !File.Exists(p.Key) || File.ReadAllText(p.Key) != p.Value);
            Boolean pending = changed || !File.Exists(dll) || ((Boolean?)previous["pendingCompile"] ?? true) || (String)previous["sourceSHA256"] != sourceHash;
            JObject manifest = new JObject(new JProperty("version", 1), new JProperty("sourceSHA256", sourceHash), new JProperty("pendingCompile", pending),
                new JProperty("baselineDLLSHA256", (Boolean?)previous["pendingCompile"] == true ? (String)previous["baselineDLLSHA256"] : File.Exists(dll) ? Hash(File.ReadAllBytes(dll)) : ""),
                new JProperty("baselineDLLWriteTicks", (Boolean?)previous["pendingCompile"] == true ? (String)previous["baselineDLLWriteTicks"] : File.Exists(dll) ? File.GetLastWriteTimeUtc(dll).Ticks.ToString(CultureInfo.InvariantCulture) : ""));
            runtime[manifestPath] = manifest.ToString(Formatting.Indented);
            runtime[Path.Combine(target, Data + "/Battle_" + layout.BattleId + "_P" + layout.Pattern + ".json")] = layout.ToJson().ToString(Formatting.Indented);
            // LC entrance installation tracks its containing DLL. Let its existing
            // compiler completion step refresh that integrity hash after this rebuild.
            String entrance = Path.Combine(owner, "FF9_Data/CustomWorldMapEntrances/WorldEntrances.runtime.json");
            if (pending && File.Exists(entrance))
            {
                JObject e = JObject.Parse(File.ReadAllText(entrance));
                if (!((Boolean?)e["pendingCompile"] ?? false))
                {
                    e["pendingCompile"] = true; e["baselineDLLSHA256"] = File.Exists(dll) ? Hash(File.ReadAllBytes(dll)) : "";
                    e["baselineDLLWriteTicks"] = File.Exists(dll) ? File.GetLastWriteTimeUtc(dll).Ticks.ToString(CultureInfo.InvariantCulture) : "";
                }
                runtime[entrance] = e.ToString(Formatting.Indented);
            }
            IncludeCameraHooks(owner);
            WriteChanges(runtime);
            return pending;
        }
        internal static void CompletePendingCompilations(Action<String> report)
        {
            foreach (String mod in WorldEntranceInstaller.Mods())
            {
                String root = WorldEntranceInstaller.Target(mod), path = Path.Combine(root, Data + "/RuntimeInstall.json");
                if (!File.Exists(path)) continue;
                JObject manifest = JObject.Parse(File.ReadAllText(path)); if (!((Boolean?)manifest["pendingCompile"] ?? false)) continue;
                String sourceHash = Hash(Encoding.UTF8.GetBytes(String.Concat(Names.Select(n => File.ReadAllText(Path.Combine(root, Source + "/" + n))).ToArray())));
                if (sourceHash != (String)manifest["sourceSHA256"]) throw new InvalidOperationException(mod + ": layout runtime sources changed; capture/install again before compiling.");
                String dll = Path.Combine(root, "StreamingAssets/Scripts/Memoria.Scripts." + mod + ".dll");
                if (!File.Exists(dll)) throw new InvalidOperationException(mod + ": layout runtime DLL missing.");
                String hash = Hash(File.ReadAllBytes(dll));
                if (hash == (String)manifest["baselineDLLSHA256"] && File.GetLastWriteTimeUtc(dll).Ticks.ToString(CultureInfo.InvariantCulture) == (String)manifest["baselineDLLWriteTicks"])
                    throw new InvalidOperationException(mod + ": layout runtime DLL was not rebuilt.");
                manifest["pendingCompile"] = false; manifest["dllSHA256"] = hash;
                File.WriteAllText(path, manifest.ToString(Formatting.Indented));
                if (report != null) report(mod + ": battle layout runtime compiled.");
            }
        }
        private static String Hash(Byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        private static void WriteChanges(Dictionary<String, String> changes)
        {
            Dictionary<String, Byte[]> originals = new Dictionary<String, Byte[]>();
            foreach (String path in changes.Keys)
                originals[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
            List<String> written = new List<String>();
            try
            {
                foreach (var entry in changes)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Key)); String temp = entry.Key + ".tmp";
                    File.WriteAllText(temp, entry.Value, new UTF8Encoding(false));
                    if (File.Exists(entry.Key)) File.Replace(temp, entry.Key, null); else File.Move(temp, entry.Key);
                    written.Add(entry.Key);
                }
            }
            catch
            {
                foreach (String path in written.AsEnumerable().Reverse())
                    if (originals[path] != null) File.WriteAllBytes(path, originals[path]); else if (File.Exists(path)) File.Delete(path);
                throw;
            }
        }
    }
}
