using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Memoria.DevConsole
{
    // Native BGS bytes are never rewritten. Only packed atlas RGB is edited.
    internal static class FieldNativeAtlas
    {
        internal sealed class Tile
        {
            internal Int32 Index, Overlay, Camera, X, Y, Depth;
            internal Boolean Effect;
        }

        internal static List<Tile> ReadTiles(Byte[] bgs)
        {
            if (bgs == null || bgs.Length < 52) throw new IOException("Truncated BGS header.");
            Int32 count = U16(bgs, 6), offset = checked((Int32)U32(bgs, 16));
            Check(bgs, offset, checked(count * 56));
            Int32 sceneX = I16(bgs, 32), sceneY = I16(bgs, 34), sceneZ = I16(bgs, 28);
            List<Tile> tiles = new List<Tile>();
            for (Int32 i = 0; i < count; i++)
            {
                Int32 o = offset + i * 56;
                UInt32 packed = U32(bgs, o + 36);
                Int32 n = (Int32)(packed >> 16), camera = (Int32)(packed & 255);
                Int32 loc = checked((Int32)U32(bgs, o + 40)), prm = checked((Int32)U32(bgs, o + 44));
                Check(bgs, loc, checked(n * 4)); Check(bgs, prm, checked(n * 8));
                for (Int32 j = 0; j < n; j++)
                {
                    UInt32 position = U32(bgs, loc + j * 4);
                    tiles.Add(new Tile {
                        Index = tiles.Count, Overlay = i, Camera = camera,
                        X = sceneX + I16(bgs, o + 8) + (Int32)(position >> 22),
                        Y = sceneY + I16(bgs, o + 10) + (Int32)((position >> 12) & 1023),
                        Depth = sceneZ + (Int32)(U32(bgs, o) >> 20) + (Int32)(position & 4095),
                        Effect = ((U32(bgs, prm + j * 8 + 4) >> 28) & 1) != 0
                    });
                }
            }
            return tiles;
        }

        internal static Color32[] Build(Byte[] bgs, Color32[] atlas, Int32 atlasWidth, Int32 atlasHeight,
            Color32[] artwork, Color32[] reference, Int32 width, Int32 height, Int32 scale,
            Int32 minX, Int32 minY, HashSet<Int32> excluded)
        {
            if (scale <= 0 || width <= 0 || height <= 0 ||
                artwork == null || reference == null || artwork.LongLength != (Int64)width * height ||
                reference.Length != artwork.Length || atlas == null ||
                atlas.LongLength != (Int64)atlasWidth * atlasHeight)
                throw new IOException("Invalid native atlas/canvas dimensions.");
            Int32 tileSize = checked(scale * 16), stride = tileSize + 4;
            if (atlasWidth <= 0 || atlasWidth % stride != 0 || atlasHeight % stride != 0)
                throw new IOException("Atlas is not a native packed tile atlas at this export scale. Re-export the original BGS field.");
            List<Tile> tiles = ReadTiles(bgs);
            Int32 columns = atlasWidth / stride;
            if ((Int64)tiles.Count > (Int64)columns * (atlasHeight / stride))
                throw new IOException("Atlas has fewer tile slots than the source BGS.");
            Color32[] result = (Color32[])atlas.Clone();
            foreach (Tile tile in tiles)
            {
                if (tile.Camera != 0 || tile.Effect || excluded.Contains(tile.Overlay)) continue;
                Int32 ax = 2 + tile.Index % columns * stride, ay = 2 + tile.Index / columns * stride;
                for (Int32 y = -2; y < tileSize + 2; y++)
                {
                    Int32 py = Math.Max(0, Math.Min(tileSize - 1, y));
                    Int32 cy = tile.Y * scale - minY + py;
                    if (cy < 0 || cy >= height) continue;
                    for (Int32 x = -2; x < tileSize + 2; x++)
                    {
                        Int32 px = Math.Max(0, Math.Min(tileSize - 1, x));
                        Int32 cx = tile.X * scale - minX + px;
                        if (cx < 0 || cx >= width) continue;
                        Int32 canvasAt = (height - 1 - cy) * width + cx;
                        Color32 before = reference[canvasAt], after = artwork[canvasAt];
                        if (before.r == after.r && before.g == after.g && before.b == after.b) continue;
                        Int32 originalAt = (atlasHeight - 1 - ay - py) * atlasWidth + ax + px;
                        Color32 original = atlas[originalAt];
                        if (original.a == 0) continue;
                        Int32 at = (atlasHeight - 1 - ay - y) * atlasWidth + ax + x;
                        Color32 updated = FieldCompositeBlending.ApplyArtworkDelta(original, before, after);
                        updated.a = atlas[at].a;
                        result[at] = updated;
                    }
                }
            }
            return result;
        }

        internal static String Export(String folder, String output, String name, Color32[] artwork,
            Color32[] reference, Int32 width, Int32 height, Int32 scale, Int32 minX, Int32 minY, HashSet<Int32> excluded)
        {
            String bgsPath = Path.Combine(folder, name + ".bgs.bytes"), atlasPath = Path.Combine(folder, "atlas.png");
            if (!File.Exists(bgsPath) || !File.Exists(atlasPath))
                throw new IOException("Template files are missing. Re-export the template field using [1] or [2], then try again with the same PNG in FieldDumper_Input.");
            Byte[] bgs = File.ReadAllBytes(bgsPath);
            Byte[] correctedBgs = FieldNativeDepthCorrections.Resolve(name, bgs);
            ValidateReference(folder, name, ReadTiles(correctedBgs));
            Texture2D atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D edited = null;
            try
            {
                if (!atlas.LoadImage(File.ReadAllBytes(atlasPath))) throw new IOException("Cannot decode source atlas.");
                Color32[] result = Build(bgs, atlas.GetPixels32(), atlas.width, atlas.height,
                    artwork, reference, width, height, scale, minX, minY, excluded);
                String native = Path.Combine(output, "NativeField");
                Directory.CreateDirectory(native);
                foreach (String file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    String relative = file.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("NativeField")) continue;
                    String ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".bytes" && ext != ".jpg" && !(ext == ".png" && relative.StartsWith("FieldSPS", StringComparison.OrdinalIgnoreCase))) continue;
                    String target = Path.Combine(native, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target, false);
                }
                edited = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false);
                edited.SetPixels32(result); edited.Apply(false, false);
                File.WriteAllBytes(Path.Combine(native, "atlas.png"), edited.EncodeToPNG());
                File.WriteAllText(Path.Combine(native, "INSTALL.txt"),
                    "INSTALL THE CONTENTS OF THIS NativeField FOLDER into the target field resource folder.\r\n" +
                    "Remove any existing .bgx from that target folder: BGX overrides BGS and flattens tile depths.\r\n" +
                    "For a custom field name, rename the main .bgs.bytes and .bgi.bytes to that field's name.\r\n" +
                    "Required Graphics.TileSize: " + scale * 16 + " (matches this exported atlas).\r\n" +
                    "BGS bytes, tile depths, overlay IDs, cameras, animation and sprite shaders are preserved.\r\n" +
                    "Only normal tile RGB receives artwork differences from the original composite. Alpha, effect tiles and excluded overlays stay original.\r\n" +
                    "A flattened redesign cannot reconstruct newly revealed hidden artwork or change walkmesh geometry.\r\n");
                File.WriteAllText(Path.Combine(native, "DEPTH_REPORT.txt"), Describe(bgs));
                if (!bgs.SequenceEqual(correctedBgs))
                {
                    String custom = Path.Combine(output, "CustomFieldGeometry");
                    Directory.CreateDirectory(custom);
                    File.WriteAllBytes(Path.Combine(custom, name + ".bgs.bytes"), correctedBgs);
                    File.WriteAllText(Path.Combine(custom, "INSTALL.txt"),
                        "CUSTOM FIELD NAMES ONLY: after copying NativeField, use this BGS instead of its original BGS.\r\n" +
                        "Rename it to the new custom field's registered resource name.\r\n" +
                        "Memoria's source-name depth corrections are already baked into these bytes.\r\n" +
                        "For the original source field name, use NativeField's unmodified BGS to avoid applying corrections twice.\r\n");
                    File.WriteAllText(Path.Combine(custom, "DEPTH_REPORT.txt"), Describe(correctedBgs));
                    File.AppendAllText(Path.Combine(native, "INSTALL.txt"),
                        "\r\nTHIS SOURCE HAS MEMORIA DEPTH CORRECTIONS. For a renamed custom field, use the BGS in ../CustomFieldGeometry after copying this folder. See its INSTALL.txt.\r\n");
                }
                return "DEPTH-SAFE INSTALL: " + native + (bgs.SequenceEqual(correctedBgs) ? "" : "\r\nCustom field name: also install the BGS from CustomFieldGeometry; source-name corrections are baked in.");
            }
            finally
            {
                UnityEngine.Object.Destroy(atlas);
                if (edited != null) UnityEngine.Object.Destroy(edited);
            }
        }

        private static void ValidateReference(String folder, String name, List<Tile> tiles)
        {
            String[] blocks = File.ReadAllText(Path.Combine(folder, name + ".bgx")).Split(new String[] { "OVERLAY" }, StringSplitOptions.None);
            Int32 count = U16(File.ReadAllBytes(Path.Combine(folder, name + ".bgs.bytes")), 6);
            if (blocks.Length - 1 != count) throw new IOException("BGX overlay count differs from BGS. Use an unmodified source export.");
            for (Int32 i = 0; i < count; i++)
            {
                List<Tile> group = tiles.Where(t => t.Overlay == i).ToList();
                if (group.Count == 0) continue;
                String line = blocks[i + 1].Split('\n').Select(t => t.Trim()).FirstOrDefault(t => t.StartsWith("Position:"));
                if (line == null) throw new IOException("Missing BGX position.");
                Int32[] p = line.Substring(9).Split(',').Select(t => Int32.Parse(t.Trim(), CultureInfo.InvariantCulture)).ToArray();
                if (p.Length != 3 || p[0] != group.Min(t => t.X) || p[1] != group.Min(t => t.Y) || p[2] != group.Min(t => t.Depth))
                    throw new IOException("BGX layout differs from BGS at overlay " + i + ". Re-export the original field before depth-safe cutting.");
            }
        }

        internal static String Describe(Byte[] bgs)
        {
            List<Tile> tiles = ReadTiles(bgs);
            StringBuilder report = new StringBuilder("Native BGS geometry is authoritative. BGX editing references flatten each overlay to one depth.\r\n");
            foreach (var group in tiles.GroupBy(t => t.Overlay))
                report.AppendFormat(CultureInfo.InvariantCulture, "Overlay {0}: {1} tiles, depth {2}..{3}, {4} distinct depths.\r\n",
                    group.Key, group.Count(), group.Min(t => t.Depth), group.Max(t => t.Depth), group.Select(t => t.Depth).Distinct().Count());
            return report.ToString();
        }

        internal static void WriteDepthReport(String folder, String name)
        {
            String path = Path.Combine(folder, name + ".bgs.bytes");
            if (File.Exists(path)) File.WriteAllText(Path.Combine(folder, "DEPTH_REPORT.txt"), Describe(File.ReadAllBytes(path)));
        }

        private static void Check(Byte[] data, Int32 offset, Int32 count)
        {
            if (offset < 0 || count < 0 || (Int64)offset + count > data.Length) throw new IOException("Invalid BGS offset/count.");
        }
        private static UInt16 U16(Byte[] d, Int32 o) { Check(d, o, 2); return BitConverter.ToUInt16(d, o); }
        private static Int16 I16(Byte[] d, Int32 o) { Check(d, o, 2); return BitConverter.ToInt16(d, o); }
        private static UInt32 U32(Byte[] d, Int32 o) { Check(d, o, 4); return BitConverter.ToUInt32(d, o); }
    }
}