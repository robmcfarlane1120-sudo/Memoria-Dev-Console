using System;
using System.IO;
using System.Linq;

namespace Memoria.DevConsole
{
    internal static class FieldCreatorWorkflow
    {
        internal static String Root { get { return Path.GetFullPath("Memoria Dev Console/Asset Dump/FieldDumper_Output"); } }
        internal static String Input { get { return Path.GetFullPath("Memoria Dev Console/Asset Dump/FieldDumper_Input"); } }
        internal static String Source(Int32 id) { return Path.Combine(Root, "Field_" + id.ToString("D4")); }
        internal static String LatestCut(Int32 id)
        {
            if (!Directory.Exists(Root)) return null;
            return Directory.GetDirectories(Root, "Field_" + id.ToString("D4") + "_Cut_*")
                .OrderByDescending(p => Directory.GetLastWriteTimeUtc(p)).FirstOrDefault();
        }
        internal static String ValidateSource(Int32 id)
        {
            try
            {
                String folder = Source(id);
                if (!Directory.Exists(folder)) return "Target field " + id + " has no source export. Use [1] Export Source by Field ID, or load that field and use [2] Export Current Loaded Field. Then return to [4] to cut your PNG.";
                String[] files = Directory.GetFiles(folder, "*.bgx");
                if (files.Length != 1) return "Expected one BGX in " + folder + "; found " + files.Length + ".";
                foreach (String line in File.ReadAllLines(files[0]))
                {
                    String trimmed = line.Trim();
                    if (!trimmed.StartsWith("Image:", StringComparison.Ordinal)) continue;
                    String name = trimmed.Substring(6).Trim();
                    if (name.StartsWith("Image:", StringComparison.Ordinal)) return "BGX contains a duplicated Image: prefix. Correct the image reference before continuing.";
                    if (Path.IsPathRooted(name) || name.Split('/', '\\').Contains("..")) return "BGX image must be relative to its field folder: " + name;
                    if (!File.Exists(Path.Combine(folder, name))) return "Missing BGX layer: " + name + ". Re-export or restore that PNG first.";
                }
                return null;
            }
            catch (Exception ex) { return "Cannot inspect the field export: " + ex.Message; }
        }
        internal static String DescribePng(String path)
        {
            try
            {
                using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
                {
                    Byte[] header = reader.ReadBytes(24);
                    if (header.Length != 24 || header[0] != 137 || header[1] != 80 || header[2] != 78 || header[3] != 71 ||
                        header[4] != 13 || header[5] != 10 || header[6] != 26 || header[7] != 10 ||
                        header[12] != 73 || header[13] != 72 || header[14] != 68 || header[15] != 82) return "invalid PNG header";
                    UInt32 width = BigEndian(header, 16), height = BigEndian(header, 20);
                    if (width == 0 || height == 0) return "invalid PNG dimensions";
                    return width + "x" + height + " | " + (new FileInfo(path).Length / 1024L) + " KB";
                }
            }
            catch (Exception ex) { return "unavailable: " + ex.Message; }
        }
        private static UInt32 BigEndian(Byte[] bytes, Int32 at)
        { return ((UInt32)bytes[at] << 24) | ((UInt32)bytes[at + 1] << 16) | ((UInt32)bytes[at + 2] << 8) | bytes[at + 3]; }

        internal static Int32 CopySupportingResources(String source, String destination)
        {
            Int32 copied = 0;
            foreach (String file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                String ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".bytes" && ext != ".obj" && ext != ".png" && ext != ".jpg" && ext != ".memnfo") continue;
                if (Path.GetFileName(file).EndsWith("_Composite_PREVIEW.png", StringComparison.OrdinalIgnoreCase)) continue;
                String relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                String target = Path.Combine(destination, relative);
                if (File.Exists(target)) continue; // replacement layers have already been written
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, false);
                copied++;
            }
            return copied;
        }
    }
}
