using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Memoria.Assets;

namespace Memoria.DevConsole
{
    // A deliberately separate VM: no Obj instances, EventEngine calls, or live
    // variable writes. Only the explicit visual callback can affect the preview.
    internal sealed partial class FieldPreviewScript
    {
        internal sealed class Entry
        {
            internal byte[] Code;
            internal int Mod, Id;
        }
        private sealed class Frame
        {
            internal int IP, Tag, Level, Wait, Last;
            internal Actor Caller;
            internal Actor WaitingFor;
            internal int WaitingDepth;
        }
        private sealed class Actor
        {
            internal Entry Entry;
            internal int Uid;
            internal byte[] Locals = new byte[1024];
            internal int[] Properties = new int[8];
            internal readonly Stack<Frame> Calls = new Stack<Frame>();
            internal bool Failed;
        }
        private struct Value
        {
            internal int Number, Offset, Type;
            internal byte[] Memory;
            internal Func<int> Getter;
            internal Action<int> Setter;
            internal int Read()
            {
                if (Getter != null) return Getter();
                if (Memory == null) return Number;
                int i = Type < 2 ? Offset >> 3 : Offset;
                if (i < 0 || i >= Memory.Length) throw new FormatException("Variable outside preview memory");
                if (Type < 2) return (Memory[i] >> (Offset & 7)) & 1;
                int n = Type < 4 ? 3 : Type < 6 ? 1 : 2;
                if (i + n > Memory.Length) throw new FormatException("Truncated variable");
                int v = 0;
                for (int b = 0; b < n; b++) v |= Memory[i + b] << (8 * b);
                if (Type == 2 || Type == 3 || Type == 4 || Type == 6) v = (v << (32 - n * 8)) >> (32 - n * 8);
                return v;
            }
            internal void Write(int v)
            {
                if (Setter != null) { Setter(v); return; }
                Read();
                if (Memory == null) throw new FormatException("Assignment is not a variable");
                if (Type < 2)
                {
                    int i = Offset >> 3, mask = 1 << (Offset & 7);
                    Memory[i] = (byte)((Memory[i] & ~mask) | ((v & 1) << (Offset & 7)));
                }
                else
                {
                    int n = Type < 4 ? 3 : Type < 6 ? 1 : 2;
                    for (int b = 0; b < n; b++) Memory[Offset + b] = (byte)(v >> (b * 8));
                }
            }
        }
        private readonly List<Entry> _entries;
        private readonly List<int[]> _indexing;
        private readonly List<Actor> _actors = new List<Actor>();
        private readonly byte[] _globals, _map = new byte[1024];
        private readonly Action<int, int[], int> _visual;
        private readonly Random _random = new Random(1979);
        private readonly int _field;
        private int _camera, _control, _cursor;
        internal int ScreenX, ScreenY;
        private int[] _party = new int[] { -1, -1, -1, -1 };
        private readonly Dictionary<int, List<int>> _vectors = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, Dictionary<int, int>> _dictionaries = new Dictionary<int, Dictionary<int, int>>();
        internal int Ticks { get; private set; }
        internal readonly List<string> Diagnostics = new List<string>();

        internal FieldPreviewScript(List<Entry> entries, List<int[]> indexing, byte[] globals,
            int field, Action<int, int[], int> visual)
        {
            _entries = entries; _indexing = indexing;
            _globals = (byte[])globals.Clone(); _field = field; _visual = visual;
            Spawn(0, 0, 0);
        }

        internal static FieldPreviewScript Load(int field, Action<int, int[], int> visual)
        {
            string name = FF9DBAll.EventDB[field];
            var files = AssetManager.LoadEventScriptFiles("CommonAsset/EventEngine/EventBinary/Field/" + Localization.CurrentSymbol + "/" + name + ".eb");
            List<Entry> entries; List<int[]> indexing;
            ReadFiles(files, out entries, out indexing);
            var result = new FieldPreviewScript(entries, indexing, FF9StateSystem.EventState.gEventGlobal, field, visual);
            for (int i = 0; i < 4; i++) result._party[i] = (int)ff9play.CharacterIDToOldIndex(FF9StateSystem.Common.FF9.party.GetCharacterId(i));
            foreach (var pair in FF9StateSystem.EventState.gScriptVector) result._vectors[pair.Key] = new List<int>(pair.Value);
            foreach (var pair in FF9StateSystem.EventState.gScriptDictionary) result._dictionaries[pair.Key] = new Dictionary<int, int>(pair.Value);
            return result;
        }

        // Same base/replacement/append ordering as EventEngineUtils.SetupEventData,
        // without swapping its process-wide EntryData/EntryTable properties.
        internal static void ReadFiles(List<byte[]> files, out List<Entry> entries, out List<int[]> indexing)
        {
            entries = new List<Entry>(); indexing = new List<int[]>();
            if (files.Count == 0) throw new FormatException("No event script for this field");
            int baseCount = 0;
            for (int mod = 0; mod < files.Count; mod++)
            using (var r = new BinaryReader(new MemoryStream(files[mod])))
            {
                if (r.ReadByte() != 'E' || r.ReadByte() != 'V') throw new FormatException("Invalid event header");
                int version = r.ReadByte(), mode = version > 2 ? r.ReadByte() : 0, count = r.ReadByte();
                if (mode == 0) r.BaseStream.Seek(124, SeekOrigin.Current);
                long origin = r.BaseStream.Position;
                var table = new List<ObjTable>(); var indices = new int[count];
                for (int i = 0; i < count; i++)
                {
                    var row = new ObjTable(); row.ReadData(r, i, count, version == 2); table.Add(row);
                }
                if (version > 2) origin = r.BaseStream.Position;
                for (int i = 0; i < count; i++)
                {
                    ObjTable row = table[i]; int at = -1;
                    if (mod > 0 && row.memoria_id >= 0 && row.memoria_id < 1000)
                        for (int j = 0; j < baseCount; j++) if (entries[j].Id == row.memoria_id) { at = j; break; }
                    indices[i] = at;
                    if (mod > 0 && (row.append_mode & 1) == 0) continue;
                    if (origin + row.ofs + row.size > r.BaseStream.Length) throw new FormatException("Event entry exceeds file");
                    r.BaseStream.Position = origin + row.ofs;
                    var e = new Entry { Code = r.ReadBytes(row.size), Mod = mod, Id = row.memoria_id };
                    if (at < 0) { at = entries.Count; entries.Add(e); } else entries[at] = e;
                    indices[i] = at;
                }
                indexing.Add(indices);
                if (mod == 0) baseCount = entries.Count;
            }
        }
        internal static int Function(byte[] code, int tag)
        {
            if (code.Length < 2 || 2 + 4 * code[1] > code.Length) return -1;
            for (int i = 0; i < code[1]; i++)
            {
                int p = 2 + i * 4;
                if (BitConverter.ToUInt16(code, p) == tag) return 2 + BitConverter.ToUInt16(code, p + 2);
            }
            return -1;
        }
        private int Resolve(int id, int mod)
        {
            if (mod < 0 || mod >= _indexing.Count || id < 0 || id >= _indexing[mod].Length) return -1;
            return _indexing[mod][id];
        }
        private void Spawn(int id, int uid, int mod)
        {
            int at = Resolve(id, mod);
            if (at < 0 || at >= _entries.Count) return;
            if (_actors.Count >= 256) throw new FormatException("Preview object limit");
            if (uid == 0) uid = id;
            if (_actors.Any(a => a.Uid == uid && a.Entry.Mod == mod)) return;
            var actor = new Actor { Entry = _entries[at], Uid = uid };
            actor.Properties[5] = uid;
            int ip = Function(actor.Entry.Code, 0);
            if (ip >= 0) actor.Calls.Push(new Frame { IP = ip, Tag = 0, Level = 0 });
            _actors.Add(actor);
        }
        private static int Read(Actor a, Frame f, int bytes, bool signed = false)
        {
            if (f.IP < 0 || f.IP + bytes > a.Entry.Code.Length) throw new FormatException("Script operand outside entry");
            int v = 0;
            for (int i = 0; i < bytes; i++) v |= a.Entry.Code[f.IP++] << (8 * i);
            return signed ? (v << (32 - 8 * bytes)) >> (32 - 8 * bytes) : v;
        }
        internal void Tick()
        {
            Ticks++;
            // Round-robin object scheduling, bounded even for malformed loops.
            int count = _actors.Count, remaining = 1024;
            for (int n = 0; n < count; n++)
            {
                if (_actors.Count == 0) break;
                Actor a = _actors[_cursor % _actors.Count];
                _cursor = (_cursor + 1) % _actors.Count;
                if (a.Failed || a.Calls.Count == 0) continue;
                Frame f = a.Calls.Peek();
                if (f.Wait > 0) { f.Wait--; continue; }
                if (f.WaitingFor != null && f.WaitingFor.Calls.Count >= f.WaitingDepth) continue;
                f.WaitingFor = null;
                try
                {
                    for (int step = 0; step < 128 && a.Calls.Count > 0 && remaining > 0; step++)
                    { remaining--; if (!Step(a)) break; }
                }
                catch (Exception ex)
                {
                    a.Failed = true;
                    if (Diagnostics.Count < 16) Diagnostics.Add("Entry " + a.Entry.Id + " @" + f.IP + ": " + ex.Message);
                }
                if (remaining == 0) break;
            }
        }
        private bool Step(Actor a)
        {
            Frame f = a.Calls.Peek(); int start = f.IP;
            int op = Read(a, f, 1);
            while ((op & 255) == 255) op = op - 255 + 256 + Read(a, f, 1);
            if (op == 1 || op == 2 || op == 3)
            {
                int offset = Read(a, f, 2, op != 2);
                if (op == 1 || (op == 2 ? f.Last == 0 : f.Last != 0)) f.IP += offset;
                return true;
            }
            if (op == 4)
            {
                a.Calls.Pop();
                if (f.Tag == 0 && a.Calls.Count == 0)
                {
                    int main = Function(a.Entry.Code, 1);
                    if (main >= 0) a.Calls.Push(new Frame { IP = main, Tag = 1, Level = 7 });
                }
                return false;
            }
            if (op == 5) { f.Last = Expr(a, f); return true; }
            if (op == 0x29)
            {
                int flag = Read(a, f, 1), count = (flag & 1) != 0 ? Expr(a, f) : Read(a, f, 1);
                flag >>= 1;
                if (count < 0 || count > 255) throw new FormatException("Region vertex limit");
                for (int i = 0; i < count * 2; i++) { if ((flag & 1) != 0) Expr(a, f); else Read(a, f, 2); flag >>= 1; }
                return true;
            }
            if (op == 6)
            {
                int count = Read(a, f, 1), def = Read(a, f, 2), origin = f.IP, jump = def;
                for (int i = 0; i < count; i++) { int key = Read(a, f, 2), value = Read(a, f, 2); if (key == (f.Last & 65535)) jump = value; }
                f.IP = origin + jump; return true;
            }
            if (op == 11 || op == 13)
            {
                int count = Read(a, f, op == 13 ? 2 : 1), origin = f.IP - 1;
                int first = Read(a, f, 2), def = Read(a, f, 2), jump = def;
                for (int i = 0; i < count; i++) { int value = Read(a, f, 2); if (f.Last - first == i) jump = value; }
                f.IP = origin + jump; return true;
            }
            Layout layout;
            if (!Layouts.TryGetValue(op, out layout)) throw new NotSupportedException("Opcode 0x" + op.ToString("X"));
            int flags = layout.Flag ? Read(a, f, 1) : 0;
            int[] args = new int[layout.Width.Length];
            for (int i = 0; i < args.Length; i++)
            {
                int width = layout.Width[i];
                if (width == 0) throw new NotSupportedException("Variable operand layout");
                args[i] = (flags & (1 << i)) != 0 ? Expr(a, f) : Read(a, f, width, layout.Flag && width > 1);
            }
            if (op >= 7 && op <= 9) Spawn(args[0], args[1], a.Entry.Mod);
            else if (op == 0x2A || op == 0x2B || op == 0x8C)
            {
                // A scene transition ends this execution path. Never follow it
                // into another map or continue past it into unreachable bytes.
                a.Calls.Clear(); return false;
            }
            else if (op == 0x22) { f.Wait = Math.Max(0, args[0] - 1); return false; }
            else if (op == 0x10 || op == 0x12 || op == 0x14 || op == 0x16 || op == 0x18 || op == 0x1A)
            {
                bool caller = op >= 0x16;
                Actor target = caller ? f.Caller : args[1] == 255 ? a : _actors.FirstOrDefault(x => x.Uid == args[1]);
                int tag = args[caller ? 1 : 2], level = args[0];
                if (target == null || target.Failed) return true;
                if (target.Calls.Count > 0 && target.Calls.Peek().Level <= level)
                { if (op != 0x10 && op != 0x16) { f.IP = start; return false; } return true; }
                int ip = Function(target.Entry.Code, tag);
                if (ip < 0) return true;
                if (target.Calls.Count >= 8) throw new FormatException("Preview call depth limit");
                target.Calls.Push(new Frame { IP = ip, Tag = tag, Level = level, Caller = a });
                if (op == 0x14 || op == 0x1A) { f.WaitingFor = target; f.WaitingDepth = target.Calls.Count; return false; }
            }
            else if (op == 0x1C)
            {
                Actor target = args[0] == 255 ? a : _actors.FirstOrDefault(x => x.Uid == args[0]);
                if (target != null) target.Calls.Clear();
                return target != a;
            }
            else
            {
                if (op == 0x7E) _camera = args[0];
                if (op == 0x2D) _control = 0;
                if (op == 0x2E) _control = 1;
                Actor positioned = a; int offset = 0;
                if (op == 0xAD || op == 0xBF) { positioned = _actors.FirstOrDefault(x => x.Uid == args[0]); offset = 1; }
                if (positioned != null)
                {
                    if (op == 0xA1 || op == 0xAD) for (int i = 0; i < 3; i++) positioned.Properties[i] = args[offset + i];
                    if (op == 0x1D || op == 0xBF) { positioned.Properties[0] = args[offset]; positioned.Properties[2] = args[offset + 1]; }
                    if (op == 0x36) positioned.Properties[3] = args[0];
                    if (op == 0x93) positioned.Properties[4] = args[0];
                }
                _visual(op, args, a.Uid);
            }
            return true;
        }

        private int Expr(Actor a, Frame f)
        {
            var stack = new Stack<Value>();
            Func<int> pop = () => stack.Pop().Read();
            Action<int> push = v => stack.Push(new Value { Number = (v << 6) >> 6 });
            for (int budget = 0; budget < 256; budget++)
            {
                int op = Read(a, f, 1);
                if (op >= 128)
                {
                    if (op == 0xD3)
                    {
                        int command = Read(a, f, 2), count = Read(a, f, 1);
                        int[] arguments = new int[count];
                        for (int i = count - 1; i >= 0; i--) arguments[i] = pop();
                        if (command == 10 && count == 1) push(arguments[0] >= 0 && arguments[0] < 4 ? _party[arguments[0]] : -1);
                        else if ((command == (int)EBin.flexible_varfunc.VECTOR && count == 2) || (command == (int)EBin.flexible_varfunc.VECTOR_SIZE && count == 1))
                        {
                            List<int> vector;
                            if (!_vectors.TryGetValue(arguments[0], out vector))
                            {
                                if (_vectors.Count >= 256) throw new FormatException("Preview vector limit");
                                vector = new List<int>(); _vectors[arguments[0]] = vector;
                            }
                            var data = vector; int index = count == 2 ? arguments[1] : -1;
                            stack.Push(new Value {
                                Getter = () => command == (int)EBin.flexible_varfunc.VECTOR_SIZE ? data.Count : index >= 0 && index < data.Count ? data[index] : 0,
                                Setter = value => {
                                    if (command == (int)EBin.flexible_varfunc.VECTOR_SIZE) {
                                        if (value < 0) return;
                                        if (value > 4096) throw new FormatException("Preview vector size limit");
                                        while (data.Count < value) data.Add(0);
                                        if (data.Count > value) data.RemoveRange(value, data.Count - value);
                                    } else if (index >= 0 && index < data.Count) data[index] = value;
                                    else if (index == data.Count) { if (data.Count >= 4096) throw new FormatException("Preview vector size limit"); data.Add(value); }
                                }
                            });
                        }
                        else if (command == (int)EBin.flexible_varfunc.DICTIONARY && count == 2)
                        {
                            Dictionary<int,int> data;
                            if (!_dictionaries.TryGetValue(arguments[0], out data))
                            {
                                if (_dictionaries.Count >= 256) throw new FormatException("Preview dictionary limit");
                                data = new Dictionary<int,int>(); _dictionaries[arguments[0]] = data;
                            }
                            int key = arguments[1];
                            stack.Push(new Value { Getter = () => { int v; return data.TryGetValue(key, out v) ? v : 0; },
                                Setter = v => { if (data.Count >= 4096 && !data.ContainsKey(key)) throw new FormatException("Preview dictionary size limit"); data[key] = v; } });
                        }
                        else throw new NotSupportedException("Extended expression " + command + " with " + count + " operands");
                        continue;
                    }
                    if ((op & 3) == 3) throw new NotSupportedException("Extended variable 0x" + op.ToString("X"));
                    int variableOffset = Read(a, f, (op & 32) != 0 ? 2 : 1);
                    stack.Push(new Value { Memory = (op & 3) == 0 ? _globals : (op & 3) == 1 ? _map : a.Locals, Offset = variableOffset, Type = (op >> 2) & 7 });
                }
                else if (op == 127) return stack.Count == 0 ? 0 : pop();
                else if (op == 125 || op == 126) push(Read(a, f, op == 125 ? 2 : 4, true));
                else if (op == 122)
                {
                    int key = Read(a, f, 1);
                    if (key == 0) push(_random.Next(256));
                    else if (key == 1) push(_camera);
                    else if (key == 2) push(_control);
                    else if (key == 3 || key == 15 || key == 26) push(0);
                    else if (key == 191) push(_field);
                    else if (key == 12) push(ScreenX);
                    else if (key == 13) push(ScreenY);
                    else throw new NotSupportedException("System variable " + key);
                }
                else if (op == 95)
                {
                    int id = Read(a, f, 1);
                    Actor target = id == 255 ? a : _actors.FirstOrDefault(x => x.Uid == id);
                    push(target == null ? 0 : target.Uid);
                }
                else if (op == 120)
                {
                    int id = Read(a, f, 1), property = Read(a, f, 1);
                    Actor target = id == 255 ? a : _actors.FirstOrDefault(x => x.Uid == id);
                    if (property > 7) throw new NotSupportedException("Battle object property " + property);
                    push(target == null ? 0 : property == 6 ? (target.Calls.Count == 0 ? 7 : target.Calls.Peek().Level) : target.Properties[property]);
                }
                else if (op == 98 || op == 99 || op == 80 || op == 81)
                {
                    int angle = pop();
                    double radians = angle * (op >= 98 ? Math.PI / 128 : Math.PI / 2048);
                    push((int)(4096 * (op == 98 || op == 80 ? Math.Sin(radians) : Math.Cos(radians))));
                }
                else if (op == 102)
                {
                    int z = pop(), x = pop();
                    push(x == 0 && z == 0 ? 0 : (int)Math.Floor(Math.Atan2(-x, -z) * 128 / Math.PI));
                }
                else if (op >= 4 && op <= 7)
                {
                    Value v = stack.Pop(); int old = v.Read(), next = old + (op % 2 == 0 ? 1 : -1);
                    v.Write(next); push(op < 6 ? old : next);
                }
                else if (op == 12 || op == 13 || op == 14 || op == 16)
                { int v = pop(); push(op == 12 ? v : op == 13 ? -v : op == 14 ? (v == 0 ? 1 : 0) : ~v); }
                else if (op == 44 || (op >= 47 && op <= 53) || (op >= 61 && op <= 63))
                {
                    int rhs = pop(); Value lhs = stack.Pop(); int v = op == 44 ? rhs : Binary(op <= 53 ? op - 30 : op - 25, lhs.Read(), rhs);
                    lhs.Write(v); push(v);
                }
                else if ((op >= 17 && op <= 27) || op == 32 || op == 33 || (op >= 36 && op <= 40))
                { int rhs = pop(), lhs = pop(); push(Binary(op, lhs, rhs)); }
                else if (op >= 70 && op <= 73)
                { int v = pop(); push(op == 70 ? (sbyte)v : op == 71 ? (byte)v : op == 72 ? (short)v : (ushort)v); }
                else throw new NotSupportedException("Expression 0x" + op.ToString("X"));
            }
            throw new FormatException("Preview expression budget exceeded");
        }
        private static int Binary(int op, int a, int b)
        {
            switch (op)
            {
                case 17: return a * b; case 18: return b == 0 ? 0 : a / b; case 19: return b == 0 ? 0 : a % b;
                case 20: return a + b; case 21: return a - b; case 22: return a << b; case 23: return a >> b;
                case 24: return a < b ? 1 : 0; case 25: return a > b ? 1 : 0; case 26: return a <= b ? 1 : 0; case 27: return a >= b ? 1 : 0;
                case 32: return a == b ? 1 : 0; case 33: return a != b ? 1 : 0;
                case 36: return a & b; case 37: return a ^ b; case 38: return a | b;
                case 39: return a != 0 && b != 0 ? 1 : 0; case 40: return a != 0 || b != 0 ? 1 : 0;
                default: throw new NotSupportedException("Binary expression " + op);
            }
        }
    }
}
