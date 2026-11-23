using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Memoria.DevConsole.EntranceJson;

namespace Memoria.DevConsole
{
    internal static class WorldEntranceInstaller
    {
        private const String ToolRoot = "Memoria Dev Console/FF9_Data/EntranceEditor";
        private const String DataFolder = "FF9_Data/CustomWorldMapEntrances";
        private static readonly String[] Names = { "WorldEntranceRuntime.cs", "WorldEntranceData.cs", "EntranceJson.cs" };
        internal static String[] Mods()
        {
            return Directory.GetDirectories(Environment.CurrentDirectory).Where(p => File.Exists(Path.Combine(p,"ModDescription.xml")) &&
                !String.Equals(Path.GetFileName(p),"Memoria Dev Console",StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();
        }
        internal static String Target(String name)
        {
            if (!Mods().Contains(name)) throw new ArgumentException("Select an installed mod folder from the target list.");
            return Path.GetFullPath(name);
        }
        internal static Int32 TicketKey(String mod)
        {
            using (SHA256 sha = SHA256.Create())
            { Byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(mod.ToLowerInvariant())); return 0x50000000 | (BitConverter.ToInt32(hash,0) & 0x0FFFFFFF); }
        }

        // True means Compile + Restart is required. No process or DLL writes here.
        internal static Boolean Install(String mod, JObject definition)
        {
            String root = Target(mod);
            WorldEntranceEditorService.Validate(definition, false);
            String sources = Path.Combine(root,"StreamingAssets/Scripts/Sources");
            String scripts = Path.Combine(root,"StreamingAssets/Scripts");
            String dll = Path.Combine(scripts,"Memoria.Scripts." + mod + ".dll");
            if (File.Exists(Path.Combine(scripts,"Memoria.Scripts.dll")))
                throw new InvalidOperationException("Legacy Memoria.Scripts.dll target requires migration before installing generated entrances.");
            if (File.Exists(dll) && (!Directory.Exists(sources) || Directory.GetFiles(sources,"*.cs",SearchOption.AllDirectories).Length == 0))
                throw new InvalidOperationException("This mod has a compiled DLL but no source tree. Its author must provide complete sources before Compile + Restart. Export JSON instead.");
            String folder = Path.Combine(root,DataFolder), jsonPath = Path.Combine(folder,"WorldEntrances.json");
            String manifestPath = Path.Combine(folder,"WorldEntrances.runtime.json");
            JObject document = EntranceJsonStore.Read(File.Exists(jsonPath) ? jsonPath : Path.Combine(root,"WorldEntrances.json"));
            EntranceJsonStore.Merge(document,definition,(String)definition["id"]);
            String[] content = {
                File.ReadAllText(Path.Combine(ToolRoot,"Templates/Runtime.cs.txt")).Replace("__TICKET_KEY__",TicketKey(mod).ToString(CultureInfo.InvariantCulture)),
                File.ReadAllText(Path.Combine(ToolRoot,"Templates/Loader.cs.txt")),File.ReadAllText(Path.Combine(ToolRoot,"Templates/Json.cs.txt")) };
            String sourceHash = Hash(Encoding.UTF8.GetBytes(String.Concat(content)));
            String dllHash = File.Exists(dll) ? Hash(File.ReadAllBytes(dll)) : "";
            String generated = Path.Combine(sources,"GeneratedWorldEntrances");
            Boolean sourceChanged = Names.Where((n,i) => !File.Exists(Path.Combine(generated,n)) || File.ReadAllText(Path.Combine(generated,n)) != content[i]).Any();
            JObject previous = File.Exists(manifestPath) ? JObject.Parse(File.ReadAllText(manifestPath)) : null;
            Boolean current = !sourceChanged && previous != null && File.Exists(dll) &&
                (String)previous["sourceSHA256"] == sourceHash && (String)previous["dllSHA256"] == dllHash && !((Boolean?)previous["pendingCompile"] ?? false);
            JObject manifest = new JObject(new JProperty("version",2),new JProperty("sourceSHA256",sourceHash),new JProperty("pendingCompile",!current));
            if (current) manifest["dllSHA256"] = dllHash;
            else
            {
                Boolean pending = previous != null && ((Boolean?)previous["pendingCompile"] ?? false) && (String)previous["sourceSHA256"] == sourceHash;
                manifest["baselineDLLSHA256"] = pending ? (String)previous["baselineDLLSHA256"] ?? dllHash : dllHash;
                String ticks = File.Exists(dll) ? File.GetLastWriteTimeUtc(dll).Ticks.ToString(CultureInfo.InvariantCulture) : "";
                manifest["baselineDLLWriteTicks"] = pending ? (String)previous["baselineDLLWriteTicks"] ?? ticks : ticks;
            }
            var changes = new Dictionary<String,String>(StringComparer.OrdinalIgnoreCase);
            for (Int32 i = 0; i < Names.Length; i++) changes[Path.Combine(generated,Names[i])] = content[i];
            changes[jsonPath] = document.ToString(Formatting.Indented);
            changes[manifestPath] = manifest.ToString(Formatting.Indented);
            ApplyChanges(root,mod,changes);
            return !current;
        }

        // Called only after the existing compiler reports all-sources success.
        internal static void CompletePendingCompilations(Action<String> report)
        {
            foreach (String mod in Mods())
            {
                String root = Target(mod), path = Path.Combine(Path.Combine(root,DataFolder),"WorldEntrances.runtime.json");
                if (!File.Exists(path)) continue;
                JObject manifest = JObject.Parse(File.ReadAllText(path));
                if (!((Boolean?)manifest["pendingCompile"] ?? false)) continue;
                String generated = Path.Combine(root,"StreamingAssets/Scripts/Sources/GeneratedWorldEntrances");
                String sourceHash = Hash(Encoding.UTF8.GetBytes(String.Concat(Names.Select(n => File.ReadAllText(Path.Combine(generated,n))).ToArray())));
                if (sourceHash != (String)manifest["sourceSHA256"]) throw new InvalidOperationException(mod + ": entrance sources changed. Install them again before compiling.");
                String dll = Path.Combine(root,"StreamingAssets/Scripts/Memoria.Scripts." + mod + ".dll");
                if (!File.Exists(dll)) throw new InvalidOperationException(mod + ": compiled entrance DLL is missing.");
                String hash = Hash(File.ReadAllBytes(dll));
                if (hash == (String)manifest["baselineDLLSHA256"] &&
                    File.GetLastWriteTimeUtc(dll).Ticks.ToString(CultureInfo.InvariantCulture) == (String)manifest["baselineDLLWriteTicks"])
                    throw new InvalidOperationException(mod + ": pending entrance DLL was not rebuilt. Restart aborted.");
                EntranceJsonStore.Write(path,new JObject(new JProperty("version",2),new JProperty("sourceSHA256",sourceHash),
                    new JProperty("dllSHA256",hash),new JProperty("pendingCompile",false)));
                if (report != null) report("Entrance runtime compiled: " + mod);
            }
        }

        private static void ApplyChanges(String root, String mod, Dictionary<String,String> proposed)
        {
            var changes = proposed.Where(p => !File.Exists(p.Key) || File.ReadAllText(p.Key) != p.Value).ToArray();
            if (changes.Length == 0) return;
            String backup = Path.GetFullPath(Path.Combine(ToolRoot,"Backups/" + mod + "/" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0,8)));
            Directory.CreateDirectory(backup);
            var originals = new Dictionary<String,Byte[]>();
            foreach (var change in changes)
            {
                Byte[] bytes = File.Exists(change.Key) ? File.ReadAllBytes(change.Key) : null;
                originals.Add(change.Key,bytes);
                if (bytes == null) continue;
                String saved = Path.Combine(backup,change.Key.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(saved)); File.WriteAllBytes(saved,bytes);
            }
            File.WriteAllText(Path.Combine(backup,"Changes.txt"),"Source/data install only; no DLL changed.\r\n" +
                String.Join("\r\n",changes.Select(p => (originals[p.Key] == null ? "Created: " : "Replaced: ") + p.Key.Substring(root.Length + 1)).ToArray()));
            var attempted = new List<String>();
            try
            {
                foreach (var change in changes) { AtomicWrite(change.Key,Encoding.UTF8.GetBytes(change.Value)); attempted.Add(change.Key); }
            }
            catch
            {
                foreach (String path in attempted.AsEnumerable().Reverse())
                { if (originals[path] != null) AtomicWrite(path,originals[path]); else if (File.Exists(path)) File.Delete(path); }
                throw;
            }
        }
        private static void AtomicWrite(String path, Byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); String temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temp,bytes); if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private static String Hash(Byte[] bytes)
        { using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-",""); }
    }
}
