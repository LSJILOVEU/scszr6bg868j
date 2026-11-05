using WarCore.Sim;

namespace WarCore.Verification
{
    internal static class StaticColliderScenarios
    {
        internal static void Run(Report report)
        {
            Check(report, false, 0xE1EFB853EBEB6ABAUL);
            Check(report, true, 0xE1EFB953EBEB6C6DUL);
        }

        private static void Check(Report report, bool stationary, ulong expected)
        {
            var collider = new Collider(SimFix.FromInt(1), SimFix.FromInt(2), 0x80000000u).WithStatic(stationary);
            var hash = new StateHash();
            collider.WriteState(hash);
            HashScenarios.Require(hash.Value == expected, "Collider v3 known hash changed.");
            var writer = new SimStateWriter();
            var serializer = new ColliderSerializer();
            serializer.Write(writer, collider);
            byte[] bytes = writer.ToArray();
            HashScenarios.Require(bytes.Length == 29 && bytes[28] == (stationary ? 1 : 0), "Collider v3 static snapshot byte changed.");
            var reader = new SimStateReader(bytes);
            Collider restored = serializer.Read(reader);
            reader.End();
            HashScenarios.Require(restored.IsStatic == stationary && restored.Layer == collider.Layer, "Static Collider round trip changed.");
            report.Line(stationary ? "staticCollider.static" : "staticCollider.unit", "hash=" + StableDigest.Hex64(hash.Value));
        }
    }
}
