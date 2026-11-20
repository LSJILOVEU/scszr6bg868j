using System;
using System.IO;
using WarCore.Content;

namespace WarCore.Tests
{
    // ED-0 fixtures: throw-away content data directories in the temp folder, and the two catalog environment variables saved
    // and restored around each test (as ContentRootTests does for WARCORE_CONTENT_DIR).
    internal sealed class Ed0Dirs : IDisposable
    {
        internal const string Weapons = "\"weapons\": [ { \"id\": \"w\", \"fireIntervalMilli\": 3390, \"magazine\": 30, \"reloadMilli\": 60000, "
            + "\"falloffStart\": 34, \"falloffEnd\": 67, \"effectRaw\": 32768, \"spreadBase\": 1000, \"spreadGrowth\": 340, "
            + "\"spreadRecoverPerSec\": 1400, \"spreadMax\": 3000 } ]";
        internal const string Combat = "\"combat\": { \"maxHealth\": 100, \"resolver\": \"rwr.hit\", \"selector\": \"nearest-visible\", "
            + "\"friendlyFire\": false, \"friendlyFireLocked\": false, \"allyCheckMissRate\": 0, \"allyHoldMilli\": 30000 }";
        internal const string Match = "\"match\": { \"captureAdvantageNum\": 2, \"captureAdvantageDen\": 1, \"captureStrict\": false, "
            + "\"captureTicks\": 300, \"interrupt\": \"reset\", \"decayPerTick\": 1, \"intervalTicks\": 90, \"thinkInterval\": 30, "
            + "\"maxTicks\": 9000, \"sight\": 35, \"gridColumns\": 4, \"gridRows\": 4, \"gridSpacing\": 1.5 }";
        internal const string Terrain = "\"surfaces\": [ { \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }, { \"id\": \"grass\" } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }";

        private readonly string previousList, previousSingle;
        internal string Root { get; }

        internal Ed0Dirs()
        {
            previousList = Environment.GetEnvironmentVariable(ContentCatalog.DirectoriesVariable);
            previousSingle = Environment.GetEnvironmentVariable(FileContentSource.DirectoryVariable);
            Environment.SetEnvironmentVariable(ContentCatalog.DirectoriesVariable, null);
            Environment.SetEnvironmentVariable(FileContentSource.DirectoryVariable, null);
            Root = Path.Combine(Path.GetTempPath(), "warcore-ed0-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(ContentCatalog.DirectoriesVariable, previousList);
            Environment.SetEnvironmentVariable(FileContentSource.DirectoryVariable, previousSingle);
            Directory.Delete(Root, true);
        }

        // A directory under Root (relative path like "Zeta/Data"), created empty.
        internal string Dir(string relative)
        {
            string dir = Path.Combine(Root, relative);
            Directory.CreateDirectory(dir);
            return dir;
        }

        // A layer with a combat section and one unit type per name; extra holds more sections (comma-led) or "".
        internal static void Layer(string dir, string id, string extra = "", params string[] unitTypes)
        {
            if (unitTypes.Length == 0) unitTypes = new[] { id + ".soldier" };
            string units = "";
            foreach (string u in unitTypes) units += (units.Length == 0 ? "" : ", ") + "{ \"id\": \"" + u + "\", \"weapon\": \"w\" }";
            File.WriteAllText(Path.Combine(dir, id + ".json"),
                "{ \"id\": \"" + id + "\", " + Weapons + ", \"unitTypes\": [ " + units + " ], " + Combat + extra + " }");
        }

        internal static void Editor(string dir, string text) => File.WriteAllText(Path.Combine(dir, ContentCatalog.EditorFile + ".json"), text);

        internal static void UseList(params string[] dirs) =>
            Environment.SetEnvironmentVariable(ContentCatalog.DirectoriesVariable, string.Join(Path.PathSeparator.ToString(), dirs));
    }
}
