using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace OreMarket
{
    // Conservative, basic-block-only constant-string argument analysis. No names
    // of mods or allowed resources are listed. Joins, locals, dynamic strings and
    // unhandled IL produce UNKNOWN; they never authorize an Ore-capable call.
    internal static class LiteralResourceArgument
    {
        internal static bool IsNonOreRequest(MethodBase method, byte[] il, int operandOffset, MethodBase called)
        {
            if (called.DeclaringType.FullName != "Part" || called.Name != "RequestResource" ||
                called.GetParameters().Length < 2 || called.GetParameters()[0].ParameterType != typeof(string)) return false;
            try
            {
                List<Instruction> instructions = Decode(il);
                var targets = new HashSet<int>();
                foreach (Instruction instruction in instructions)
                {
                    if (instruction.Code.OperandType == OperandType.ShortInlineBrTarget)
                        targets.Add(instruction.End + unchecked((sbyte)il[instruction.Operand]));
                    else if (instruction.Code.OperandType == OperandType.InlineBrTarget)
                        targets.Add(instruction.End + BitConverter.ToInt32(il, instruction.Operand));
                    else if (instruction.Code.OperandType == OperandType.InlineSwitch)
                        for (int k = 0; k < BitConverter.ToInt32(il, instruction.Operand); k++)
                            targets.Add(instruction.End + BitConverter.ToInt32(il, instruction.Operand + 4 + 4 * k));
                }
                var stack = new List<string>();
                foreach (Instruction instruction in instructions)
                {
                    if (targets.Contains(instruction.Start)) stack.Clear();
                    if (instruction.Operand == operandOffset)
                    {
                        int index = stack.Count - called.GetParameters().Length;
                        return index >= 0 && stack[index] != null && stack[index].Length > 0 && stack[index] != "Ore";
                    }
                    OpCode code = instruction.Code;
                    if (code == OpCodes.Ldstr)
                        stack.Add(method.Module.ResolveString(BitConverter.ToInt32(il, instruction.Operand)));
                    else if (code == OpCodes.Dup)
                        stack.Add(stack.Count == 0 ? null : stack[stack.Count - 1]);
                    else if (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj)
                    {
                        MethodBase target = method.Module.ResolveMethod(BitConverter.ToInt32(il, instruction.Operand),
                            method.DeclaringType.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
                        Pop(stack, target.GetParameters().Length + (!target.IsStatic && code != OpCodes.Newobj ? 1 : 0));
                        if (code == OpCodes.Newobj || target is MethodInfo info && info.ReturnType != typeof(void)) stack.Add(null);
                    }
                    else
                    {
                        int pop = PopCount(code.StackBehaviourPop);
                        if (pop < 0) stack.Clear(); else Pop(stack, pop);
                        int push = code.StackBehaviourPush == StackBehaviour.Push0 ? 0 :
                            code.StackBehaviourPush == StackBehaviour.Push1_push1 ? 2 : 1;
                        for (int k = 0; k < push; k++) stack.Add(null);
                    }
                    if (code.FlowControl == FlowControl.Branch || code.FlowControl == FlowControl.Cond_Branch ||
                        code.FlowControl == FlowControl.Return || code.FlowControl == FlowControl.Throw) stack.Clear();
                }
            }
            catch (Exception) { } // No exemption when analysis is inconclusive.
            return false;
        }

        private static void Pop(List<string> stack, int count)
        {
            if (count >= stack.Count) stack.Clear(); else stack.RemoveRange(stack.Count - count, count);
        }
        private static int PopCount(StackBehaviour behavior)
        {
            switch (behavior)
            {
                case StackBehaviour.Pop0: return 0;
                case StackBehaviour.Pop1:
                case StackBehaviour.Popi:
                case StackBehaviour.Popref: return 1;
                case StackBehaviour.Pop1_pop1:
                case StackBehaviour.Popi_pop1:
                case StackBehaviour.Popi_popi:
                case StackBehaviour.Popi_popi8:
                case StackBehaviour.Popi_popr4:
                case StackBehaviour.Popi_popr8:
                case StackBehaviour.Popref_pop1:
                case StackBehaviour.Popref_popi: return 2;
                case StackBehaviour.Popi_popi_popi:
                case StackBehaviour.Popref_popi_pop1:
                case StackBehaviour.Popref_popi_popi:
                case StackBehaviour.Popref_popi_popi8:
                case StackBehaviour.Popref_popi_popr4:
                case StackBehaviour.Popref_popi_popr8:
                case StackBehaviour.Popref_popi_popref: return 3;
                default: return -1;
            }
        }
        private sealed class Instruction
        {
            internal int Start, Operand, End;
            internal OpCode Code;
        }
        private static readonly Dictionary<short, OpCode> Codes = BuildCodes();
        private static Dictionary<short, OpCode> BuildCodes()
        {
            var result = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.GetValue(null) is OpCode code) result[code.Value] = code;
            return result;
        }
        private static List<Instruction> Decode(byte[] il)
        {
            var result = new List<Instruction>();
            for (int index = 0; index < il.Length;)
            {
                var instruction = new Instruction { Start = index };
                short value = il[index++];
                if (value == 0xfe) value = unchecked((short)(0xfe00 | il[index++]));
                instruction.Code = Codes[value];
                instruction.Operand = index;
                int size;
                switch (instruction.Code.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(il, index); break;
                    default: size = 4; break;
                }
                index += size;
                instruction.End = index;
                result.Add(instruction);
            }
            return result;
        }
    }
}
