using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using WarCore.Sim;

namespace WarCore.Tests
{
    // IL catches allocations hidden behind aliases, target-typed new, closures and implicit boxing.
    internal static class HotPathAllocationRules
    {
        internal sealed class Instruction
        {
            internal int Offset;
            internal OpCode Code;
            internal MemberInfo Member;
            internal int[] Targets = Array.Empty<int>();
        }

        internal static List<Instruction> Read(MethodBase method)
        {
            byte[] bytes = method.GetMethodBody()?.GetILAsByteArray();
            var result = new List<Instruction>();
            if (bytes == null)
                return result;
            var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)).ToDictionary(code => unchecked((ushort)code.Value));
            for (int offset = 0; offset < bytes.Length;)
            {
                var item = new Instruction { Offset = offset };
                ushort value = bytes[offset++];
                if (value == 0xfe)
                    value = (ushort)(0xfe00 | bytes[offset++]);
                item.Code = opcodes[value];
                OperandType operand = item.Code.OperandType;
                int size = operand == OperandType.InlineNone ? 0 : operand == OperandType.ShortInlineI || operand == OperandType.ShortInlineVar || operand == OperandType.ShortInlineBrTarget ? 1 :
                    operand == OperandType.InlineVar ? 2 : operand == OperandType.InlineI8 || operand == OperandType.InlineR ? 8 : 4;
                if (operand == OperandType.InlineSwitch)
                {
                    int count = BitConverter.ToInt32(bytes, offset);
                    size = 4 + count * 4;
                    item.Targets = Enumerable.Range(0, count).Select(i => offset + size + BitConverter.ToInt32(bytes, offset + 4 + i * 4)).ToArray();
                }
                else if (operand == OperandType.InlineBrTarget || operand == OperandType.ShortInlineBrTarget)
                    item.Targets = new[] { offset + size + (size == 1 ? unchecked((sbyte)bytes[offset]) : BitConverter.ToInt32(bytes, offset)) };
                if (operand == OperandType.InlineMethod || operand == OperandType.InlineType)
                {
                    Type[] types = method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
                    Type[] arguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
                    item.Member = method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset), types, arguments);
                }
                offset += size;
                result.Add(item);
            }
            return result;
        }

        internal static IEnumerable<string> Violations(MethodBase method)
        {
            List<Instruction> code = Read(method);
            var starts = new HashSet<int>(code.SelectMany(item => item.Targets));
            for (int i = 0; i < code.Count; i++)
            {
                Instruction item = code[i];
                // Explicit invalid-input throws are cold. Never excuse a branch that can return normally.
                int end = i;
                while (end + 1 < code.Count && !starts.Contains(code[end + 1].Offset)
                    && code[end].Code.FlowControl != FlowControl.Branch && code[end].Code.FlowControl != FlowControl.Cond_Branch
                    && code[end].Code.FlowControl != FlowControl.Throw && code[end].Code.FlowControl != FlowControl.Return)
                    end++;
                if (code[end].Code == OpCodes.Throw)
                    continue;
                string reason = null;
                if (item.Code == OpCodes.Newarr)
                    reason = "array allocation";
                if (item.Code == OpCodes.Box)
                    reason = "boxing";
                if (item.Code == OpCodes.Newobj && !item.Member.DeclaringType.IsValueType)
                    reason = "reference allocation";
                if (item.Code == OpCodes.Ldftn || item.Code == OpCodes.Ldvirtftn)
                    reason = "delegate/closure construction";
                if (item.Member is MethodBase called && (item.Code == OpCodes.Call || item.Code == OpCodes.Callvirt))
                {
                    if (called.DeclaringType.Namespace == "System.Linq")
                        reason = "LINQ";
                    if (called.GetParameters().Any(parameter => parameter.IsDefined(typeof(ParamArrayAttribute), false)))
                        reason = "params call";
                    if (called.DeclaringType == typeof(string) && new[] { "Concat", "Format", "Join", "Substring", "ToCharArray" }.Contains(called.Name))
                        reason = "string allocation";
                    if (called.DeclaringType == typeof(System.Text.Encoding) && called is MethodInfo info && info.ReturnType == typeof(byte[]))
                        reason = "encoding array allocation";
                    if (called.DeclaringType == typeof(Array) && called.Name == "Resize"
                        && !(method.DeclaringType == typeof(SimEventBuffer) && (method.Name == "Publish" || method.Name == "CompleteWarmup")))
                        reason = "array resize";
                }
                if (reason != null)
                    yield return method.DeclaringType.FullName + "." + method.Name + " IL_" + item.Offset.ToString("X4") + ": " + reason;
            }
        }

        internal static IEnumerable<MethodBase> HotMethods()
        {
            Assembly assembly = typeof(SimClock).Assembly;
            var pending = new Queue<MethodBase>();
            var seen = new HashSet<MethodBase>();
            foreach (Type type in assembly.GetTypes())
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (method.Name == "Step" || method.Name == "StepEntity" || method.Name == "StateHash" || method.Name == "WriteState"
                        || method.Name == "Schedule" || method.Name == "ScheduleWithHandle" || method.Name == "Cancel" || method.Name == "TryDequeue"
                        || method.Name == "Create" || method.Name == "Destroy" || method.Name == "Publish"
                        || method.Name == "With" || type.DeclaringType == typeof(EntityStore)
                        || (method.IsStatic && (type == typeof(SimFix) || type == typeof(SimVec3) || type == typeof(SimWide)))
                        || (type == typeof(SimVec3) && (method.Name == "get_Length" || method.Name == "get_Normalized"))
                        || type == typeof(SpatialBodyColumns) || type == typeof(SpatialPartitionState) || type == typeof(SpatialGridState)
                        || type == typeof(GridSpatialIndex) || type == typeof(SpatialQueryView) || type == typeof(GridSpatialQuery) || type == typeof(SpatialQueryBase) || type == typeof(GridQueryContext))
                        pending.Enqueue(method);
            while (pending.Count != 0)
            {
                MethodBase method = pending.Dequeue();
                if (!seen.Add(method) || (method.IsConstructor && !method.DeclaringType.IsValueType) || Cold(method))
                    continue;
                yield return method;
                foreach (Instruction item in Read(method))
                    if (item.Member is MethodBase next && next.DeclaringType.Assembly == assembly)
                        pending.Enqueue(next);
            }
        }

        private static bool Cold(MethodBase method)
        {
            string key = method.DeclaringType.Name + "." + method.Name;
            // These named entries are assembly/setup, explicit rebuild or guarded pre-warm capacity paths.
            // Error detail creation is reached only after the scheduler rejects an exhausted warmed queue.
            return key == "GridSpatialQuery.Rebuild" || key == "GridSpatialIndex.Rebuild" || key == "SpatialQueryView.Bind" || key == "EntityStore.Reserve" || key == "SimScheduler.Reserve"
                || key == "SimScheduler.CapacityError" || key == "TieredTimeline.PrepareWarmup" || key == "StructureSystem.PrepareWarmup" || key == "Components`1.Resize" || key == "TriggerRegistry.Freeze";
        }
    }
}
