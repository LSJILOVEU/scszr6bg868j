using System.Collections.Generic;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // A3 fixtures. The content layer is A2's literal (A2Fixture.cs PackJson) with the ai section appended when given;
    // everything else that shapes behaviour comes from A2's fixture (map, weapon, match, seed) or is written here.
    internal static class A3
    {
        internal const string Grass500 = ", \"concealMilli\": 500";

        // memory == null leaves the ai key out; otherwise "ai": { "targetMemoryTicks": memory }.
        internal static string PackJson(string grass, int? memory) => "{ \"id\": \"a2-test\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"dirt\" }, { \"id\": \"grass\", \"speedMilli\": 800" + grass + " } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }"
            + (memory == null ? "" : ", \"ai\": { \"targetMemoryTicks\": " + memory.Value + " }") + " }";
        internal static string PackJson(string grass, string ai) => "{ \"id\": \"a2-test\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"dirt\" }, { \"id\": \"grass\", \"speedMilli\": 800" + grass + " } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }, \"ai\": " + ai + " }";
        internal static ContentPack Load(string json) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["a2-test"] = json }, FileContentSource.Rwr()), "a2-test");
        internal static ContentPack Pack(string grass, int? memory) => Load(PackJson(grass, memory));

        // 64-bit FNV-1a over the bytes (constants as A2Fixture.cs HashRun).
        internal static ulong Digest(byte[] bytes)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in bytes) h = (h ^ b) * 1099511628211UL;
            return h;
        }
    }
}
