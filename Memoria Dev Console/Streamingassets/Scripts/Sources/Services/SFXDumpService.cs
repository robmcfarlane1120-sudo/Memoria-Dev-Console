using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Memoria.Data;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    public static class SFXDumpService
    {
        public static void Update()
        {
            try
            {
                DumpAllSFX();
            }
            catch (Exception ex)
            {
                Log.Error("[MDC][SFXDump] FAILED: " + ex);
            }
        }

        private static void DumpAllSFX()
        {
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
                    "SFXDump"
                );

            Directory.CreateDirectory(outputRoot);

            Log.Message(
                "[MDC][SFXDump] Starting full SFX dump."
            );

            Log.Message(
                "[MDC][SFXDump] Output directory: " +
                outputRoot
            );

            HashSet<Int32> effectIds =
                new HashSet<Int32>();

            Array values =
                Enum.GetValues(
                    typeof(SpecialEffect)
                );

            foreach (System.Object value in values)
            {
                Int32 id =
                    (Int32)value;

                if (id >= 0)
                    effectIds.Add(id);
            }

            List<Int32> sortedIds =
                new List<Int32>(
                    effectIds
                );

            sortedIds.Sort();

            Int32 successCount = 0;
            Int32 missingCount = 0;
            Int32 failedCount = 0;

            StringBuilder index =
                new StringBuilder();

            index.AppendLine(
                "ID,Name,RawBytes,Chunks,Status"
            );

            foreach (Int32 id in sortedIds)
            {
                String effectName =
                    Enum.GetName(
                        typeof(SpecialEffect),
                        id
                    );

                if (String.IsNullOrEmpty(effectName))
                    effectName = "Unknown";

                String effectFolderName =
                    "ef" +
                    id.ToString("D3");

                String assetPath =
                    "SpecialEffects/" +
                    effectFolderName;

                Byte[] data = null;

                try
                {
                    data =
                        AssetManager.LoadBytes(
                            assetPath,
                            true
                        );
                }
                catch (Exception ex)
                {
                    failedCount++;

                    index.AppendLine(
                        id + "," +
                        Csv(effectName) + ",0,0," +
                        Csv("LOAD ERROR: " + ex.Message)
                    );

                    Log.Warning(
                        "[MDC][SFXDump] LOAD ERROR " +
                        effectFolderName +
                        ": " +
                        ex.Message
                    );

                    continue;
                }

                if (
                    data == null ||
                    data.Length == 0
                )
                {
                    missingCount++;

                    index.AppendLine(
                        id + "," +
                        Csv(effectName) +
                        ",0,0,Missing"
                    );

                    continue;
                }

                String effectOutput =
                    Path.Combine(
                        outputRoot,
                        effectFolderName
                    );

                Directory.CreateDirectory(
                    effectOutput
                );

                File.WriteAllBytes(
                    Path.Combine(
                        effectOutput,
                        effectFolderName +
                        ".bytes"
                    ),
                    data
                );

                try
                {
                    SFXBinaryFile sfx =
                        new SFXBinaryFile();

                    sfx.Load(data);

                    WriteInfo(
                        effectOutput,
                        id,
                        effectName,
                        data.Length,
                        sfx
                    );

                    WriteSequence(
                        effectOutput,
                        sfx
                    );

                    DumpSmallFiles(
                        effectOutput,
                        sfx
                    );

                    Int32 chunkCount =
                        sfx.chunkCount;

                    index.AppendLine(
                        id + "," +
                        Csv(effectName) + "," +
                        data.Length + "," +
                        chunkCount +
                        ",OK"
                    );

                    successCount++;

                    Log.Message(
                        "[MDC][SFXDump] OK " +
                        effectFolderName +
                        " (" +
                        effectName +
                        ") = " +
                        data.Length +
                        " bytes"
                    );
                }
                catch (Exception ex)
                {
                    failedCount++;

                    index.AppendLine(
                        id + "," +
                        Csv(effectName) + "," +
                        data.Length +
                        ",0," +
                        Csv("PARSE ERROR: " + ex.Message)
                    );

                    File.WriteAllText(
                        Path.Combine(
                            effectOutput,
                            "ParseError.txt"
                        ),
                        ex.ToString()
                    );

                    Log.Warning(
                        "[MDC][SFXDump] PARSE ERROR " +
                        effectFolderName +
                        ": " +
                        ex.Message
                    );
                }
            }

            File.WriteAllText(
                Path.Combine(
                    outputRoot,
                    "SFXIndex.csv"
                ),
                index.ToString()
            );

            Log.Message(
                "[MDC][SFXDump] Complete. OK=" +
                successCount +
                " Missing=" +
                missingCount +
                " Failed=" +
                failedCount
            );
        }

        private static void WriteInfo(
            String effectOutput,
            Int32 id,
            String effectName,
            Int32 rawSize,
            SFXBinaryFile sfx
        )
        {
            StringBuilder info =
                new StringBuilder();

            info.AppendLine(
                "ID: " + id
            );

            info.AppendLine(
                "Name: " + effectName
            );

            info.AppendLine(
                "Raw bytes: " + rawSize
            );

            info.AppendLine(
                "Chunk count: " +
                sfx.chunkCount
            );

            info.AppendLine(
                "Sequence entries: " +
                sfx.sequence.Count
            );

            info.AppendLine(
                "Camera files: " +
                sfx.cameraFileList.Count
            );

            info.AppendLine(
                "AKAO files: " +
                sfx.akaoFileList.Count
            );

            File.WriteAllText(
                Path.Combine(
                    effectOutput,
                    "Info.txt"
                ),
                info.ToString()
            );
        }

        private static void WriteSequence(
            String effectOutput,
            SFXBinaryFile sfx
        )
        {
            StringBuilder sequence =
                new StringBuilder();

            for (
                Int32 i = 0;
                i < sfx.sequence.Count;
                i++
            )
            {
                SFXBinaryFile.SequenceCode code =
                    sfx.sequence[i];

                sequence.Append(
                    i.ToString("D4")
                );

                sequence.Append(
                    " | "
                );

                sequence.Append(
                    code.code
                );

                sequence.Append(
                    " | arg1="
                );

                sequence.Append(
                    code.arg1
                );

                sequence.Append(
                    " | arg2="
                );

                sequence.AppendLine(
                    code.arg2.ToString()
                );
            }

            File.WriteAllText(
                Path.Combine(
                    effectOutput,
                    "Sequence.txt"
                ),
                sequence.ToString()
            );
        }

        private static void DumpSmallFiles(
            String effectOutput,
            SFXBinaryFile sfx
        )
        {
            for (
                Int32 chunk = 0;
                chunk < sfx.smallFileRaw.Count;
                chunk++
            )
            {
                List<Byte[]> files =
                    sfx.smallFileRaw[chunk];

                if (
                    files == null ||
                    files.Count == 0
                )
                    continue;

                String chunkFolder =
                    Path.Combine(
                        effectOutput,
                        "chunk_" +
                        chunk.ToString("D2")
                    );

                Directory.CreateDirectory(
                    chunkFolder
                );

                for (
                    Int32 file = 0;
                    file < files.Count;
                    file++
                )
                {
                    Byte[] bytes =
                        files[file];

                    if (bytes == null)
                        continue;

                    String dumpPath =
                        Path.Combine(
                            chunkFolder,
                            "file_" +
                            file.ToString("D3") +
                            ".bin"
                        );

                    File.WriteAllBytes(
                        dumpPath,
                        bytes
                    );
                }
            }
        }

        private static String Csv(String value)
        {
            if (value == null)
                return "\"\"";

            return
                "\"" +
                value.Replace(
                    "\"",
                    "\"\""
                ) +
                "\"";
        }
    }
}
