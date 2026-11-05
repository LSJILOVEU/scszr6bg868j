using System;
using System.IO;
using WarCore.Verification.Fixtures;

namespace WarCore.Verification
{
    public static class Round4Runner
    {
        public static void Run(string mode, string path, bool longRun, bool placed = false)
        {
            using (var output = new StreamWriter(path) { AutoFlush=true })
            {
                Round4ChurnBench.Run(mode,output,placed);
                if (longRun)
                    Round4LongRun.Run(output);
            }
            Console.Write(File.ReadAllText(path));
        }
    }
}
