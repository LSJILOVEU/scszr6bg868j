using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WarCore.Verification
{
    // FNV-1a 64 over UTF-8, with a single LF after every logical row. No runtime GetHashCode.
    internal sealed class StableDigest
    {
        private ulong value = 14695981039346656037UL;
        internal int Rows { get; private set; }
        internal string Hex => value.ToString("X16", CultureInfo.InvariantCulture);
        internal void Row(string row)
        {
            foreach (byte item in Encoding.UTF8.GetBytes(row + "\n"))
                value = unchecked((value ^ item) * 1099511628211UL);
            Rows++;
        }
        internal static string Hex64(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);
        internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        internal static string Capture(Func<long> operation)
        {
            try { return "value:" + Number(operation()); }
            catch (Exception error) { return "exception:" + error.GetType().FullName; }
        }
    }

    internal sealed class Report : IDisposable
    {
        private readonly StreamWriter writer;
        internal Report(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
        }
        internal void Line(string key, string value)
        {
            string line = key + "|" + value;
            writer.WriteLine(line);
            Console.WriteLine(line);
        }
        internal void Digest(string key, StableDigest digest) => Line(key, "rows=" + digest.Rows + ";fnv1a64=" + digest.Hex);
        public void Dispose() => writer.Dispose();
    }
}
