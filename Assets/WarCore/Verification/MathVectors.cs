using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using WarCore.Sim;
using WarCore.Verification.Fixtures;

namespace WarCore.Verification
{
    // Mirrors MathVectorChecks' public-product calls, including every wide word and conversion.
    internal static class MathVectors
    {
        internal static SimVec3 Vector(long[] raw) => new SimVec3(SimFix.FromRaw(raw[0]), SimFix.FromRaw(raw[1]), SimFix.FromRaw(raw[2]));

        internal static void Run(Report report, string key, string json, bool legacy)
        {
            VectorJson document = VectorJson.Parse(json);
            var keys = new List<string>(document.Keys);
            keys.Sort(StringComparer.Ordinal);
            var digest = new StableDigest();
            foreach (string operation in keys)
            {
                if (operation == "meta" || (legacy && (operation == "sin" || operation == "cos" || operation == "atan2")))
                    continue;
                VectorJson rows = document[operation];
                for (int i = 0; i < rows.Count; i++)
                {
                    string id = operation + "[" + i.ToString("D4", CultureInfo.InvariantCulture) + "]";
                    digest.Row(id + "=" + Evaluate(operation, rows[i], id));
                }
            }
            report.Digest(key, digest);
        }

        private static string Expect(VectorJson expected, Func<long> operation, string id)
        {
            long value = 0;
            Exception error = null;
            try
            {
                value = operation();
            }
            catch (Exception caught)
            {
                error = caught;
            }
            if (expected.IsString)
            {
                Type wanted = ArithmeticContractV2.Expected(expected.Text);
                if (error == null || error.GetType() != wanted)
                    throw new InvalidOperationException(id + ": arithmetic-v2 exception mismatch; expected=" + wanted + "; actual=" + error);
                return "exception:" + error.GetType().FullName;
            }
            if (error != null || value != expected.Integer)
                throw new InvalidOperationException(id + ": numeric mismatch; actual=" + value + "; expected=" + expected.Integer, error);
            return "value:" + StableDigest.Number(value);
        }

        private static string Evaluate(string operation, VectorJson row, string id)
        {
            if (operation == "midpoint")
            {
                SimVec3 value = SimVec3.Midpoint(Vector(row["a"].Integers()), Vector(row["b"].Integers()));
                if (!value.Equals(Vector(row["expect"].Integers())))
                    throw new InvalidOperationException(id + ": midpoint mismatch");
                return value.X.Raw + "," + value.Y.Raw + "," + value.Z.Raw;
            }
            if (operation == "vec3_wide")
            {
                SimVec3 a = Vector(row["a"].Integers());
                SimVec3 b = Vector(row["b"].Integers());
                SimWideVec3 cross = SimVec3.Cross(a, b);
                return Wide(row, "dot", SimVec3.Dot(a, b), id) + ";" + Wide(row, "lengthSquaredA", a.LengthSquared, id)
                    + ";cross=" + Words(cross.X, row["cross"][0].Text, id) + "/" + Words(cross.Y, row["cross"][1].Text, id)
                    + "/" + Words(cross.Z, row["cross"][2].Text, id);
            }
            long x = row[0].Integer;
            if (operation == "sqrt") return Expect(row[1], () => SimMath.Sqrt(SimFix.FromRaw(x)).Raw, id);
            if (operation == "sin") return Expect(row[1], () => SimMath.Sin(SimFix.FromRaw(x)).Raw, id);
            if (operation == "cos") return Expect(row[1], () => SimMath.Cos(SimFix.FromRaw(x)).Raw, id);
            long y = row[1].Integer;
            switch (operation)
            {
                case "mul": return Expect(row[2], () => (SimFix.FromRaw(x) * SimFix.FromRaw(y)).Raw, id);
                case "div": return Expect(row[2], () => (SimFix.FromRaw(x) / SimFix.FromRaw(y)).Raw, id);
                case "ratio": return Expect(row[2], () => SimFix.Ratio(x, y).Raw, id);
                case "atan2": return Expect(row[2], () => SimMath.Atan2(SimFix.FromRaw(x), SimFix.FromRaw(y)).Raw, id);
                default: throw new InvalidOperationException("Unknown math vector: " + operation);
            }
        }

        private static string Wide(VectorJson row, string prefix, SimWide value, string id) => prefix + "="
            + Words(value, row[prefix].Text, id) + ";toFix=" + Expect(row[prefix + "_toSimFix"], () => value.ToSimFix().Raw, id)
            + ";sqrt=" + Expect(row[prefix + "_sqrt"], () => value.Sqrt().Raw, id);

        private static string Words(SimWide value, string expectedText, string id)
        {
            BigInteger expected = BigInteger.Parse(expectedText, CultureInfo.InvariantCulture);
            BigInteger magnitude = BigInteger.Abs(expected);
            var words = new string[13];
            words[0] = value.Sign.ToString(CultureInfo.InvariantCulture);
            if (value.Sign != expected.Sign) throw new InvalidOperationException(id + ": wide sign mismatch");
            for (int i = 0; i < 12; i++)
            {
                uint word = value.RawWord(i);
                if (word != (uint)(magnitude & uint.MaxValue)) throw new InvalidOperationException(id + ": wide word mismatch " + i);
                words[i + 1] = word.ToString("X8", CultureInfo.InvariantCulture);
                magnitude >>= 32;
            }
            if (!magnitude.IsZero) throw new InvalidOperationException(id + ": wide tail mismatch");
            return string.Join(",", words);
        }
    }
}
