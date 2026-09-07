using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Assets.Sources.Scripts.UI.Common;
using Memoria;
using Memoria.Assets;
using Memoria.Prime;

using UnityEngine;
using Object = System.Object;

namespace Memoria.DevConsole
{
    public sealed class AssetDumpService
    {
        private Boolean _running;

        public Boolean IsRunning
        {
            get { return _running; }
        }

        public IEnumerator DumpFieldBackgrounds(Action<String> status)
        {
            if (_running)
            {
                Report(status, "Another complete-game asset dump is already running.");
                yield break;
            }

            _running = true;
            IEnumerator worker = new FieldBackgroundDumpOperation().Run(status);
            try
            {
                while (worker.MoveNext())
                    yield return worker.Current;
            }
            finally
            {
                _running = false;
            }
        }

        public IEnumerator DumpModelTextures(Action<String> status)
        {
            if (_running)
            {
                Report(status, "Another complete-game asset dump is already running.");
                yield break;
            }

            _running = true;
            IEnumerator worker = new TextureDumpOperation().Run(status);
            try
            {
                while (worker.MoveNext())
                    yield return worker.Current;
            }
            finally
            {
                _running = false;
            }
        }

        public IEnumerator DumpFieldBackground(Int32 fieldId, Action<String> status)
        {
            if (_running)
            {
                Report(status, "Another asset dump is already running.");
                yield break;
            }

            _running = true;
            IEnumerator worker = new FieldBackgroundDumpOperation().RunSingle(fieldId, status);
            try
            {
                while (worker.MoveNext())
                    yield return worker.Current;
            }
            finally
            {
                _running = false;
            }
        }


        public IEnumerator DumpCurrentBattleBackground(Action<String> status)
        {
            if (_running)
            {
                Report(status, "Another asset dump is already running.");
                yield break;
            }

            _running = true;
            IEnumerator worker = new CurrentBattleBackgroundDumpOperation().Run(status);
            try
            {
                while (worker.MoveNext())
                    yield return worker.Current;
            }
            finally
            {
                _running = false;
            }
        }

        public IEnumerator DumpModelTexture(Int32 modelId, Action<String> status)
        {
            if (_running)
            {
                Report(status, "Another asset dump is already running.");
                yield break;
            }

            _running = true;
            IEnumerator worker = new TextureDumpOperation().RunSingle(modelId, status);
            try
            {
                while (worker.MoveNext())
                    yield return worker.Current;
            }
            finally
            {
                _running = false;
            }
        }

        private static void Report(Action<String> status, String text)
        {
            if (status != null)
                status(text);
        }
    }


    internal sealed class CurrentBattleBackgroundDumpOperation
    {
        private const String OutputRootName = "BattleBackgroundDumper_Output";

        public IEnumerator Run(Action<String> status)
        {
            GameObject background = null;
            try
            {
                background = FF9StateSystem.Battle.FF9Battle.map.btlBGPtr;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BATTLE BG DUMPER] Could not query current battle background.");
            }

            if (background == null)
            {
                Report(status,
                    "No live battle background was found.\r\n" +
                    "Enter an actual battle, press F10, then run this option again.");
                yield break;
            }

            Int32 battleMapIndex = FF9StateSystem.Battle.battleMapIndex;
            String battleSceneName = FindBattleSceneName(battleMapIndex);
            String battleBackgroundName = FindBattleBackgroundName(battleSceneName);
            String rootName = SanitizeFileName(!String.IsNullOrEmpty(battleBackgroundName)
                ? battleBackgroundName
                : (String.IsNullOrEmpty(background.name) ? "BattleBackground" : background.name));
            String outputRoot = Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), OutputRootName);
            String outputDirectory = Path.Combine(outputRoot, "BattleMap_" + battleMapIndex.ToString("D4", CultureInfo.InvariantCulture) + "_" + rootName);
            String materialMapPath = Path.Combine(outputDirectory, "MaterialMap.csv");

            Directory.CreateDirectory(outputDirectory);

            MeshRenderer[] renderers = background.GetComponentsInChildren<MeshRenderer>(true);
            HashSet<String> exportedTextureNames = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            Int32 exported = 0;
            Int32 failed = 0;

            Report(status,
                "Dumping current battle background...\r\n" +
                "Battle map index: " + battleMapIndex + "\r\n" +
                "Battle scene: " + (String.IsNullOrEmpty(battleSceneName) ? "unknown" : battleSceneName) + "\r\n" +
                "BBG model: " + (String.IsNullOrEmpty(battleBackgroundName) ? "unknown" : battleBackgroundName) + "\r\n" +
                "Background root: " + background.name + "\r\n" +
                "Output: " + outputDirectory);

            using (StreamWriter mapWriter = new StreamWriter(materialMapPath, false))
            {
                mapWriter.WriteLine("Renderer,Material,Texture,Width,Height,PNG");

                for (Int32 rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    MeshRenderer renderer = renderers[rendererIndex];
                    if (renderer == null)
                        continue;

                    Material[] materials = renderer.materials;
                    for (Int32 materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        Material material = materials[materialIndex];
                        Texture texture = material != null ? material.mainTexture : null;
                        if (texture == null)
                            continue;

                        String textureName = String.IsNullOrEmpty(texture.name)
                            ? "unnamed_texture_" + rendererIndex + "_" + materialIndex
                            : texture.name;
                        String pngName = SanitizeFileName(textureName) + ".png";

                        mapWriter.WriteLine(
                            Csv(renderer.name) + "," +
                            Csv(material.name) + "," +
                            Csv(textureName) + "," +
                            texture.width.ToString(CultureInfo.InvariantCulture) + "," +
                            texture.height.ToString(CultureInfo.InvariantCulture) + "," +
                            Csv(pngName));

                        if (!exportedTextureNames.Add(textureName))
                            continue;

                        try
                        {
                            Texture2D readable = TextureHelper.CopyAsReadable(texture);
                            if (readable == null)
                            {
                                failed++;
                                continue;
                            }

                            TextureHelper.WriteTextureToFile(readable, Path.Combine(outputDirectory, pngName));
                            exported++;

                            if (!Object.ReferenceEquals(readable, texture))
                                UnityEngine.Object.DestroyImmediate(readable);
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Log.Error(ex, "[BATTLE BG DUMPER] Failed texture: " + textureName);
                        }
                    }

                    if ((rendererIndex % 8) == 0)
                        yield return null;
                }
            }

            String infoPath = Path.Combine(outputDirectory, "BATTLE_BACKGROUND_INFO.txt");
            File.WriteAllText(
                infoPath,
                "Battle map index: " + battleMapIndex + "\r\n" +
                "Battle scene: " + (String.IsNullOrEmpty(battleSceneName) ? "unknown" : battleSceneName) + "\r\n" +
                "BBG model: " + (String.IsNullOrEmpty(battleBackgroundName) ? "unknown" : battleBackgroundName) + "\r\n" +
                "Background root: " + background.name + "\r\n" +
                "Renderer count: " + renderers.Length + "\r\n" +
                "Unique PNG textures exported: " + exported + "\r\n" +
                "Failed textures: " + failed + "\r\n" +
                "\r\nThe PNG filenames preserve the live Unity texture names used by this battle background.\r\n" +
                "MaterialMap.csv records which renderer/material used each texture.\r\n");

            Log.Message("[BATTLE BG DUMPER] Finished. Exported " + exported + " unique textures to " + outputDirectory);
            Report(status,
                "Current Battle Background Dump COMPLETE\r\n" +
                "Exported: " + exported + " PNG(s)   Failed: " + failed + "\r\n" +
                "Battle map index: " + battleMapIndex + "\r\n" +
                "Battle scene: " + (String.IsNullOrEmpty(battleSceneName) ? "unknown" : battleSceneName) + "\r\n" +
                "BBG model: " + (String.IsNullOrEmpty(battleBackgroundName) ? "unknown" : battleBackgroundName) + "\r\n" +
                "Background: " + background.name + "\r\n" +
                "Output: " + outputDirectory);
        }

        private static String FindBattleSceneName(Int32 battleMapIndex)
        {
            try
            {
                foreach (KeyValuePair<String, Int32> pair in FF9BattleDB.SceneData)
                    if (pair.Value == battleMapIndex)
                        return pair.Key;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BATTLE BG DUMPER] Could not resolve battle scene name.");
            }
            return null;
        }

        private static String FindBattleBackgroundName(String battleSceneName)
        {
            if (String.IsNullOrEmpty(battleSceneName))
                return null;

            try
            {
                String name;
                if (FF9BattleDB.MapModel.TryGetValue(battleSceneName, out name))
                    return name;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BATTLE BG DUMPER] Could not resolve BBG model name.");
            }
            return null;
        }

        private static String Csv(String value)
        {
            if (value == null)
                value = String.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static String SanitizeFileName(String value)
        {
            if (String.IsNullOrEmpty(value))
                return "unnamed";

            Char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(value.Length);
            for (Int32 i = 0; i < value.Length; i++)
            {
                Char ch = value[i];
                builder.Append(invalid.Contains(ch) ? '_' : ch);
            }
            return builder.ToString();
        }

        private static void Report(Action<String> status, String text)
        {
            if (status != null)
                status(text);
        }
    }

    internal sealed class FieldBackgroundDumpOperation
    {

        private const String OutputRootName = "FieldDumper_Output";
        private const String CompleteMarkerName = "_COMPLETE.txt";
        private const String IndexFileName = "FieldIndex.csv";
        public IEnumerator Run(Action<String> status)
        {
String outputRoot = Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), OutputRootName);
            String completeMarker = Path.Combine(outputRoot, CompleteMarkerName);
            String indexPath = Path.Combine(outputRoot, IndexFileName);

            Directory.CreateDirectory(outputRoot);

            if (File.Exists(completeMarker))
            {
                Log.Message($"[FIELD DUMPER] Already complete. Delete {completeMarker} to run it again.");
                Report(status, "Field Background Dump already complete.\r\nDelete FieldDumper_Output\\_COMPLETE.txt to run the complete dump again.\r\nOutput: " + outputRoot);
                yield break;
            }

            Dictionary<Int32, String> fieldMap = GetInternalFieldMap();

            List<KeyValuePair<Int32, String>> fields =
                fieldMap
                    .Where(pair => !String.IsNullOrEmpty(pair.Value) && pair.Value != "invalidFieldMapID")
                    .OrderBy(pair => pair.Key)
                    .ToList();

            Report(status, $"Field Background Dump running... 0 / {fields.Count} fields\r\nOutput: {outputRoot}");

            using (StreamWriter indexWriter = new StreamWriter(indexPath, false))
            {
                indexWriter.WriteLine("FieldId,FieldName,Status,Folder");

                Int32 completed = 0;
                Int32 skipped = 0;
                Int32 failed = 0;

                foreach (KeyValuePair<Int32, String> pair in fields)
                {
                    Int32 fieldId = pair.Key;
                    String fieldName = pair.Value;
                    String fieldFolderName = $"Field_{fieldId:D4}";
                    String fieldFolder = Path.Combine(outputRoot, fieldFolderName);
                    String bgxPath = Path.Combine(fieldFolder, fieldName + BGSCENE_DEF.MemoriaBGXExtension);

                    try
                    {
                        Directory.CreateDirectory(fieldFolder);

                        // Resume behavior: a successfully-written BGX means this field was already exported.
                        if (File.Exists(bgxPath))
                        {
                            skipped++;
                            WriteIndexLine(indexWriter, fieldId, fieldName, "SKIPPED", fieldFolderName);
                            continue;
                        }

                        BGSCENE_DEF scene = new BGSCENE_DEF(true);
                        scene.LoadResources(FieldMap.GetMapResourcePath(fieldName), fieldName);

                        ExportSceneWithCorrectPaths(scene, bgxPath);

                        completed++;
                        WriteIndexLine(indexWriter, fieldId, fieldName, "OK", fieldFolderName);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        WriteIndexLine(indexWriter, fieldId, fieldName, "FAILED", fieldFolderName);
                        Log.Error(ex, $"[FIELD DUMPER] Failed field {fieldId}: {fieldName}");
                    }

                    // Let the game breathe and let Unity release temporary work between fields.
                    if (((completed + skipped + failed) % 5) == 0)
                    {
                        Resources.UnloadUnusedAssets();
                        GC.Collect();
                    }

                    if (((completed + skipped + failed) % 10) == 0)
                        Report(status, $"Field Background Dump running... {completed + skipped + failed} / {fields.Count} fields\r\nExported: {completed}   Skipped: {skipped}   Failed: {failed}\r\nOutput: {outputRoot}");

                    yield return null;
                }

                indexWriter.Flush();

                File.WriteAllText(
                    completeMarker,
                    "FFIX Field Dumper completed.\r\n" +
                    $"Completed: {completed}\r\n" +
                    $"Skipped: {skipped}\r\n" +
                    $"Failed: {failed}\r\n" +
                    $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n");

                Log.Message($"[FIELD DUMPER] Finished: {completed} exported, {skipped} skipped, {failed} failed. Output: {outputRoot}");
                Report(status, $"Field Background Dump COMPLETE\r\nExported: {completed}   Skipped: {skipped}   Failed: {failed}\r\nOutput: {outputRoot}");
            }
        }

        public IEnumerator RunSingle(Int32 fieldId, Action<String> status)
        {
            String outputRoot = Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), OutputRootName);
            Dictionary<Int32, String> fieldMap = GetInternalFieldMap();
            String fieldName;
            if (!fieldMap.TryGetValue(fieldId, out fieldName) || String.IsNullOrEmpty(fieldName) || fieldName == "invalidFieldMapID")
            {
                Report(status, $"Field ID {fieldId} was not found in the internal FFIX field table.");
                yield break;
            }

            String fieldFolderName = $"Field_{fieldId:D4}";
            String fieldFolder = Path.Combine(outputRoot, fieldFolderName);
            String bgxPath = Path.Combine(fieldFolder, fieldName + BGSCENE_DEF.MemoriaBGXExtension);
            Directory.CreateDirectory(fieldFolder);

            Report(status, $"Dumping field {fieldId}: {fieldName}...\r\nOutput: {fieldFolder}");
            Log.Message($"[FIELD DUMPER] Single field export {fieldId}: {fieldName}");

            try
            {
                BGSCENE_DEF scene = new BGSCENE_DEF(true);
                scene.LoadResources(FieldMap.GetMapResourcePath(fieldName), fieldName);
                ExportSceneWithCorrectPaths(scene, bgxPath);
                Report(status, $"Field {fieldId} COMPLETE\r\n{fieldName}\r\nOutput: {fieldFolder}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[FIELD DUMPER] Failed single field {fieldId}: {fieldName}");
                Report(status, $"Field {fieldId} FAILED\r\n{ex.Message}");
            }

            yield return null;
        }

        private static Dictionary<Int32, String> GetInternalFieldMap()
        {
            // EventEngineUtils is internal in Assembly-CSharp, so external Memoria script mods
            // cannot reference it directly at compile time. Field Creator itself can because it
            // lives inside Assembly-CSharp. Reflection lets us read the same public static table.
            Assembly gameAssembly = typeof(FieldMap).Assembly;
            Type utilsType = gameAssembly.GetType("EventEngineUtils", false);
            if (utilsType == null)
                throw new Exception("Could not find internal type EventEngineUtils in Assembly-CSharp.");

            FieldInfo mapField = utilsType.GetField(
                "eventIDToFBGID",
                BindingFlags.Public | BindingFlags.Static);

            if (mapField == null)
                throw new Exception("Could not find EventEngineUtils.eventIDToFBGID.");

            Dictionary<Int32, String> map =
                mapField.GetValue(null) as Dictionary<Int32, String>;

            if (map == null)
                throw new Exception("EventEngineUtils.eventIDToFBGID had an unexpected type.");

            return map;
        }

        private static void ExportSceneWithCorrectPaths(BGSCENE_DEF scene, String bgxExportPath)
        {
            // This deliberately mirrors Memoria's BGSCENE_DEF.ExportMemoriaBGX,
            // except textureBasePath includes the destination folder.
            // The stock method passes only the filename to ExportMemoriaBGXOverlay,
            // which is why Field Creator PNGs fall into the working directory.

            if (!scene.isPureMemoriaScene)
                scene.atlas = TextureHelper.CopyAsReadable(scene.atlas);

            String folder = Path.GetDirectoryName(bgxExportPath);
            String fileName = Path.GetFileNameWithoutExtension(bgxExportPath);

            if (String.IsNullOrEmpty(folder))
                throw new Exception("Could not determine BGX output folder.");

            Directory.CreateDirectory(folder);

            String textureBasePath = Path.Combine(folder, fileName);
            String bgxText = String.Empty;

            foreach (BGOVERLAY_DEF bgOverlay in scene.overlayList)
            {
                bgxText += "OVERLAY\n";
                bgxText += scene.ExportMemoriaBGXOverlay(bgOverlay, textureBasePath);
            }

            foreach (BGANIM_DEF bgAnim in scene.animList)
            {
                bgxText += "ANIMATION\n";
                bgxText += $"CameraId: {bgAnim.camNdx}\n";
                bgxText += $"FrameRate: {bgAnim.frameRate}\n";
                bgxText += $"Overlays: {String.Join(", ", bgAnim.frameList.Select(frame => frame.target.ToString()).ToArray())}\n";
                bgxText += "\n";
            }

            foreach (BGCAM_DEF bgCamera in scene.cameraList)
            {
                bgxText += "CAMERA\n";
                bgxText += $"ViewDistance: {bgCamera.proj}\n";
                bgxText += $"CenterOffset: {bgCamera.centerOffset[0]}, {bgCamera.centerOffset[1]}\n";
                bgxText += $"Position: {bgCamera.t[0]}, {bgCamera.t[1]}, {bgCamera.t[2]}\n";
                bgxText += $"Range: {bgCamera.w}, {bgCamera.h}\n";
                bgxText += $"DepthOffset: {bgCamera.depthOffset}\n";
                bgxText += $"Viewport: {bgCamera.vrpMinX}, {bgCamera.vrpMaxX}, {bgCamera.vrpMinY}, {bgCamera.vrpMaxY}\n";

                Matrix4x4 matrixRT = bgCamera.GetMatrixRT();
                bgxText += String.Format(
                    CultureInfo.InvariantCulture,
                    "OrientationMatrix: {0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}\n",
                    matrixRT[0, 0], matrixRT[0, 1], matrixRT[0, 2],
                    matrixRT[1, 0], matrixRT[1, 1], matrixRT[1, 2],
                    matrixRT[2, 0], matrixRT[2, 1], matrixRT[2, 2]);

                bgxText += "\n";
            }

            File.WriteAllText(bgxExportPath, bgxText);
        }

        private static void WriteIndexLine(
            StreamWriter writer,
            Int32 fieldId,
            String fieldName,
            String status,
            String folder)
        {
            writer.WriteLine(
                $"{fieldId},{Csv(fieldName)},{Csv(status)},{Csv(folder)}");
            writer.Flush();
        }

        private static String Csv(String value)
        {
            if (value == null)
                return String.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    
        private static void Report(Action<String> status, String text)
        {
            if (status != null)
                status(text);
        }
    }

    internal sealed class TextureDumpOperation
    {

        private const String OutputRootName = "2D Texture Dump Output";
        private const String CompleteMarkerName = "_COMPLETE.txt";
        private const String IndexFileName = "ModelIndex.csv";
        private sealed class NameUsage
        {
            public String Name;
            public Int32 Count;
            public HashSet<String> BattleScenes = new HashSet<String>();
        }

        private sealed class ModelNameInfo
        {
            public Dictionary<String, NameUsage> Names = new Dictionary<String, NameUsage>(StringComparer.OrdinalIgnoreCase);
        }
        public IEnumerator Run(Action<String> status)
        {
String outputRoot = Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), OutputRootName);
            String completeMarker = Path.Combine(outputRoot, CompleteMarkerName);
            String indexPath = Path.Combine(outputRoot, IndexFileName);
            Directory.CreateDirectory(outputRoot);

            if (File.Exists(completeMarker))
            {
                Log.Message($"[ASSET DUMPER] Already complete. Delete {completeMarker} to run it again.");
                Report(status, "2D Texture Dump already complete.\r\nDelete 2D Texture Dump Output\\_COMPLETE.txt to run the complete dump again.\r\nOutput: " + outputRoot);
                yield break;
            }

            Dictionary<Int32, ModelNameInfo> enemyNames = BuildEnemyNameMap();

            List<KeyValuePair<Int32, String>> geoModels = FF9BattleDB.GEO
                .OrderBy(pair => pair.Key)
                .ToList();

            Report(status, $"2D Texture Dump running... 0 / {geoModels.Count} models\r\nOutput: {outputRoot}");

            Int32 completed = 0;
            Int32 skipped = 0;
            Int32 failed = 0;
            Int32 textureCount = 0;

            using (StreamWriter indexWriter = new StreamWriter(indexPath, false))
            {
                indexWriter.WriteLine("ModelId,DisplayName,GEOName,Textures,Status,Folder");

                foreach (KeyValuePair<Int32, String> geo in geoModels)
                {
                    Int32 modelId = geo.Key;
                    String geoName = geo.Value;
                    ModelNameInfo nameInfo;
                    enemyNames.TryGetValue(modelId, out nameInfo);

                    String clearName = GetPrimaryName(nameInfo, geoName);
                    String folderLabel = String.IsNullOrEmpty(clearName) ? geoName : clearName;
                    String categoryName = GetModelCategory(geoName);
                    String folderName = modelId.ToString("D4", CultureInfo.InvariantCulture) + "_" + SanitizeFileName(folderLabel);
                    String categoryFolder = Path.Combine(outputRoot, categoryName);
                    String modelFolder = Path.Combine(categoryFolder, folderName);
                    String keyPath = Path.Combine(modelFolder, "GEO_KEY.txt");
                    String texturesFolder = Path.Combine(modelFolder, "Textures");

                    try
                    {
                        Directory.CreateDirectory(modelFolder);
                        Directory.CreateDirectory(texturesFolder);

                        // Resume support: GEO_KEY means this model already completed.
                        if (File.Exists(keyPath))
                        {
                            skipped++;
                            WriteIndexLine(indexWriter, modelId, clearName, geoName, 0, "SKIPPED", categoryName + "/" + folderName);
                            continue;
                        }

                        Int32 dumpedTextures = DumpModelTextures(geoName, texturesFolder);

                        WriteGeoKey(keyPath, modelId, geoName, clearName, nameInfo);

                        completed++;
                        textureCount += dumpedTextures;
                        WriteIndexLine(indexWriter, modelId, clearName, geoName, dumpedTextures, "OK", categoryName + "/" + folderName);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        WriteIndexLine(indexWriter, modelId, clearName, geoName, 0, "FAILED", categoryName + "/" + folderName);
                        Log.Error(ex, $"[ASSET DUMPER] Failed model {modelId}: {geoName}");
                    }

                    if (((completed + skipped + failed) % 5) == 0)
                    {
                        Resources.UnloadUnusedAssets();
                        GC.Collect();
                    }

                    if (((completed + skipped + failed) % 10) == 0)
                        Report(status, $"2D Texture Dump running... {completed + skipped + failed} / {geoModels.Count} models\r\nTextures written: {textureCount}   Failed: {failed}\r\nOutput: {outputRoot}");

                    yield return null;
                }

                indexWriter.Flush();
            }

            File.WriteAllText(
                completeMarker,
                "FFIX Texture Dumper completed.\r\n" +
                $"Completed models: {completed}\r\n" +
                $"Skipped models: {skipped}\r\n" +
                $"Failed models: {failed}\r\n" +
                $"Textures written: {textureCount}\r\n" +
                $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n");

            Log.Message($"[ASSET DUMPER] Finished: {completed} models, {textureCount} textures, {skipped} skipped, {failed} failed. Output: {outputRoot}");
            Report(status, $"2D Texture Dump COMPLETE\r\nModels exported: {completed}   Skipped: {skipped}   Failed: {failed}\r\nTextures written: {textureCount}\r\nOutput: {outputRoot}");
        }


        public IEnumerator RunSingle(Int32 modelId, Action<String> status)
        {
            KeyValuePair<Int32, String> geoEntry = FF9BattleDB.GEO
                .Where(pair => pair.Key == modelId)
                .FirstOrDefault();
            String geoName = geoEntry.Value;
            if (String.IsNullOrEmpty(geoName))
            {
                Report(status, $"Model ID {modelId} was not found in FF9BattleDB.GEO.");
                yield break;
            }

            String outputRoot = Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), OutputRootName);
            Dictionary<Int32, ModelNameInfo> enemyNames = BuildEnemyNameMap();
            ModelNameInfo nameInfo;
            enemyNames.TryGetValue(modelId, out nameInfo);
            String clearName = GetPrimaryName(nameInfo, geoName);
            String folderLabel = String.IsNullOrEmpty(clearName) ? geoName : clearName;
            String categoryName = GetModelCategory(geoName);
            String folderName = modelId.ToString("D4", CultureInfo.InvariantCulture) + "_" + SanitizeFileName(folderLabel);
            String modelFolder = Path.Combine(Path.Combine(outputRoot, categoryName), folderName);
            String texturesFolder = Path.Combine(modelFolder, "Textures");
            String keyPath = Path.Combine(modelFolder, "GEO_KEY.txt");

            Directory.CreateDirectory(texturesFolder);
            Report(status, $"Dumping model {modelId}: {geoName}...\r\nOutput: {modelFolder}");
            Log.Message($"[ASSET DUMPER] Single model texture export {modelId}: {geoName}");

            try
            {
                Int32 dumpedTextures = DumpModelTextures(geoName, texturesFolder);
                WriteGeoKey(keyPath, modelId, geoName, clearName, nameInfo);
                Report(status, $"Model {modelId} COMPLETE\r\n{geoName}\r\nTextures written: {dumpedTextures}\r\nOutput: {modelFolder}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[ASSET DUMPER] Failed single model {modelId}: {geoName}");
                Report(status, $"Model {modelId} FAILED\r\n{ex.Message}");
            }

            Resources.UnloadUnusedAssets();
            GC.Collect();
            yield return null;
        }

        private static Dictionary<Int32, ModelNameInfo> BuildEnemyNameMap()
        {
            Dictionary<Int32, ModelNameInfo> result = new Dictionary<Int32, ModelNameInfo>();
            Int32 scenesRead = 0;

            foreach (KeyValuePair<String, Int32> scenePair in FF9BattleDB.SceneData)
            {
                if (String.IsNullOrEmpty(scenePair.Key) || !scenePair.Key.StartsWith("BSC_", StringComparison.Ordinal))
                    continue;

                String sceneName = scenePair.Key.Substring(4);

                try
                {
                    BTL_SCENE scene = new BTL_SCENE();
                    scene.ReadBattleScene(sceneName);

                    String[] battleText = FF9TextTool.GetBattleText(scenePair.Value);
                    if (battleText == null)
                        continue;

                    Int32 typeCount = Math.Min((Int32)scene.header.TypCount, scene.MonAddr.Length);
                    typeCount = Math.Min(typeCount, battleText.Length);

                    for (Int32 i = 0; i < typeCount; i++)
                    {
                        Int32 geoId = scene.MonAddr[i].Geo;
                        String displayName = CleanDisplayName(battleText[i]);
                        if (String.IsNullOrEmpty(displayName))
                            continue;

                        ModelNameInfo modelInfo;
                        if (!result.TryGetValue(geoId, out modelInfo))
                        {
                            modelInfo = new ModelNameInfo();
                            result.Add(geoId, modelInfo);
                        }

                        NameUsage usage;
                        if (!modelInfo.Names.TryGetValue(displayName, out usage))
                        {
                            usage = new NameUsage { Name = displayName };
                            modelInfo.Names.Add(displayName, usage);
                        }

                        usage.Count++;
                        usage.BattleScenes.Add(sceneName);
                    }

                    scenesRead++;
                }
                catch (Exception ex)
                {
                    // A bad/unsupported scene should not kill the complete asset dump.
                    Log.Warning($"[ASSET DUMPER] Could not read battle names from {sceneName}: {ex.Message}");
                }
            }

            return result;
        }

        private static String CleanDisplayName(String text)
        {
            if (String.IsNullOrEmpty(text))
                return String.Empty;

            try
            {
                text = FF9TextTool.RemoveOpCode(text);
            }
            catch
            {
            }

            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            while (text.Contains("  "))
                text = text.Replace("  ", " ");
            return text;
        }

        private static String GetPrimaryName(ModelNameInfo info, String geoName)
        {
            // Actual battle text is authoritative for enemy display names.
            if (info != null && info.Names.Count > 0)
            {
                NameUsage best = info.Names.Values
                    .OrderByDescending(entry => entry.Count)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (best != null && !String.IsNullOrEmpty(best.Name))
                    return best.Name;
            }

            // Named field/main/sub/NPC models only get a friendly name when the
            // GEO token clearly identifies a known character.
            return GetKnownCharacterName(geoName);
        }


        private static String GetKnownCharacterName(String geoName)
        {
            if (String.IsNullOrEmpty(geoName))
                return null;

            String token = geoName;
            Int32 split = geoName.LastIndexOf('_');
            if (split >= 0 && split + 1 < geoName.Length)
                token = geoName.Substring(split + 1);

            switch (token)
            {
                case "ZDN": return "Zidane";
                case "VIV": return "Vivi";
                case "GRN": return "Garnet";
                case "STN": return "Steiner";
                case "EIK": return "Eiko";
                case "KUI": return "Quina";
                case "FRJ": return "Freya";
                case "SLM": return "Amarant";

                case "BAK": return "Baku";
                case "BLN": return "Blank";
                case "CNA": return "Cinna";
                case "MRC": return "Marcus";
                case "BRN": return "Queen Brahne";
                case "RBY": return "Ruby";
                case "TOT": return "Doctor Tot";
                case "ZON": return "Zorn";
                case "KJA": return "Kuja";
                case "CID": return "Cid";
                case "GRL": return "Garland";
                case "FLT": return "Sir Fratley";
                case "BW1": return "Black Waltz 1";
                case "BW2": return "Black Waltz 2";
                case "BW3": return "Black Waltz 3";
                case "ZNR": return "Zenero";
            }

            return null;
        }


        private static Int32 DumpModelTextures(String geoName, String outputFolder)
        {
            GameObject model = null;
            HashSet<String> written = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            Int32 count = 0;

            try
            {
                model = ModelFactory.CreateModel(geoName, false, false, -1);
                if (model == null)
                {
                    Log.Warning($"[ASSET DUMPER] ModelFactory returned null for {geoName}.");
                    return 0;
                }

                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                Int32 rendererIndex = 0;

                foreach (Renderer renderer in renderers)
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (Int32 materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        Material material = materials[materialIndex];
                        if (material == null || material.mainTexture == null)
                            continue;

                        Texture texture = material.mainTexture;
                        String textureName = !String.IsNullOrEmpty(texture.name)
                            ? SanitizeFileName(texture.name)
                            : $"renderer_{rendererIndex:D2}_material_{materialIndex:D2}";

                        String uniqueKey = texture.GetInstanceID().ToString(CultureInfo.InvariantCulture);
                        if (!written.Add(uniqueKey))
                            continue;

                        String outputPath = GetUniquePngPath(outputFolder, textureName);
                        WriteTextureToPng(texture, outputPath);
                        count++;
                    }

                    rendererIndex++;
                }
            }
            finally
            {
                if (model != null)
                    UnityEngine.Object.Destroy(model);
            }

            return count;
        }

        private static void WriteTextureToPng(Texture source, String outputPath)
        {
            Int32 width = Math.Max(1, source.width);
            Int32 height = Math.Max(1, source.height);

            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Texture2D readable = null;

            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;

                readable = new Texture2D(width, height, TextureFormat.ARGB32, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);

                Byte[] png = readable.EncodeToPNG();
                File.WriteAllBytes(outputPath, png);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (readable != null)
                    UnityEngine.Object.Destroy(readable);
            }
        }

        private static String GetModelCategory(String geoName)
        {
            if (String.IsNullOrEmpty(geoName))
                return "99_Other";

            if (geoName.StartsWith("GEO_MON_", StringComparison.Ordinal))
                return "01_Enemies";
            if (geoName.StartsWith("GEO_MAIN_", StringComparison.Ordinal))
                return "02_Main_Characters";
            if (geoName.StartsWith("GEO_SUB_", StringComparison.Ordinal))
                return "03_Sub_Characters";
            if (geoName.StartsWith("GEO_NPC_", StringComparison.Ordinal))
                return "04_NPCs";
            if (geoName.StartsWith("GEO_WEP_", StringComparison.Ordinal))
                return "05_Weapons";
            if (geoName.StartsWith("GEO_ACC_", StringComparison.Ordinal))
                return "06_Accessories";

            return "99_Other";
        }

        private static String GetUniquePngPath(String folder, String baseName)
        {
            String path = Path.Combine(folder, baseName + ".png");
            if (!File.Exists(path))
                return path;

            for (Int32 i = 1; ; i++)
            {
                path = Path.Combine(folder, baseName + "_" + i.ToString(CultureInfo.InvariantCulture) + ".png");
                if (!File.Exists(path))
                    return path;
            }
        }







        private static Int32 ParseNumericFileName(String path)
        {
            Int32 value;
            return Int32.TryParse(Path.GetFileNameWithoutExtension(path), out value) ? value : Int32.MaxValue;
        }

        private static void WriteGeoKey(
            String keyPath,
            Int32 modelId,
            String geoName,
            String clearName,
            ModelNameInfo nameInfo)
        {
            using (StreamWriter writer = new StreamWriter(keyPath, false))
            {
                writer.WriteLine($"Model ID: {modelId}");
                writer.WriteLine($"GEO Key: {geoName}");

                if (!String.IsNullOrEmpty(clearName))
                    writer.WriteLine($"Display Name: {clearName}");
                else
                    writer.WriteLine("Display Name: [No clear canonical name - folder falls back to GEO key]");

                if (nameInfo != null && nameInfo.Names.Count > 0)
                {
                    writer.WriteLine();
                    writer.WriteLine("Battle Name Usage:");

                    foreach (NameUsage usage in nameInfo.Names.Values
                        .OrderByDescending(entry => entry.Count)
                        .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        writer.WriteLine($"  {usage.Name} ({usage.Count})");
                        foreach (String battleScene in usage.BattleScenes.OrderBy(value => value))
                            writer.WriteLine($"    {battleScene}");
                    }
                }
            }
        }











        private static String CombinePath(params String[] parts)
        {
            if (parts == null || parts.Length == 0)
                return String.Empty;

            String result = parts[0];
            for (Int32 i = 1; i < parts.Length; i++)
                result = Path.Combine(result, parts[i]);
            return result;
        }

        private static String SanitizeFileName(String value)
        {
            if ((value == null || value.Trim().Length == 0))
                return "Unknown";

            foreach (Char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');

            value = value.Trim().TrimEnd('.');
            while (value.Contains("  "))
                value = value.Replace("  ", " ");

            return String.IsNullOrEmpty(value) ? "Unknown" : value;
        }

        private static void WriteIndexLine(
            StreamWriter writer,
            Int32 modelId,
            String clearName,
            String geoName,
            Int32 textureCount,
            String status,
            String folder)
        {
            writer.WriteLine(
                Csv(modelId.ToString(CultureInfo.InvariantCulture)) + "," +
                Csv(String.IsNullOrEmpty(clearName) ? String.Empty : clearName) + "," +
                Csv(geoName) + "," +
                Csv(textureCount.ToString(CultureInfo.InvariantCulture)) + "," +
                Csv(status) + "," +
                Csv(folder));
        }


        private static String Csv(String value)
        {
            if (value == null)
                return String.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    
        private static void Report(Action<String> status, String text)
        {
            if (status != null)
                status(text);
        }
    }
}
