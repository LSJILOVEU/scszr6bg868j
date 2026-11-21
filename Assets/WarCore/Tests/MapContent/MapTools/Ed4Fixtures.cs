using System;
using System.IO;
using System.Linq;
using System.Text;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // ED-4 fixtures: the shipped maps, the message table and palette of the project, and small throw-away cartridges. No test
    // source may hold a Chinese literal (ArchitectureTests), so every message is compared with what the file says.
    internal static class Ed4Fixtures
    {
        internal const string MapDir = "Assets/WarCore/Maps/";
        internal const string TablePath = "Assets/WarCore/Content/MapTools/Text/map-messages.zh.txt";

        internal static readonly string[] MapNames =
            { "door-demo", "door-terrain-demo", "lanes-playtest", "m6-demo", "m7-demo", "plate-demo", "terrain-demo" };

        internal static byte[] MapBytes(string name) => File.ReadAllBytes(MapDir + name + ".map.json");

        internal static MapMessages Messages() => MapMessages.Load(TablePath);

        // The pack and palette the preview of these bytes would use, found through the project's own catalog.
        internal static ContentPack PackOf(byte[] bytes) => ContentCatalog.Find().Load(MapDescJson.Read(bytes).Pack);

        internal static MapPalette PaletteOf(byte[] bytes) => MapPalette.ForLayer(ContentCatalog.Find(), MapDescJson.Read(bytes).Pack);

        internal static MapPreviewData Preview(byte[] bytes) => MapPreview.Build(bytes, PackOf(bytes), PaletteOf(bytes), Messages());

        // A cartridge data directory under dirs holding layer "<name>1" and, when text is not null, a palette.json.
        internal static string Cartridge(Ed0Dirs dirs, string name, string palette)
        {
            string dir = dirs.Dir(name + "/Data");
            Ed0Dirs.Layer(dir, name.ToLowerInvariant() + "1");
            if (palette != null) File.WriteAllText(Path.Combine(dir, ContentCatalog.ColorsFile + ".json"), palette, new UTF8Encoding(false));
            return dir;
        }

        internal static string Hex(int rgb) => rgb.ToString("X6");

        // A text file of the message table format in the temp folder; returns its path.
        internal static string TableFile(Ed0Dirs dirs, string text)
        {
            string path = Path.Combine(dirs.Root, "table-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        // The ids of the problems a gate run reports, for comparing orders.
        internal static string[] Codes(System.Collections.Generic.IEnumerable<MapProblem> problems) =>
            problems.Select(p => p.Code + "|" + p.Id).ToArray();
    }
}
