using System;
using System.Collections.Generic;
using System.IO;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    public static class BattleIDByteDumpService
    {
        public static void Update()
        {
            Log.Message(
                "[MDC][BattleIDByteDump] Update entered."
            );

            try
            {
                DumpBattleScripts();
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MDC][BattleIDByteDump] FAILED: " + ex
                );
            }
        }

        private static void DumpBattleScripts()
        {
            Log.Message(
                "[MDC][BattleIDByteDump] Starting AssetManager bundle scan."
            );

            /*
             * Application.dataPath:
             *
             * FINAL FANTASY IX\x64\FF9_Data
             *
             * First ".." gets us:
             * FINAL FANTASY IX\x64
             *
             * Second ".." gets us:
             * FINAL FANTASY IX
             *
             * Nested Path.Combine calls are intentional because
             * this compiler does not support the 3-argument overload.
             */

            String x64Root =
                Path.GetFullPath(
                    Path.Combine(
                        Application.dataPath,
                        ".."
                    )
                );

            String gameRoot =
                Path.GetFullPath(
                    Path.Combine(
                        x64Root,
                        ".."
                    )
                );

            String mdcRoot =
                Path.Combine(
                    gameRoot,
                    "Memoria Dev Console"
                );

            String assetDumpRoot =
                Path.Combine(
                    mdcRoot,
                    "Asset Dump"
                );

            String outputRoot =
                Path.Combine(
                    assetDumpRoot,
                    "BattleScriptDump"
                );

            Directory.CreateDirectory(
                outputRoot
            );

            Log.Message(
                "[MDC][BattleIDByteDump] Game root: " +
                gameRoot
            );

            Log.Message(
                "[MDC][BattleIDByteDump] Output directory: " +
                outputRoot
            );

            List<BattleScriptInfo> results =
                new List<BattleScriptInfo>();

            Int32 sceneCount = 0;
            Int32 successCount = 0;
            Int32 failCount = 0;

            foreach (
                KeyValuePair<String, Int32> pair
                in FF9BattleDB.SceneData
            )
            {
                String sceneKey = pair.Key;

                if (String.IsNullOrEmpty(sceneKey))
                    continue;

                if (!sceneKey.StartsWith(
                    "BSC_",
                    StringComparison.Ordinal
                ))
                    continue;

                sceneCount++;

                String battleName =
                    sceneKey.Substring(4);

                String eventName =
                    "EVT_BATTLE_" +
                    battleName;

                String assetPath =
                    "CommonAsset/EventEngine/EventBinary/" +
                    "Battle/" +
                    "US/" +
                    eventName +
                    ".eb";

                Byte[] data = null;

                try
                {
                    /*
                     * Use Memoria's full asset resolver.
                     *
                     * LoadBytesMultiple handles the actual
                     * Memoria asset/bundle/archive lookup path.
                     */

                    foreach (
                        Byte[] candidate
                        in AssetManager.LoadBytesMultiple(
                            assetPath
                        )
                    )
                    {
                        if (
                            candidate != null &&
                            candidate.Length > 0
                        )
                        {
                            data = candidate;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[MDC][BattleIDByteDump] ERROR: " +
                        battleName +
                        " | " +
                        ex.Message
                    );

                    failCount++;

                    continue;
                }

                if (
                    data == null ||
                    data.Length == 0
                )
                {
                    failCount++;

                    continue;
                }

                BattleScriptInfo info =
                    new BattleScriptInfo();

                info.BattleID =
                    pair.Value;

                info.Name =
                    battleName;

                info.Size =
                    data.Length;

                results.Add(
                    info
                );

                successCount++;

                String dumpFile =
                    eventName +
                    ".eb.bytes";

                String dumpPath =
                    Path.Combine(
                        outputRoot,
                        dumpFile
                    );

                File.WriteAllBytes(
                    dumpPath,
                    data
                );

                Log.Message(
                    "[MDC][BattleIDByteDump] OK: " +
                    battleName +
                    " = " +
                    data.Length +
                    " bytes"
                );
            }

            /*
             * Sort largest -> smallest.
             */

            results.Sort(
                delegate(
                    BattleScriptInfo a,
                    BattleScriptInfo b
                )
                {
                    return b.Size.CompareTo(
                        a.Size
                    );
                }
            );

            String csvPath =
                Path.Combine(
                    outputRoot,
                    "BattleScriptSizes.csv"
                );

            using (
                StreamWriter writer =
                    new StreamWriter(
                        csvPath,
                        false
                    )
            )
            {
                writer.WriteLine(
                    "BattleID,Battle,Bytes"
                );

                foreach (
                    BattleScriptInfo info
                    in results
                )
                {
                    writer.WriteLine(
                        info.BattleID +
                        "," +
                        info.Name +
                        "," +
                        info.Size
                    );
                }
            }

            Log.Message(
                "[MDC][BattleIDByteDump] ========================"
            );

            Log.Message(
                "[MDC][BattleIDByteDump] COMPLETE"
            );

            Log.Message(
                "[MDC][BattleIDByteDump] Scenes: " +
                sceneCount
            );

            Log.Message(
                "[MDC][BattleIDByteDump] Loaded: " +
                successCount
            );

            Log.Message(
                "[MDC][BattleIDByteDump] Failed: " +
                failCount
            );

            Log.Message(
                "[MDC][BattleIDByteDump] Output: " +
                outputRoot
            );

            Log.Message(
                "[MDC][BattleIDByteDump] CSV: " +
                csvPath
            );

            Log.Message(
                "[MDC][BattleIDByteDump] ========================"
            );
        }

        private sealed class BattleScriptInfo
        {
            public Int32 BattleID;
            public String Name;
            public Int32 Size;
        }
    }
}