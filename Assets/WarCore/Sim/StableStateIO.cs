using System;
using System.IO;
using System.Text;

namespace WarCore.Sim
{
    // Opting in declares a field schema without physical entity or task identities.
    public interface IStableReferenceValidator<T> where T : unmanaged, IStateHash
    {
        void ValidateReferences(EntityStore store, in T value);
    }

    public interface IStableStateSerializer : ISimStateSerializer { }
    public interface IStableComponentSerializer<T> : IComponentSerializer<T> where T : unmanaged, IStateHash { }

    public sealed partial class SimStateWriter
    {
        public bool IsStable { get; }
        internal EntityStore Entities { get; }
        private readonly StateHash stableHash;
        private readonly bool measuring;
        private readonly SimStateWriter measure;
        internal long Length { get; private set; }
        internal SimStateWriter(EntityStore entities, bool materialize = false, StateHash hash = null, bool measuring = false)
        {
            IsStable = true;
            Entities = entities;
            stableHash = hash;
            this.measuring = measuring;
            if (materialize)
                stream = new MemoryStream();
            if (!measuring)
                measure = new SimStateWriter(entities, measuring:true);
        }
        internal void ResetStable()
        {
            Length = 0;
            stableHash?.Reset();
        }
        private void StableByte(byte value)
        {
            if (Length == int.MaxValue)
                throw new SimBoundaryException("Stable record exceeds its byte capacity.");
            Length++;
            stableHash?.Add(value);
            stream?.WriteByte(value);
        }
        private long StableTextByteCount(string value)
        {
            long byteCount = 0;
            // The wide count cannot overflow for an int32-sized string; encoding never decides the boundary.
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (current <= 0x7F)
                    byteCount++;
                else if (current <= 0x7FF)
                    byteCount += 2;
                else if (char.IsHighSurrogate(current))
                {
                    if (i+1 >= value.Length || !char.IsLowSurrogate(value[i+1]))
                        throw new SimBoundaryException("Stable text must be valid Unicode.");
                    i++;
                    byteCount += 4;
                }
                else if (char.IsLowSurrogate(current))
                    throw new SimBoundaryException("Stable text must be valid Unicode.");
                else
                    byteCount += 3;
            }
            if (byteCount > int.MaxValue-8L-Length)
                throw new SimBoundaryException("Stable text exceeds the remaining byte capacity.");
            return byteCount;
        }
        private void StableString(string value)
        {
            UInt64((ulong)StableTextByteCount(value));
            Span<byte> buffer = stackalloc byte[192];
            int offset = 0;
            while (offset < value.Length)
            {
                int count = value.Length - offset;
                if (count > 64)
                    count = 64;
                if (offset + count < value.Length && char.IsHighSurrogate(value[offset + count - 1]))
                    count--;
                int size = Encoding.UTF8.GetBytes(value.AsSpan(offset,count),buffer);
                for (int i = 0; i < size; i++)
                    Byte(buffer[i]);
                offset += count;
            }
        }
        public void Link(EntityLink link)
        {
            Entities?.ValidateLink(link,false);
            Bool(link.HasValue);
            if (link.HasValue)
                UInt64(link.Id.Value);
        }
        public void TaskKeyValue(TaskKey key)
        {
            Int64(key.DueTick);
            UInt64(key.OriginDomain);
            UInt64(key.Origin.Value);
            UInt64(key.Counter);
            UInt64(key.Kind);
        }
        public void StableEvent(StableSimEvent item)
        {
            Int32(item.Type.Value);
            Int64(item.Tick);
            Int64(item.Sequence);
            Link(item.Source);
            Link(item.Target);
            SnapshotValues.Vector(this,item.Payload.Position);
            SnapshotValues.Vector(this,item.Payload.Aux);
            Int64(item.Payload.Magnitude.Raw);
            Int64(item.Payload.Radius.Raw);
            Int32(item.Tag);
        }
        internal void Section(ISimStateSerializer serializer)
        {
            if (!(serializer is IStableStateSerializer))
                throw new SimBoundaryException("An explicit stable extension schema is required.");
            if (measuring)
            {
                UInt64(0);
                serializer.SaveState(this);
                return;
            }
            measure.ResetStable();
            serializer.SaveState(measure);
            UInt64((ulong)measure.Length);
            serializer.SaveState(this);
        }
        internal void Component<T>(IComponentSerializer<T> serializer, in T value) where T : unmanaged, IStateHash
        {
            if (!(serializer is IStableComponentSerializer<T>))
                throw new SimBoundaryException("An explicit stable component schema is required.");
            if (measuring)
            {
                UInt64(0);
                serializer.Write(this,in value);
                return;
            }
            measure.ResetStable();
            serializer.Write(measure,in value);
            UInt64((ulong)measure.Length);
            serializer.Write(this,in value);
        }
    }

    public sealed partial class SimStateReader
    {
        public bool IsStable { get; }
        internal EntityStore Entities { get; }
        internal SimStateReader(byte[] bytes, EntityStore entities) : this(bytes)
        {
            IsStable = true;
            Entities = entities;
        }
        internal string PeekString()
        {
            int saved = offset;
            string value = String();
            offset = saved;
            return value;
        }
        private static void ValidateStableUtf8(ReadOnlySpan<byte> value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                uint first = value[i];
                if (first < 128)
                    continue;
                int extra = first >= 0xC2 && first <= 0xDF ? 1 : first >= 0xE0 && first <= 0xEF ? 2
                    : first >= 0xF0 && first <= 0xF4 ? 3 : -1;
                Require(extra > 0 && extra < value.Length-i,"Invalid stable UTF-8 prefix or length.");
                uint scalar = first & (uint)(0x7F >> (extra+1));
                for (int j = 0; j < extra; j++)
                {
                    byte next = value[++i];
                    Require(next >= 0x80 && next <= 0xBF,"Invalid stable UTF-8 continuation.");
                    scalar = (scalar << 6) | (uint)(next & 0x3F);
                }
                uint minimum = extra == 1 ? 0x80u : extra == 2 ? 0x800u : 0x10000u;
                Require(scalar >= minimum && scalar <= 0x10FFFF && (scalar < 0xD800 || scalar > 0xDFFF),
                    "Noncanonical stable UTF-8 scalar.");
            }
        }
        public EntityLink Link()
        {
            EntityLink link = Bool() ? new EntityLink(new StableId(UInt64())) : default;
            Entities?.ValidateLink(link,false);
            return link;
        }
        public TaskKey ReadTaskKey() => new TaskKey(Int64(),UInt64(),new StableId(UInt64()),UInt64(),UInt64());
        public StableSimEvent StableEvent()
        {
            var type = new EventTypeId(Int32());
            long tick = Int64();
            long sequence = Int64();
            EntityLink source = Link();
            EntityLink target = Link();
            SimVec3 position = SnapshotValues.Vector(this);
            SimVec3 aux = SnapshotValues.Vector(this);
            SimFix magnitude = SimFix.FromRaw(Int64());
            SimFix radius = SimFix.FromRaw(Int64());
            int tag = Int32();
            return new StableSimEvent(new SimEvent(type,tick,position:position,aux:aux,magnitude:magnitude,radius:radius,tag:tag,sequence:sequence),source,target);
        }
        internal SimStateReader Section() => IsStable ? new SimStateReader(Bytes(),Entities) : new SimStateReader(Bytes());
    }
}
