using System;
using System.IO;
using System.Text;

namespace WarCore.Sim
{
    public sealed class SimSnapshotException : InvalidOperationException
    {
        public SimSnapshotException(string message) : base(message)
        {
        }
    }

    // Stable ids and versions belong to content contracts, never CLR names or reflection order.
    public interface ISimStateSerializer
    {
        string StateTypeId { get; }
        int StateVersion { get; }
        void SaveState(SimStateWriter writer);
        void LoadState(SimStateReader reader);
    }

    public interface IComponentSerializer<T> where T : unmanaged, IStateHash
    {
        int TypeId { get; }
        int Version { get; }
        void Write(SimStateWriter writer, in T value);
        T Read(SimStateReader reader);
    }

    // Snapshots are cold boundary operations. Explicit little-endian fields exclude padding
    // and keep the format independent of both the hash protocol and the host runtime.
    public sealed partial class SimStateWriter
    {
        private readonly MemoryStream stream;
        public SimStateWriter() => stream = new MemoryStream();
        public void Byte(byte value)
        {
            if (IsStable)
            {
                StableByte(value);
                return;
            }
            if (stream.Length == int.MaxValue)
                throw new SimBoundaryException("Snapshot exceeds the supported byte count.");
            stream.WriteByte(value);
        }

        public void Bool(bool value) => Byte((byte)(value ? 1 : 0));
        public void Int32(int value)
        {
            if (IsStable)
            {
                Int64(value);
                return;
            }
            uint bits = unchecked((uint)value);
            for (int i = 0; i < 4; i++)
            {
                Byte(unchecked((byte)bits));
                bits >>= 8;
            }
        }

        public void Int64(long value) => UInt64(unchecked((ulong)value));
        public void UInt64(ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                Byte(unchecked((byte)value));
                value >>= 8;
            }
        }

        public void String(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (IsStable)
                StableString(value);
            else
                Bytes(Encoding.UTF8.GetBytes(value));
        }

        public void Bytes(byte[] value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (IsStable)
            {
                UInt64((ulong)value.Length);
                foreach (byte item in value)
                    Byte(item);
                return;
            }
            if (stream.Length > int.MaxValue - 4L - value.Length)
                throw new SimBoundaryException("Snapshot exceeds the supported byte count.");
            Int32(value.Length);
            stream.Write(value, 0, value.Length);
        }

        public byte[] ToArray() => stream.ToArray();
    }

    public sealed partial class SimStateReader
    {
        private readonly byte[] bytes;
        private int offset;
        public int Remaining => bytes.Length - offset;
        public SimStateReader(byte[] bytes)
        {
            this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        }

        public byte Byte()
        {
            Require(Remaining != 0, "Truncated snapshot.");
            return bytes[offset++];
        }

        public bool Bool()
        {
            byte value = Byte();
            Require(value <= 1, "Invalid snapshot boolean.");
            return value != 0;
        }

        public int Int32()
        {
            if (IsStable)
            {
                long wide = Int64();
                Require(wide >= int.MinValue && wide <= int.MaxValue, "Stable integer exceeds its field range.");
                return (int)wide;
            }
            uint value = 0;
            for (int i = 0; i < 4; i++)
                value |= (uint)Byte() << (i * 8);
            return SimInteger.Int32Bits(value);
        }

        public ulong UInt64()
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++)
                value |= (ulong)Byte() << (i * 8);
            return value;
        }

        public long Int64() => SimInteger.FromBits(UInt64());
        public int Count(int maximum)
        {
            int value = Int32();
            Require(value >= 0 && value <= maximum, "Snapshot count exceeds its section bounds.");
            return value;
        }

        public byte[] Bytes()
        {
            int count = Count(Remaining);
            Require(count <= Remaining, "Truncated snapshot byte block.");
            var result = new byte[count];
            Array.Copy(bytes, offset, result, 0, count);
            offset += count;
            return result;
        }

        public string String()
        {
            byte[] value = Bytes();
            if (IsStable)
                ValidateStableUtf8(value);
            return Encoding.UTF8.GetString(value);
        }
        public void End() => Require(Remaining == 0, "Snapshot section contains trailing bytes.");
        public static void Require(bool valid, string message)
        {
            if (!valid)
                throw new SimSnapshotException(message);
        }
    }
}
