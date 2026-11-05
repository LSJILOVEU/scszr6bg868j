using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Test-side envelope builder: corrupt payloads get valid digests, so the reader reaches semantic validation.
    internal static class Wp22Envelope
    {
        internal static byte[][] Payloads(byte[] bytes)
        {
            var result = new byte[12][];
            using var reader = new BinaryReader(new MemoryStream(bytes));
            reader.BaseStream.Position = 8;
            for (int i = 1; i <= 11; i++)
            {
                Assert.That(reader.ReadUInt16(), Is.EqualTo(i));
                result[i] = reader.ReadBytes((int)reader.ReadUInt32());
                reader.BaseStream.Position += 32;
            }
            return result;
        }

        internal static void Put(byte[] bytes, int offset, Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream(bytes, true);
            stream.Position = offset;
            using var writer = new BinaryWriter(stream);
            write(writer);
        }

        internal static byte[] Pack(byte[][] payloads)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            using var sha = SHA256.Create();
            writer.Write(new byte[] { 87, 67, 77, 80, 2, 0, 11, 0 });
            var offsets = new int[12];
            for (ushort id = 1; id <= 11; id++)
            {
                offsets[id] = (int)stream.Position;
                if (id == 10)
                {
                    int at = 42;
                    foreach (int reference in new[] { 3, 4, 6, 7 })
                    { Put(payloads[10], at, w => w.Write((uint)offsets[reference])); at += 6; }
                }
                writer.Write(id); writer.Write((uint)payloads[id].Length); writer.Write(payloads[id]);
                writer.Write(sha.ComputeHash(stream.ToArray(), offsets[id], payloads[id].Length + 6));
            }
            writer.Write(sha.ComputeHash(stream.ToArray()));
            return stream.ToArray();
        }

        internal static void Reject(byte[] bytes, string code)
            => Assert.That(Assert.Throws<InvalidDataException>(() => WcMapReader.Read(bytes)).Message, Is.EqualTo("WCMAP_" + code));

        internal static string Sha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
    }

    // Independent reference: planes from three vertices (cross product) and exact BigInteger rationals. It shares no
    // formula or branch with HeightField.
    internal static class Wp22Oracle
    {
        internal const long S = SimFix.Scale;

        internal static BigInteger FloorDiv(BigInteger n, BigInteger d)
        {
            if (d.Sign < 0) { n = -n; d = -d; }
            BigInteger q = BigInteger.DivRem(n, d, out BigInteger r);
            return r.Sign < 0 ? q - 1 : q;
        }

        // Plane through (x,h,z) vertices; returns the normal (nx, nh, nz).
        private static (BigInteger nx, BigInteger nh, BigInteger nz) Normal(long[] p0, long[] p1, long[] p2)
        {
            BigInteger ax = p1[0] - p0[0], ah = p1[1] - p0[1], az = p1[2] - p0[2];
            BigInteger bx = p2[0] - p0[0], bh = p2[1] - p0[1], bz = p2[2] - p0[2];
            return (ah * bz - az * bh, az * bx - ax * bz, ax * bh - ah * bx);
        }

        // The two triangles of the cell with corner heights h00 h10 h01 h11, diagonal (0,0)-(1,1) in local raw metres.
        internal static long[][] Triangle(bool lower, long h00, long h10, long h01, long h11) => lower
            ? new[] { new[] { 0L, h00, 0L }, new[] { S, h10, 0L }, new[] { S, h11, S } }
            : new[] { new[] { 0L, h00, 0L }, new[] { 0L, h01, S }, new[] { S, h11, S } };

        // Floor of the plane height at local (u, v), 0 <= u, v <= S (closed cell: used for shared-edge checks).
        internal static long HeightAt(long h00, long h10, long h01, long h11, long u, long v)
        {
            long[][] t = Triangle(u >= v, h00, h10, h01, h11);
            var (nx, nh, nz) = Normal(t[0], t[1], t[2]);
            // nx*(u-x0) + nh*(H-h0) + nz*(v-z0) = 0  =>  H = h0 - (nx*(u-x0) + nz*(v-z0)) / nh
            BigInteger numerator = (BigInteger)t[0][1] * nh - (nx * (u - t[0][0]) + nz * (v - t[0][2]));
            return (long)FloorDiv(numerator, nh);
        }

        // slope^2 = (nx^2 + nz^2) / nh^2 <= (num/den)^2 for both triangles of the cell.
        internal static bool CellWithin(long h00, long h10, long h01, long h11, long num, long den)
        {
            foreach (bool lower in new[] { true, false })
            {
                long[][] t = Triangle(lower, h00, h10, h01, h11);
                var (nx, nh, nz) = Normal(t[0], t[1], t[2]);
                if ((nx * nx + nz * nz) * den * den > (BigInteger)num * num * nh * nh) return false;
            }
            return true;
        }
    }
}
