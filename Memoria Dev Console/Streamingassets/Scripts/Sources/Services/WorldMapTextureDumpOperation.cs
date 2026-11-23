using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Memoria.Prime;
using Memoria.Assets;
using UnityEngine;

namespace Memoria.DevConsole
{
    internal sealed class WorldMapTextureDumpOperation
    {
        private sealed class Entry
        {
            public String Path;
            public Texture2D Texture;
        }
        private readonly Dictionary<String, Entry> entries = new Dictionary<String, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly List<String> discoveryErrors = new List<String>();
        private void Add(String path, Texture2D texture = null)
        {
            path = path.Replace('\\', '/');
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - 4);
            if (!entries.ContainsKey(path)) entries.Add(path, new Entry { Path = path, Texture = texture });
            else if (texture != null && entries[path].Texture == null) entries[path].Texture = texture;
        }
        public IEnumerator Run(Action<String> status)
        {
            String root = Path.Combine(Path.Combine(Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), "Memoria Dev Console"), "Asset Dump"), "WorldMapTextureDumper_Output");
            Directory.CreateDirectory(root);
            status?.Invoke("Scanning world-map 2D textures...\r\nOutput: " + root);
            yield return null;
            Discover();
            Int32 exported = 0, missing = 0, failed = 0, processed = 0;
            using (StreamWriter writer = new StreamWriter(Path.Combine(root, "TextureIndex.csv"), false))
            {
                writer.WriteLine("Source,PNG,Width,Height,Status,Detail");
                foreach (Entry entry in entries.Values.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
                {
                    String result = Export(entry, root, writer);
                    if (result == "EXPORTED") exported++;
                    else if (result == "UNAVAILABLE") missing++;
                    else failed++;
                    processed++;
                    if (processed % 4 == 0)
                    {
                        writer.Flush();
                        status?.Invoke("World Map Texture Dump: " + processed + " / " + entries.Count + "\r\nExported: " + exported + "   Unavailable: " + missing + "   Failed: " + failed);
                        yield return null;
                    }
                }
                foreach (String error in discoveryErrors)
                    writer.WriteLine(Csv(error) + ",,,,DISCOVERY_ERROR,");
            }
            String summary = "World Map 2D Texture Dump finished\r\nExported: " + exported + "   Unavailable: " + missing + "   Failed: " + failed +
                "   Discovery errors: " + discoveryErrors.Count + "\r\nOutput: " + root;
            File.WriteAllText(Path.Combine(root, "ExportReport.txt"), summary + "\r\n" + String.Join("\r\n", discoveryErrors.ToArray()) +
                "\r\nPNG paths mirror source resource paths. Output rows are vertically flipped from source texture pixels for editing. Reverse this flip when importing against the original mesh UVs. TextureIndex.csv records each result.\r\nOnly source Texture2D images are exported, including animation frames. No material-reference copies or RenderTexture snapshots.\r\nRe-running overwrites matching exports and regenerates the index; unrelated old files remain.\r\n");
            Log.Message("[WORLD MAP TEXTURE DUMPER] " + summary);
            status?.Invoke(summary);
        }
        private void Discover()
        {
            // Explicit native loader paths cover Resources-only assets and all animation frames.
            Add("WorldMap/Textures/res(1_24)_terrain");
            Add("WorldMap/Textures/res(1_24)_objects");
            String[] frames = { "11_0_128", "11_128_128", "11_128_0", "10_0_128", "10_64_0", "10_128_0", "10_128_64", "10_128_128", "11_64_0", "11_192_64" };
            for (Int32 i = 0; i < frames.Length; i++)
                for (Int32 frame = 0; frame < (i < 2 || i == 9 ? 4 : 6); frame++) Add("WorldMap/Textures/" + frames[i] + "_" + frame);
            foreach (String name in new[] { "11_0_192", "11_64_192", "Quicksand", "WaterShrine", "0_4_0_128", "1_4_0_128", "0_4_0_192", "1_4_0_192" }) Add("WorldMap/Materials/" + name);
            // The primary atlases were repathed from EmbeddedAsset; the runtime material
            // can still reference the embedded original when no replacement PNG is installed.
            foreach (String path in new[] { "WorldMap/Textures", "WorldMap/Materials", "EmbeddedAsset/WorldMap_Local/Textures" })
            {
                try { foreach (Texture2D texture in Resources.LoadAll<Texture2D>(path)) Add(path + "/" + texture.name, texture); }
                catch (Exception ex) { discoveryErrors.Add(path + ": " + ex.Message); }
            }
            ResolveAtlas("Terrain", "terrain");
            ResolveAtlas("Object", "objects");
            if (AssetManager.FolderHighToLow == null) return;
            foreach (AssetManager.AssetFolder mod in AssetManager.FolderHighToLow)
            {
                foreach (var bundle in mod.DictAssetBundleRefs.Values)
                {
                    if (bundle.assetBundle == null) continue;
                    try
                    {
                        foreach (String asset in bundle.assetBundle.GetAllAssetNames())
                        {
                            const String prefix = "assets/resources/";
                            if (!asset.StartsWith(prefix + "worldmap/", StringComparison.OrdinalIgnoreCase) &&
                                !asset.StartsWith(prefix + "embeddedasset/worldmap_local/", StringComparison.OrdinalIgnoreCase)) continue;
                            String path = asset.Substring(prefix.Length);
                            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) Add(path);

                        }
                    }
                    catch (Exception ex) { discoveryErrors.Add("Bundle scan: " + ex.Message); }
                }
                foreach (String resourceRoot in new[] { AssetManagerUtil.GetResourcesAssetsPath(true), AssetManagerUtil.GetStreamingAssetsPath() + "/Assets/Resources" })
                foreach (String prefix in new[] { "WorldMap", "EmbeddedAsset/WorldMap_Local" })
                {
                    String directory = Path.Combine(mod.FolderPath, resourceRoot + "/" + prefix);
                    try
                    {
                        if (!Directory.Exists(directory)) continue;
                        foreach (String file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                            if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                Add(prefix + "/" + file.Substring(directory.Length).TrimStart('/', '\\'));
                    }
                    catch (Exception ex) { discoveryErrors.Add(directory + ": " + ex.Message); }
                }
            }
        }
        private void ResolveAtlas(String materialName, String atlasName)
        {
            String target = "WorldMap/Textures/res(1_24)_" + atlasName;
            try
            {
                Texture2D texture = AssetManager.Load<Texture2D>(target, true);
                if (texture == null)
                {
                    Material material = AssetManager.Load<Material>("WorldMap/Materials/" + materialName, true);
                    texture = material == null ? null : material.mainTexture as Texture2D;
                }
                // One canonical source-image export; never generate a material/property copy.
                Add(target, texture);
            }
            catch (Exception ex) { discoveryErrors.Add(target + ": " + ex.Message); }
        }
        private static String Export(Entry entry, String root, StreamWriter writer)
        {
            Texture2D readable = null;
            Texture2D texture = null;
            Texture2D nativeCopy = null;
            String relative = entry.Path + ".png";
            try
            {
                if (entry.Path.Split('/', '\\').Any(p => p == "..") || Path.IsPathRooted(entry.Path)) throw new IOException("Invalid texture resource path.");
                texture = entry.Texture != null ? entry.Texture : AssetManager.Load<Texture2D>(entry.Path, true);
                if (texture == null)
                {
                    writer.WriteLine(Csv(entry.Path) + ",,,,UNAVAILABLE,");
                    return "UNAVAILABLE";
                }
                // Read actual source pixels first. A blit of an already readable image
                // is unnecessary and depends on the active render/shader state.
                Color32[] pixels;
                String readback = "CPU";
                try { pixels = texture.GetPixels32(); }
                catch (Exception)
                {
                    readback = "Memoria readback";
                    nativeCopy = TextureHelper.CopyAsReadable(texture);
                    if (nativeCopy == null) throw new IOException("Memoria could not create a readable texture.");
                    pixels = nativeCopy.GetPixels32();
                }
                if (pixels == null || pixels.Length != texture.width * texture.height)
                    throw new IOException("Readback returned an incomplete pixel buffer.");
                Boolean uniform = IsUniform(pixels);
                if (uniform && (entry.Path.IndexOf("res(1_24)_terrain", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                entry.Path.IndexOf("res(1_24)_objects", StringComparison.OrdinalIgnoreCase) >= 0))
                    throw new IOException("Rejected solid-color atlas: " + readback + ", source format " + texture.format + ". See Memoria.log.");
                readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                FlipRows(pixels, texture.width, texture.height);
                readable.SetPixels32(pixels);
                readable.Apply();
                String target = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, readable.EncodeToPNG());
                writer.WriteLine(Csv(entry.Path) + "," + Csv(relative) + "," + texture.width + "," + texture.height + ",EXPORTED," + Csv(readback + "; PNG row orientation=vertical flip from source; source=" + texture.format + (uniform ? "; SOLID COLOR (check source)" : "; pixel variation verified")));
                return "EXPORTED";
            }
            catch (Exception ex)
            {
                writer.WriteLine(Csv(entry.Path) + "," + Csv(relative) + ",,,FAILED," + Csv(ex.Message));
                Log.Warning("[WORLD MAP TEXTURE DUMPER] " + entry.Path + ": " + ex.Message);
                return "FAILED";
            }
            finally
            {
                if (readable != null) UnityEngine.Object.Destroy(readable);
                if (nativeCopy != null && !ReferenceEquals(nativeCopy, texture)) UnityEngine.Object.Destroy(nativeCopy);
            }
        }
        // Applies identically to CPU and native GPU readback. Never modify the source texture.
        private static void FlipRows(Color32[] pixels, Int32 width, Int32 height)
        {
            if (width <= 0 || height <= 0 || pixels == null || pixels.Length != (Int64)width * height)
                throw new ArgumentException("Invalid texture dimensions or pixel buffer.");
            for (Int32 y = 0; y < height / 2; y++)
            {
                Int32 top = y * width, bottom = (height - 1 - y) * width;
                for (Int32 x = 0; x < width; x++)
                {
                    Color32 temporary = pixels[top + x];
                    pixels[top + x] = pixels[bottom + x];
                    pixels[bottom + x] = temporary;
                }
            }
        }
        private static Boolean IsUniform(Color32[] pixels)
        {
            if (pixels.Length == 0) return true;
            Color32 first = pixels[0];
            for (Int32 i = 1; i < pixels.Length; i++)
                if (pixels[i].r != first.r || pixels[i].g != first.g || pixels[i].b != first.b || pixels[i].a != first.a) return false;
            return true;
        }
        private static String Csv(String value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
    }
}
