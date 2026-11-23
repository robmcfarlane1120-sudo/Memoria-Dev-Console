using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Memoria.Assets;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    internal static class ModelViewerAnimationAliases
    {
        internal static HashSet<String> Resolve(String model, IDictionary<String, String> mappings)
        {
            var names = new HashSet<String>(StringComparer.Ordinal) { model };
            String canonical;
            if (!mappings.TryGetValue(model, out canonical)) canonical = model;
            foreach (var entry in mappings)
                if (String.Equals(entry.Value, canonical, StringComparison.Ordinal)) names.Add(entry.Key);
            return names;
        }

        internal static List<KeyValuePair<Int32, String>> Merge(
            IEnumerable<KeyValuePair<Int32, String>> original,
            IEnumerable<KeyValuePair<Int32, String>> database,
            HashSet<String> aliases, Func<KeyValuePair<Int32, String>, Boolean> load)
        {
            var result = new List<KeyValuePair<Int32, String>>();
            var seen = new HashSet<String>(StringComparer.Ordinal);
            foreach (var animation in original)
                if (seen.Add(animation.Value) && load(animation)) result.Add(animation);
            foreach (var animation in database)
            {
                if (seen.Contains(animation.Value)) continue;
                Boolean matches = false;
                foreach (String alias in aliases)
                    if (alias.StartsWith("GEO_", StringComparison.Ordinal) &&
                        animation.Value.StartsWith("ANH_" + alias.Substring(4) + "_", StringComparison.Ordinal))
                    { matches = true; break; }
                if (matches && seen.Add(animation.Value) && load(animation))
                {
                    result.Add(animation);
                }
            }
            return result;
        }

        internal static String AnimationModel(String animation)
        {
            if (String.IsNullOrEmpty(animation) || !animation.StartsWith("ANH_", StringComparison.Ordinal)) return null;
            String[] parts = animation.Split('_');
            return parts.Length < 5 ? null : "GEO_" + parts[1] + "_" + parts[2] + "_" + parts[3];
        }
    }

    public sealed partial class ModelViewerService
    {
        private GameObject _animationAliasModel;

        private void ExtendModelViewerAnimations()
        {
            GameObject model = GetStaticFieldValue(typeof(ModelViewerScene), "currentModel") as GameObject;
            if (model == null || model == _animationAliasModel) return;
            _animationAliasModel = model; // One pass per viewer model; no per-frame DB scan.
            try
            {
                IList models = GetStaticFieldValue(typeof(ModelViewerScene), "geoList") as IList;
                System.Object indexValue = GetStaticFieldValue(typeof(ModelViewerScene), "currentGeoIndex");
                if (models == null || indexValue == null) return;
                Int32 index = (Int32)indexValue;
                if (index < 0 || index >= models.Count) return;
                System.Object entry = models[index];
                Type entryType = entry.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo nameField = entryType.GetField("Name", flags), kindField = entryType.GetField("Kind", flags);
                FieldInfo normalKind = typeof(ModelViewerScene).GetField("MODEL_KIND_NORMAL", BindingFlags.Static | BindingFlags.NonPublic);
                if (nameField == null || kindField == null || normalKind == null ||
                    (Int32)kindField.GetValue(entry) != (Int32)normalKind.GetRawConstantValue()) return;
                String name = (String)nameField.GetValue(entry);
                var aliases = ModelViewerAnimationAliases.Resolve(name, ModelFactory.upscaleTable);
                if (aliases.Count < 2 && !name.StartsWith("GEO_MON_", StringComparison.Ordinal)) return;
                var original = GetStaticFieldValue(typeof(ModelViewerScene), "animList") as List<KeyValuePair<Int32, String>>;
                Animation animation = model.GetComponent<Animation>();
                if (original == null || animation == null) return;
                String selected = GetStaticFieldValue(typeof(ModelViewerScene), "currentAnimName") as String;
                var merged = ModelViewerAnimationAliases.Merge(original, FF9DBAll.AnimationDB, aliases, clip => {
                    try
                    {
                        AnimationFactory.AddAnimWithAnimatioName(model, clip.Value);
                        return animation.GetClip(clip.Value) != null;
                    }
                    catch (Exception ex)
                    {
                        ViewerDiagnostics.Warning("[Dev Console][Model Viewer] Skipped alias animation " + clip.Value + ": " + ex.Message);
                        return false;
                    }
                });
                SetStaticFieldValue(typeof(ModelViewerScene), "animList", merged);
                Int32 selectedIndex = merged.FindIndex(clip => clip.Value == selected);
                SetStaticFieldValue(typeof(ModelViewerScene), "currentAnimIndex", Math.Max(0, selectedIndex));
                if (selectedIndex < 0)
                {
                    String fallback = merged.Count == 0 ? "" : merged[0].Value;
                    SetStaticFieldValue(typeof(ModelViewerScene), "currentAnimName", fallback);
                    if (fallback.Length > 0) animation.Play(fallback);
                }
                // Do not replay/reset the selected clip or touch any live battle model.
                if (original.Count != merged.Count)
                    ViewerDiagnostics.Message("[Dev Console][Model Viewer] Animation catalog for " + name + ": " +
                        original.Count + " entries -> " + merged.Count + " distinct clips.");
            }
            catch (Exception ex)
            {
                ViewerDiagnostics.Warning("[Dev Console][Model Viewer] Keeping native animation catalog: " + ex.Message);
            }
        }
    }
}
