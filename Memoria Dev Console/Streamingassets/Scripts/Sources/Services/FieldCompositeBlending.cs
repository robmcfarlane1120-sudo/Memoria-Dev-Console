using System;
using UnityEngine;

namespace Memoria.DevConsole
{
    internal enum FieldCompositeBlendMode { Normal, Add, Subtract }

    internal static class FieldCompositeBlending
    {
        internal static Boolean IsEffectShader(String shader)
        {
            return shader == "PSX/FieldMap_Abr_0" || shader == "PSX/FieldMap_Abr_1" ||
                shader == "PSX/FieldMap_Abr_2" || shader == "PSX/FieldMap_Abr_3";
        }

        internal static FieldCompositeBlendMode Parse(String shader)
        {
            // Native Abr_1 and Abr_3 use Blend One One with premultiplied RGB.
            // Abr_3 changes output alpha, not the RGB multiplier.
            if (shader == "PSX/FieldMap_Abr_1" || shader == "PSX/FieldMap_Abr_3")
                return FieldCompositeBlendMode.Add;
            if (shader == "PSX/FieldMap_Abr_2")
                return FieldCompositeBlendMode.Subtract;
            return FieldCompositeBlendMode.Normal;
        }

        internal static Color32 Blend(Color32 background, Color32 foreground, FieldCompositeBlendMode mode)
        {
            if (foreground.a == 0) return background;
            Single a = foreground.a / 255f, b = background.a / 255f;
            if (mode == FieldCompositeBlendMode.Normal)
            {
                Single outA = a + b * (1f - a);
                return new Color32(
                    Channel((foreground.r * a + background.r * b * (1f - a)) / outA),
                    Channel((foreground.g * a + background.g * b * (1f - a)) / outA),
                    Channel((foreground.b * a + background.b * b * (1f - a)) / outA),
                    Channel(outA * 255f));
            }
            // Work in premultiplied color, then return straight RGBA for the PNG.
            // Black effect pixels must not cover existing scenery.
            Single coverage = Math.Max(a, b);
            Single sign = mode == FieldCompositeBlendMode.Subtract ? -1f : 1f;
            return new Color32(
                Channel((background.r * b + sign * foreground.r * a) / coverage),
                Channel((background.g * b + sign * foreground.g * a) / coverage),
                Channel((background.b * b + sign * foreground.b * a) / coverage),
                Channel(coverage * 255f));
        }

        internal static Color32 ApplyArtworkDelta(Color32 original, Color32 reference, Color32 artwork)
        {
            // Keep effects and hidden source texture detail. An unchanged composite
            // is an exact pixel round-trip, including original alpha and hidden RGB.
            return new Color32(
                Channel(original.r + artwork.r - reference.r),
                Channel(original.g + artwork.g - reference.g),
                Channel(original.b + artwork.b - reference.b), original.a);
        }

        internal static void CompositeOnto(Color32[] destination, Int32 width, Int32 height,
            Color32[] source, Int32 sourceWidth, Int32 sourceHeight, Int32 left, Int32 top,
            FieldCompositeBlendMode mode)
        {
            for (Int32 y = 0; y < sourceHeight; y++)
            {
                Int32 dy = height - top - sourceHeight + y;
                if (dy < 0 || dy >= height) continue;
                for (Int32 x = 0; x < sourceWidth; x++)
                {
                    Int32 dx = left + x;
                    if (dx < 0 || dx >= width) continue;
                    Int32 at = dy * width + dx;
                    destination[at] = Blend(destination[at], source[y * sourceWidth + x], mode);
                }
            }
        }
        private static Byte Channel(Single value)
        {
            return (Byte)Math.Max(0, Math.Min(255, (Int32)Math.Round(value)));
        }
    }
}