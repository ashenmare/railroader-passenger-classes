using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace PassengerClasses
{
    // A fingerprint of what a game method does, for a hook that stands in for it: the method's own instructions read by name (each opcode, the text
    // of each string, the owner and name of each method, field or type it names, each number), never by metadata token, because the tokens move
    // whenever the game's dll is rebuilt and the instructions may not have. BCL only. A different fingerprint means the game's method changed and
    // the hook stays off.
    internal static class Fingerprint
    {
        private static Dictionary<short, OpCode> _ops;

        private static Dictionary<short, OpCode> Ops()
        {
            if (_ops != null) return _ops;
            var d = new Dictionary<short, OpCode>();
            foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(OpCode)) { OpCode oc = (OpCode)f.GetValue(null); d[oc.Value] = oc; }
            _ops = d;
            return d;
        }

        // The methods are taken in the order given; the caller sorts them by name so the order never depends on the runtime.
        internal static string Of(IList<MethodBase> methods)
        {
            var sb = new StringBuilder();
            foreach (MethodBase m in methods)
            {
                sb.Append('#').Append(m.DeclaringType == null ? "" : m.DeclaringType.Name).Append("::").Append(m.Name).Append('\n');
                MethodBody body = m.GetMethodBody();
                if (body == null) { sb.Append("nobody\n"); continue; }
                Describe(m.Module, m, body.GetILAsByteArray(), sb);
            }
            using (SHA256 h = SHA256.Create())
            {
                byte[] hash = h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder();
                for (int i = 0; i < 8; i++) hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
            }
        }

        // The text the hash is taken over (the self-check reads it too).
        internal static void Describe(Module module, MethodBase m, byte[] il, StringBuilder sb)
        {
            Dictionary<short, OpCode> ops = Ops();
            Type[] tArgs = m.DeclaringType != null && m.DeclaringType.IsGenericType ? m.DeclaringType.GetGenericArguments() : null;
            Type[] mArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;
            int i = 0;
            while (i < il.Length)
            {
                short v = il[i++];
                if (v == 0xFE) v = unchecked((short)(0xFE00 | il[i++]));
                OpCode oc;
                if (!ops.TryGetValue(v, out oc)) { sb.Append("?op\n"); return; }
                sb.Append(oc.Name);
                switch (oc.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget: i += 1; break;
                    case OperandType.ShortInlineVar: i += 1; break;
                    case OperandType.ShortInlineI: sb.Append(' ').Append(oc == OpCodes.Ldc_I4_S ? ((sbyte)il[i]).ToString() : il[i].ToString()); i += 1; break;
                    case OperandType.InlineVar: i += 2; break;
                    case OperandType.InlineBrTarget: i += 4; break;
                    case OperandType.InlineI: sb.Append(' ').Append(BitConverter.ToInt32(il, i)); i += 4; break;
                    case OperandType.InlineI8: sb.Append(' ').Append(BitConverter.ToInt64(il, i)); i += 8; break;
                    case OperandType.ShortInlineR: sb.Append(' ').Append(BitConverter.ToSingle(il, i).ToString("R", System.Globalization.CultureInfo.InvariantCulture)); i += 4; break;
                    case OperandType.InlineR: sb.Append(' ').Append(BitConverter.ToDouble(il, i).ToString("R", System.Globalization.CultureInfo.InvariantCulture)); i += 8; break;
                    case OperandType.InlineSwitch: { int n = BitConverter.ToInt32(il, i); i += 4 + 4 * n; sb.Append(' ').Append(n); break; }
                    case OperandType.InlineString:
                        { int tok = BitConverter.ToInt32(il, i); i += 4; string s; try { s = module.ResolveString(tok); } catch (Exception) { s = "?"; } sb.Append(" \"").Append(s).Append('"'); break; }
                    case OperandType.InlineSig: i += 4; break;
                    default:   // InlineMethod, InlineField, InlineType, InlineTok: the member's owner and name
                        {
                            int tok = BitConverter.ToInt32(il, i); i += 4;
                            string name;
                            try { MemberInfo mi = module.ResolveMember(tok, tArgs, mArgs); name = (mi.DeclaringType == null ? "" : mi.DeclaringType.Name + "::") + mi.Name; }
                            catch (Exception) { name = "?"; }
                            sb.Append(' ').Append(name);
                            break;
                        }
                }
                sb.Append('\n');
            }
        }
    }
}
