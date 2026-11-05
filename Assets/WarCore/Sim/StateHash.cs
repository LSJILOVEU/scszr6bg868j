using System;
using System.Runtime.CompilerServices;
using System.Text;

// The player verification harness needs the same internal compatibility path as EditMode tests.
[assembly: InternalsVisibleTo("WarCore.Verification")]

namespace WarCore.Sim
{
    public interface IStateHash
    {
        void WriteState(StateHash hash);
    }

    // Explicit field encoding avoids runtime hash codes, memory padding and machine byte order.
    public sealed class StateHash
    {
        private ulong value = 14695981039346656037UL;
        public ulong Value => value;
        internal bool IgnoreColliderLayers { get; }
        internal bool IgnoreColliderStatic { get; }
        // Version 4 appends box depth and yaw after the version 3 fields; capsule bytes are unchanged.
        public const int ColliderEncodingVersion = 4;

        public StateHash()
        {
        }

        // Compatibility omits bytes entirely; zero is a real value in the current encoding.
        internal StateHash(bool ignoreColliderLayers, bool ignoreColliderStatic = true)
        {
            IgnoreColliderLayers = ignoreColliderLayers;
            IgnoreColliderStatic = ignoreColliderStatic;
        }

        internal void Reset()
        {
            value = 14695981039346656037UL;
        }

        public void Add(byte item)
        {
            value = unchecked((value ^ item) * 1099511628211UL);
        }

        public void Add(bool item)
        {
            Add((byte)(item ? 1 : 0));
        }

        public void Add(int item)
        {
            Add(unchecked((ulong)(long)item));
        }

        public void Add(long item)
        {
            Add(unchecked((ulong)item));
        }

        public void Add(ulong item)
        {
            for (int i = 0; i < 8; i++)
            {
                Add(unchecked((byte)item));
                item >>= 8;
            }
        }

        internal void AddUtf8(ReadOnlySpan<byte> bytes)
        {
            Add(bytes.Length);
            foreach (byte item in bytes)
                Add(item);
        }

        public void Add(string item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            // Stable ids are normally ASCII. Emit the identical UTF-8 bytes directly.
            // Retain the existing encoder and its fallback behavior for all other strings.
            bool ascii = true;
            for (int i = 0; i < item.Length; i++)
                if (item[i] > 127)
                {
                    ascii = false;
                    break;
                }
            if (ascii)
            {
                Add(item.Length);
                for (int i = 0; i < item.Length; i++)
                    Add((byte)item[i]);
                return;
            }
            Add(Encoding.UTF8.GetByteCount(item));
            Span<byte> bytes = stackalloc byte[192];
            int offset = 0;
            while (offset < item.Length)
            {
                int count = item.Length - offset;
                if (count > 64)
                    count = 64;
                // Never split a surrogate pair: chunking must preserve the original encoder fallback.
                if (offset + count < item.Length && char.IsHighSurrogate(item[offset + count - 1]))
                    count--;
                int written = Encoding.UTF8.GetBytes(item.AsSpan(offset, count), bytes);
                for (int i = 0; i < written; i++)
                    Add(bytes[i]);
                offset += count;
            }
        }
    }
}
