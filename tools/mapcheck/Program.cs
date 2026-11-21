using System;
using System.IO;
using System.Linq;
using System.Text;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;

namespace WarCore.MapCheck
{
    // mapcheck <map.json> <out.html>: runs the map gate on a description, prints the problems and writes a web page that shows the
    // map from above with a pin on every problem that has a place. Exit code 1 when the gate found a problem (the page is
    // written anyway), 2 on bad arguments.
    public static class Program
    {
        private const string Marker = "/*MAPCHECK-DATA*/null";

        public static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("usage: WarCore.MapCheck <map.json> <out.html>");
                return 2;
            }
            string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
            string template = Path.Combine(AppContext.BaseDirectory, "mapcheck.html");
            UseProjectRoot();
            byte[] bytes = File.ReadAllBytes(input);
            ContentCatalog catalog = ContentCatalog.Find();
            string pack = PackOf(bytes);
            bool readable = pack != null;
            if (!readable) pack = AnyLayer(catalog);
            MapPalette palette = readable ? MapPalette.ForLayer(catalog, pack) : MapPalette.Default;
            MapPreviewData data = MapPreview.Build(bytes, catalog.Load(pack), palette, MapMessages.Find());

            string page = File.ReadAllText(template, Encoding.UTF8);
            if (page.IndexOf(Marker, StringComparison.Ordinal) < 0) throw new InvalidDataException("mapcheck.html has no " + Marker);
            File.WriteAllText(output, page.Replace(Marker, MapPreview.ToJson(data)), new UTF8Encoding(false));

            foreach (MapPreviewProblem p in data.Problems) Console.WriteLine(p.Code + (p.Id.Length == 0 ? "" : " [" + p.Id + "]") + "  " + p.Text);
            foreach (MapPreviewNote n in data.Notes) Console.WriteLine("note " + n.Key + " [" + n.Name + "]  " + n.Text);
            Console.WriteLine("mapcheck: " + Path.GetFileName(input) + " problems=" + data.Problems.Count + " pins=" + data.Pins.Count + " notes=" + data.Notes.Count + " -> " + output);
            return data.Problems.Count == 0 ? 0 : 1;
        }

        // The content layer a description names, or null when it cannot be read.
        private static string PackOf(byte[] bytes)
        {
            try { return MapDescJson.Read(bytes).Pack; }
            catch (InvalidDataException) { return null; }
        }

        // Any layer of the catalog, only so that a description that cannot be read still gets a page (its one problem needs no pack).
        private static string AnyLayer(ContentCatalog catalog)
        {
            foreach (string cartridge in catalog.Cartridges)
            {
                string layer = catalog.DefaultLayer(cartridge) ?? catalog.Layers(cartridge).FirstOrDefault();
                if (layer != null) return layer;
            }
            throw new ContentException("no content layer in any cartridge");
        }

        // Content packs are read relative to the Unity project root, as in the editor.
        private static void UseProjectRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Assets", "WarCore"))) directory = directory.Parent;
            if (directory == null) throw new DirectoryNotFoundException("Cannot locate the Unity project root above the binary.");
            Directory.SetCurrentDirectory(directory.FullName);
        }
    }
}
