using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Memoria.DevConsole
{
    internal static class FieldNativeDepthCorrections
    {
        internal static Byte[] Resolve(String name, Byte[] bgs)
        {
            // The same loader/exporter may apply depth corrections after reading
            // the source bytes. Capture its actual scene rather than guessing offsets
            // or accepting arbitrary differences in the editing BGX.
            BGSCENE_DEF scene = new BGSCENE_DEF(true);
            scene.LoadResources(FieldMap.GetMapResourcePath(name), name);
            if (scene.isPureMemoriaScene || scene.ebgBin == null)
                throw new IOException("Native cutting requires an original BGS source, not a BGX replacement.");
            if (!scene.ebgBin.SequenceEqual(bgs))
                throw new IOException("The installed source BGS differs from this export. Export the source field again.");
            List<Int32[]> layout = new List<Int32[]>();
            foreach (BGOVERLAY_DEF overlay in scene.overlayList)
            {
                Int32[] values = new Int32[overlay.spriteList.Count + 2];
                values[0] = overlay.orgZ; values[1] = overlay.curZ;
                for (Int32 i = 0; i < overlay.spriteList.Count; i++) values[i + 2] = overlay.spriteList[i].depth;
                layout.Add(values);
            }
            return Bake(bgs, scene.orgZ, scene.curZ, layout);
        }

        // Store the effective depth values without changing positions, tile order,
        // parameters, cameras or animation. This is used only for renamed fields.
        internal static Byte[] Bake(Byte[] original, Int32 sceneOrg, Int32 sceneCur, IList<Int32[]> layout)
        {
            FieldNativeAtlas.ReadTiles(original);
            Int32 count = BitConverter.ToUInt16(original, 6), start = checked((Int32)BitConverter.ToUInt32(original, 16));
            if (layout.Count != count || sceneOrg < Int16.MinValue || sceneOrg > Int16.MaxValue ||
                sceneCur < Int16.MinValue || sceneCur > Int16.MaxValue)
                throw new IOException("Invalid effective BGS depth layout.");
            Byte[] result = (Byte[])original.Clone();
            Buffer.BlockCopy(BitConverter.GetBytes((Int16)sceneOrg), 0, result, 28, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((Int16)sceneCur), 0, result, 30, 2);
            for (Int32 i = 0; i < count; i++)
            {
                Int32 at = start + i * 56;
                Int32 n = (Int32)(BitConverter.ToUInt32(original, at + 36) >> 16);
                Int32[] values = layout[i];
                if (values == null || values.Length != n + 2)
                    throw new IOException("Effective BGS tile count differs at overlay " + i + ".");
                // Memoria can give individual tiles signed depth offsets. Move a
                // common amount into the overlay base so all 12-bit BGS fields fit,
                // while retaining each tile's effective scene-relative depth.
                Int32 minimum = n == 0 ? 0 : values.Skip(2).Min();
                Int32 maximum = n == 0 ? 0 : values.Skip(2).Max();
                Int32 low = Math.Max(maximum - 4095, Math.Max(-values[0], -values[1]));
                Int32 high = Math.Min(minimum, Math.Min(4095 - values[0], 4095 - values[1]));
                if (low > high) throw new IOException("Effective depth span cannot fit native BGS at overlay " + i + ".");
                Int32 shift = Math.Max(low, Math.Min(0, high));
                UInt32 header = BitConverter.ToUInt32(original, at);
                Write(result, at, (header & 255u) | ((UInt32)(values[1] + shift) << 8) | ((UInt32)(values[0] + shift) << 20));
                Int32 loc = checked((Int32)BitConverter.ToUInt32(original, at + 40));
                for (Int32 j = 0; j < n; j++)
                {
                    UInt32 tile = BitConverter.ToUInt32(original, loc + j * 4);
                    Write(result, loc + j * 4, (tile & 0xFFFFF000u) | (UInt32)(values[j + 2] - shift));
                }
            }
            return result;
        }

        private static void Write(Byte[] bytes, Int32 at, UInt32 value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, at, 4);
        }
    }
}