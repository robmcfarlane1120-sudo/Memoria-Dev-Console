using System;
using UnityEngine;

namespace Memoria.DevConsole
{
    internal static class FieldCompositeResampler
    {
        // Pixel-center sampling, retaining Unity's bottom-left row order.
        internal static Color32[] Resize(Color32[] source, Int32 sourceWidth, Int32 sourceHeight, Int32 width, Int32 height)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || width <= 0 || height <= 0 ||
                source == null || source.LongLength != (Int64)sourceWidth * sourceHeight)
                throw new ArgumentException("Invalid composite dimensions.");
            if (sourceWidth == width && sourceHeight == height) return source;
            Color32[] result = new Color32[checked(width * height)];
            for (Int32 y = 0; y < height; y++)
            {
                Double fy = Math.Max(0, Math.Min(sourceHeight - 1, (y + 0.5) * sourceHeight / height - 0.5));
                Int32 y0 = (Int32)fy, y1 = Math.Min(y0 + 1, sourceHeight - 1);
                Double ty = fy - y0;
                for (Int32 x = 0; x < width; x++)
                {
                    Double fx = Math.Max(0, Math.Min(sourceWidth - 1, (x + 0.5) * sourceWidth / width - 0.5));
                    Int32 x0 = (Int32)fx, x1 = Math.Min(x0 + 1, sourceWidth - 1);
                    Double tx = fx - x0;
                    Color32 a = source[y0 * sourceWidth + x0], b = source[y0 * sourceWidth + x1];
                    Color32 c = source[y1 * sourceWidth + x0], d = source[y1 * sourceWidth + x1];
                    result[y * width + x] = new Color32(
                        Blend(a.r, b.r, c.r, d.r, tx, ty), Blend(a.g, b.g, c.g, d.g, tx, ty),
                        Blend(a.b, b.b, c.b, d.b, tx, ty), Blend(a.a, b.a, c.a, d.a, tx, ty));
                }
            }
            return result;
        }

        private static Byte Blend(Byte a, Byte b, Byte c, Byte d, Double x, Double y)
        {
            Double bottom = a + (b - a) * x, top = c + (d - c) * x;
            return (Byte)Math.Round(bottom + (top - bottom) * y);
        }
    }
}
