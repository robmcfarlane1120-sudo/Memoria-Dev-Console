using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Memoria.Prime;

namespace Memoria.DevConsole
{
    // Lightweight metadata export, isolated from the original texture export.
    internal static class BattleBackgroundDiagnostics
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static void Export(GameObject root, string directory)
        {
            // Retain counts only: no vertex/UV arrays or per-pixel buffers are needed.
            var countsByMesh = new Dictionary<Mesh, int[]>();
            using (var meshes = new StreamWriter(Path.Combine(directory, "MeshMap.csv"), false))
            {
                Row(meshes, "HierarchyPath", "GameObject", "Renderer", "RendererPath", "RendererType", "RendererEnabled", "ActiveInHierarchy", "Mesh", "Submesh", "MaterialIndex", "Texture", "VertexCount", "TriangleCount", "LocalPosition", "LocalRotationQuaternion", "LocalScale", "WorldPosition", "WorldRotationQuaternion", "LocalBoundsCenter", "LocalBoundsSize", "WorldBoundsCenter", "WorldBoundsSize", "LocalToWorldMatrixRowMajor", "Status");
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                MeshRenderer[] originalRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null) continue;
                    try
                    {
                        var filter = renderer.GetComponent<MeshFilter>();
                        var skin = renderer as SkinnedMeshRenderer;
                        Mesh mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                        string path = Hierarchy(renderer.transform, root.transform);
                        string status = mesh != null ? "OK" : "Missing mesh or unsupported renderer";
                        int[] triangleCounts = null;
                        if (mesh != null && !countsByMesh.TryGetValue(mesh, out triangleCounts))
                        {
                            triangleCounts = new int[mesh.subMeshCount];
                            for (int submesh = 0; submesh < triangleCounts.Length; submesh++)
                            {
                                try { triangleCounts[submesh] = mesh.GetTriangles(submesh).Length / 3; }
                                catch (Exception ex)
                                {
                                    triangleCounts[submesh] = -1;
                                    Warn(path + ": cannot read submesh " + submesh + ": " + ex.Message);
                                }
                            }
                            countsByMesh.Add(mesh, triangleCounts);
                        }
                        if (mesh == null) Warn(path + ": " + status);
                        Material[] materials = renderer.sharedMaterials;
                        int slots = Math.Max(1, Math.Max(materials.Length, mesh != null ? mesh.subMeshCount : 0));
                        int originalIndex = Array.IndexOf(originalRenderers, renderer as MeshRenderer);
                        Transform t = renderer.transform;
                        Bounds local = mesh != null ? mesh.bounds : new Bounds();
                        for (int slot = 0; slot < slots; slot++)
                        {
                            // Unity redraws the last submesh for excess material slots.
                            int submesh = mesh != null && mesh.subMeshCount > 0 ? Math.Min(slot, mesh.subMeshCount - 1) : -1;
                            Material material = slot < materials.Length ? materials[slot] : null;
                            Texture texture = material != null ? material.mainTexture : null;
                            string textureName = texture == null ? "" : !string.IsNullOrEmpty(texture.name) ? texture.name : "unnamed_texture_" + originalIndex + "_" + slot;
                            string png = texture == null ? "" : Sanitize(textureName) + ".png";
                            int triangles = triangleCounts != null && submesh >= 0 ? triangleCounts[submesh] : -1;
                            string rowStatus = status;
                            if (mesh != null && submesh < 0) rowStatus = "Mesh has no submeshes";
                            else if (mesh != null && triangles < 0) rowStatus = "Triangle count unavailable";
                            if (material == null) rowStatus += "; missing material";
                            Row(meshes, path, renderer.gameObject.name, renderer.name, path, renderer.GetType().Name, renderer.enabled, renderer.gameObject.activeInHierarchy, mesh != null ? mesh.name : "", submesh, slot, png, mesh != null ? (object)mesh.vertexCount : "", triangles >= 0 ? (object)triangles : "", Vec(t.localPosition), Quat(t.localRotation), Vec(t.localScale), Vec(t.position), Quat(t.rotation), mesh != null ? Vec(local.center) : "", mesh != null ? Vec(local.size) : "", Vec(renderer.bounds.center), Vec(renderer.bounds.size), Matrix(t.localToWorldMatrix), rowStatus);
                        }
                    }
                    catch (Exception ex) { Warn("Renderer " + renderer.name + ": " + ex.Message); }
                }
            }
        }
        private static string Hierarchy(Transform node, Transform root)
        {
            var parts = new List<string>();
            while (node != null) { parts.Add(Uri.EscapeDataString(node.name) + "[" + (node == root ? 0 : node.GetSiblingIndex()) + "]"); if (node == root) break; node = node.parent; }
            parts.Reverse(); return string.Join("/", parts.ToArray());
        }
        private static string Sanitize(string value) { foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); return value; }
        private static string Vec(Vector3 v) { return v.x.ToString("R", Invariant) + ";" + v.y.ToString("R", Invariant) + ";" + v.z.ToString("R", Invariant); }
        private static string Quat(Quaternion q) { return q.x.ToString("R", Invariant) + ";" + q.y.ToString("R", Invariant) + ";" + q.z.ToString("R", Invariant) + ";" + q.w.ToString("R", Invariant); }
        private static string Matrix(Matrix4x4 m) { var parts = new string[16]; for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) parts[r * 4 + c] = m[r, c].ToString("R", Invariant); return string.Join(";", parts); }
        private static void Row(StreamWriter writer, params object[] values) { writer.WriteLine(string.Join(",", values.Select(v => "\"" + Convert.ToString(v, Invariant).Replace("\"", "\"\"") + "\"").ToArray())); }
        private static void Warn(string message) { Log.Warning("[BATTLE BG DIAGNOSTICS] " + message); }
    }
}