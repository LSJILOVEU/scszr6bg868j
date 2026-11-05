using System;
using System.Security.Cryptography;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed partial class FixedMathOracleTests
    {
        [TestCase("mul", 0xA11CE001UL, "6474d2b3bad56966fe1dc6557d055bc94e77d7820a2396417b9f3a2fb32115ce")]
        [TestCase("div", 0xA11CE002UL, "cfb428abe88187179e1f2f91672c994db99fd465818006c64dd0a13d66b472da")]
        public void PythonCoverageStreamMatchesTheActualCSharpGenerator(string operation, ulong seed, string expected)
        {
            long[] edges = Edges();
            var random = new Inputs(seed);
            var bytes = new byte[(edges.Length + RandomCases) * 16];
            int offset = 0;
            for (int i = 0; i < edges.Length + RandomCases; i++)
            {
                long a = i < edges.Length ? edges[i] : random.Sample(edges);
                long b = i < edges.Length ? edges[i * 37 % edges.Length] : random.Sample(edges);
                if (i >= edges.Length && operation == "mul" && i % 2 == 0)
                {
                    int bits = (int)(random.Next() % 62) + 1;
                    a = random.Bits(bits);
                    b = random.Bits(63 - bits);
                }
                foreach (long value in new[] { a, b })
                {
                    ulong raw = unchecked((ulong)value);
                    for (int j = 0; j < 8; j++)
                    {
                        bytes[offset++] = unchecked((byte)raw);
                        raw >>= 8;
                    }
                }
            }
            using (SHA256 hash = SHA256.Create())
            {
                string actual = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                Assert.That(actual, Is.EqualTo(expected));
                TestContext.WriteLine("Python/C# coverage stream {0}: pairs={1}; sha256={2}.", operation, edges.Length + RandomCases, actual);
            }
        }
    }
}
