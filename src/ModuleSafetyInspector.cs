using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace OreMarket
{
    // Best-effort behavior inspection, not a security boundary or mod certification.
    // Classify potential direct writes separately from unsupported resource flow operations.
    // Physics/save/recovery checks remain essential. Hidden equal-balance substitutions cannot be proven.
    public static class ModuleSafetyInspector
    {
        private sealed class Assessment
        {
            internal string Observable = "";
            internal string Unsafe = "";
        }
        private static readonly Dictionary<Type, Assessment> Cache = new Dictionary<Type, Assessment>();
        private static readonly Dictionary<short, OpCode> Codes = BuildCodes();
        private static Dictionary<short, OpCode> BuildCodes()
        {
            var result = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.GetValue(null) is OpCode code) result[code.Value] = code;
            return result;
        }
        // Legacy strict inspection remains available for audits and regression tests.
        public static bool Allows(Type type, out string reason)
        {
            Assessment result = Assess(type);
            reason = result.Unsafe.Length > 0 ? result.Unsafe : result.Observable;
            return reason.Length == 0;
        }

        // Direct amount/capacity fields are observed by OreMarketTank. Their presence
        // does not prove a write occurred. Resource flow/list operations remain unsupported.
        public static bool CanTrack(Type type, out string reason)
        {
            Assessment result = Assess(type);
            reason = result.Unsafe;
            return reason.Length == 0;
        }

        public static string ObservationReason(Type type) => Assess(type).Observable;

        private static Assessment Assess(Type type)
        {
            if (Cache.TryGetValue(type, out Assessment result)) return result;
            result = new Assessment();
            var seen = new HashSet<MethodBase>();
            for (Type current = type; current != null && !Boundary(current); current = current.BaseType)
            {
                foreach (MethodBase method in current.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    InspectSafely(method, seen, result);
                foreach (ConstructorInfo method in current.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    InspectSafely(method, seen, result);
            }
            Cache[type] = result;
            return result;
        }

        private static void InspectSafely(MethodBase method, HashSet<MethodBase> seen, Assessment result)
        {
            try { Inspect(method, seen, result); }
            catch (Exception) { } // Unknown code alone is not evidence of an operation (existing policy).
        }

        private static bool Boundary(Type type)
        {
            string assembly = type.Assembly.GetName().Name;
            return assembly == "Assembly-CSharp" || assembly == "mscorlib" || assembly == "netstandard" ||
                assembly == "System" || assembly.StartsWith("System.", StringComparison.Ordinal) ||
                assembly.StartsWith("UnityEngine", StringComparison.Ordinal) || assembly == "Unity.TextMeshPro";
        }
        private static void Inspect(MethodBase method, HashSet<MethodBase> seen, Assessment result)
        {
            if (!seen.Add(method)) return;
            // Delegate targets referenced by ldftn/ldvirtftn are inspected below.
            if (typeof(Delegate).IsAssignableFrom(method.DeclaringType) &&
                (method.IsConstructor || method.Name == "Invoke")) return;
            if (seen.Count > 2048) return; // Stop expanding unrelated helper graphs.
            if (method.DeclaringType.TypeInitializer != null) InspectSafely(method.DeclaringType.TypeInitializer, seen, result);
            if (method.IsAbstract) return; // Concrete implementations are inspected on the module type.
            MethodBody body = method.GetMethodBody();
            if (body == null) return; // Native/abstract code cannot be inspected here.
            byte[] bytes = body.GetILAsByteArray();
            for (int index = 0; index < bytes.Length;)
            {
                short value = bytes[index++];
                if (value == 0xfe) value = unchecked((short)(0xfe00 | bytes[index++]));
                OpCode code = Codes[value];
                int size;
                switch (code.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(bytes, index); break;
                    default: size = 4; break;
                }
                
                if (code.OperandType == OperandType.InlineField || code.OperandType == OperandType.InlineMethod)
                {
                    MemberInfo member = method.Module.ResolveMember(BitConverter.ToInt32(bytes, index),
                        method.DeclaringType.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
                    string owner = member.DeclaringType?.FullName ?? "";
                    if (member is FieldInfo field && (owner == "PartResource" || owner == "ProtoPartResourceSnapshot") &&
                        (field.Name == "amount" || field.Name == "maxAmount") &&
                        (code == OpCodes.Stfld || code == OpCodes.Ldflda || code == OpCodes.Ldsflda))
                    { result.Observable = "Observed resource quantity/capacity fields required: " + member; }
                    if (member is MethodBase called)
                    {
                        string name = called.Name;
                        if ((owner == "Part" || owner == "Vessel" || owner.Contains("ResourceBroker") || owner == "PartResourceList") &&
                            (name.Contains("RequestResource") || name.Contains("TransferResource") || name.Contains("StoreResource") ||
                             name.Contains("SetResource") || name.Contains("AddResource") || name == "Clear" || name == "Remove" || name == "Add"))
                        {
                            if (!LiteralResourceArgument.IsNonOreRequest(method, bytes, index, called))
                                result.Unsafe = "Unsupported Ore/unknown resource operation: " + called;
                        }
                        if ((!Boundary(called.DeclaringType) && called.IsAbstract) ||
                            owner == "System.Activator" || owner.Contains("Runtime.InteropServices.Marshal") ||
                            owner.Contains("Unsafe") || name == "AddComponent" || name == "StartCoroutine" && called.GetParameters().Length > 0 && called.GetParameters()[0].ParameterType == typeof(string))
                        { index += size; continue; } // No resource write established by this dispatch alone.
                        if (!Boundary(called.DeclaringType)) InspectSafely(called, seen, result);
                    }
                }
                index += size;
            }
            return;
        }
    }
}


