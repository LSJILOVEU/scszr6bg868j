using System.IO;
using System.Text;
using NUnit.Framework;

namespace WarCore.Tests
{
    // BODY-1: the shipped map files with their "pack" line swapped in memory (the files on disk are never touched).
    internal static class Body1Maps
    {
        internal const string Lanes = "Assets/WarCore/Maps/lanes-playtest.map.json";
        internal const string M6 = "Assets/WarCore/Maps/m6-demo.map.json";
        internal const string M7 = "Assets/WarCore/Maps/m7-demo.map.json";
        internal const string Door = "Assets/WarCore/Maps/door-demo.map.json";
        internal const string Plate = "Assets/WarCore/Maps/plate-demo.map.json";
        internal const string Terrain = "Assets/WarCore/Maps/terrain-demo.map.json";
        internal const string DoorTerrain = "Assets/WarCore/Maps/door-terrain-demo.map.json";

        // The map file with  "pack": "<from>"  replaced by  "pack": "<to>" . A file that does not say <from> fails the test
        // (so a renamed pack in the map can never turn this into a silent no-op).
        internal static byte[] WithPack(string path, string from, string to)
        {
            string text = File.ReadAllText(path), want = "\"pack\": \"" + from + "\"";
            Assert.That(text, Does.Contain(want), path + " no longer says " + want);
            return new UTF8Encoding(false).GetBytes(text.Replace(want, "\"pack\": \"" + to + "\""));
        }
    }
}
