using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Memoria.BattleSceneLayouts.Json;
using UnityEngine;

namespace Memoria.BattleSceneLayouts
{
    public sealed class LayoutUnit
    {
        public Boolean Party;
        public Int32 Slot;
        public String Name;
        public Vector3 Home;
        public Single Scale = 1f;
        public Boolean HasRotation;
        public Vector3 Rotation;
        public JObject ToJson()
        {
            JObject result = new JObject(new JProperty("party", Party), new JProperty("slot", Slot),
                new JProperty("name", Name), new JProperty("home", BattleLayout.Vector(Home)),
                new JProperty("scale", Scale));
            if (HasRotation) result["rotation"] = BattleLayout.Vector(Rotation);
            return result;
        }
    }

    public sealed class LayoutCamera
    {
        public Boolean Enabled = true;
        public Vector3 Position, Angles;
        // Keep the exact PSX camera basis, including its handedness. Euler controls
        // are offsets from this basis, not a lossy conversion to a Unity transform.
        public Matrix4x4 Basis = Matrix4x4.identity;
        public Single Fov = 45f;
        public Single SfxZoom = 1f;
        public LayoutCamera Clone() { return (LayoutCamera)MemberwiseClone(); }
        public JObject ToJson()
        {
            JArray matrix = new JArray(); for (Int32 i = 0; i < 16; i++) matrix.Add(Basis[i]);
            return new JObject(new JProperty("enabled", Enabled), new JProperty("position", BattleLayout.Vector(Position)), new JProperty("angleOffsets", BattleLayout.Vector(Angles)), new JProperty("verticalFov", Fov), new JProperty("sfxCameraZoom", SfxZoom), new JProperty("basis", matrix));
        }
    }

    public sealed class BattleLayout
    {
        public Int32 BattleId, Pattern;
        public Single StageScale = 1f;
        public LayoutCamera Camera = new LayoutCamera();
        public readonly List<LayoutUnit> Units = new List<LayoutUnit>();
        private static Single Determinant(Matrix4x4 m) { return m.m00*(m.m11*m.m22-m.m12*m.m21)-m.m01*(m.m10*m.m22-m.m12*m.m20)+m.m02*(m.m10*m.m21-m.m11*m.m20); }
        public static JArray Vector(Vector3 v) { return new JArray(v.x, v.y, v.z); }
        private static Single Number(JToken t, Single min, Single max)
        {
            Single v;
            if (t == null || !Single.TryParse(t.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || Single.IsNaN(v) || Single.IsInfinity(v) || v < min || v > max)
                throw new FormatException("Invalid or out-of-range layout number.");
            return v;
        }
        private static Int32 Integer(JToken t, Int32 min, Int32 max)
        { Single value = Number(t, min, max); if (value != Math.Floor(value)) throw new FormatException("Expected an integer layout identifier."); return (Int32)value; }
        private static Vector3 ReadVector(JToken t)
        {
            if (!(t is JArray) || t.Count != 3) throw new FormatException("Expected XYZ array.");
            return new Vector3(Number(t[0], -30000, 30000), Number(t[1], -30000, 30000), Number(t[2], -30000, 30000));
        }
        public JObject ToJson()
        {
            JArray units = new JArray(); foreach (LayoutUnit unit in Units) units.Add(unit.ToJson());
            return new JObject(new JProperty("version", 1), new JProperty("battleId", BattleId), new JProperty("pattern", Pattern), new JProperty("stageScale", StageScale), new JProperty("camera", Camera.ToJson()), new JProperty("units", units));
        }
        public BattleLayout Clone() { return Parse(ToJson().ToString()); }
        public static BattleLayout Parse(String json)
        {
            JObject j = JObject.Parse(json);
            if ((Int32)j["version"] != 1) throw new FormatException("Unsupported layout version.");
            BattleLayout layout = new BattleLayout { BattleId = Integer(j["battleId"], 0, 65535), Pattern = Integer(j["pattern"], 0, 255) };
            if (j["stageScale"] != null) layout.StageScale = Number(j["stageScale"], 0.1f, 5f);
            JObject cam = j["camera"] as JObject;
            if (cam == null) throw new FormatException("Missing camera.");
            layout.Camera.Enabled = (Boolean)cam["enabled"];
            layout.Camera.Position = ReadVector(cam["position"]); layout.Camera.Angles = ReadVector(cam["angleOffsets"]);
            layout.Camera.Fov = Number(cam["verticalFov"], 5, 120);
            if (cam["sfxCameraZoom"] != null) layout.Camera.SfxZoom = Number(cam["sfxCameraZoom"], 1f, 3f);
            JArray basis = cam["basis"] as JArray;
            if (basis == null || basis.Count != 16) throw new FormatException("Invalid camera basis.");
            for (Int32 i = 0; i < 16; i++) layout.Camera.Basis[i] = Number(basis[i], -30000, 30000);
            Matrix4x4 m = layout.Camera.Basis;
            if (Math.Abs(Math.Abs(Determinant(m)) - 1f) > 0.1f || Math.Abs(m.m30) + Math.Abs(m.m31) + Math.Abs(m.m32) > 0.001f || Math.Abs(m.m33 - 1f) > 0.001f)
                throw new FormatException("Invalid native camera basis.");
            JArray units = j["units"] as JArray;
            if (units == null || units.Count > 16) throw new FormatException("Invalid unit list.");
            HashSet<String> keys = new HashSet<String>();
            foreach (JToken u in units)
            {
                LayoutUnit unit = new LayoutUnit { Party = (Boolean)u["party"], Slot = Integer(u["slot"], 0, 15), Name = (String)u["name"], Home = ReadVector(u["home"]) };
                if (u["scale"] != null) unit.Scale = Number(u["scale"], 0.1f, 5f);
                if (u["rotation"] != null)
                {
                    unit.Rotation = ReadVector(u["rotation"]);
                    if (Math.Abs(unit.Rotation.x) > 360 || Math.Abs(unit.Rotation.y) > 360 ||
                        Math.Abs(unit.Rotation.z) > 360)
                        throw new FormatException("Model rotation must be between -360 and 360 degrees.");
                    unit.HasRotation = true;
                }
                if (unit.Party && unit.Slot > 3) throw new FormatException("Party slot must be 0-3.");
                if (!keys.Add(unit.Party + ":" + unit.Slot)) throw new FormatException("Duplicate formation slot.");
                layout.Units.Add(unit);
            }
            return layout;
        }
        public static void Save(String path, BattleLayout layout)
        {
            String content = layout.ToJson().ToString(Formatting.Indented);
            Parse(content); // Validate before replacing an existing preset.
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            String temp = path + ".tmp"; File.WriteAllText(temp, content);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
        }
    }
}
