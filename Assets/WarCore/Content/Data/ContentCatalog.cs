using System;
using System.Collections.Generic;
using System.IO;

namespace WarCore.Content
{
    // The content data directories a map tool can choose from. A cartridge is one data directory; a layer is one
    // <layer id>.json in it (the "pack" of a map description names a layer, never a cartridge). A file in a folder under
    // it (parts/<name>.json, id "parts/<name>") is a part: not a layer of the catalog, only what the layers that list it
    // in "extends" are made of.
    // Cartridge name: the parent folder's name when the directory itself is called "Data" (Content/Rwr/Data -> Rwr), else the
    // directory's own name. Cartridges and layers are kept in ordinal order, so nothing depends on the file system's order.
    // Reserved file names (not layers): editor.json (the cartridge's editor settings) and palette.json (its colours).
    public sealed class ContentCatalog
    {
        // Path.PathSeparator-separated list of data directories; read before DirectoryVariable.
        public const string DirectoriesVariable = "WARCORE_CONTENT_DIRS";
        public const string EditorFile = "editor";
        public const string ColorsFile = "palette";
        private const string ContentRoot = "Assets/WarCore/Content";

        private readonly string[] names;
        private readonly string[] directories;
        private readonly string[][] layers;

        public IReadOnlyList<string> Cartridges => Array.AsReadOnly(names);

        // directories: data directories in any order; each must exist.
        public ContentCatalog(IEnumerable<string> directories)
        {
            if (directories == null) throw new ArgumentNullException(nameof(directories));
            var found = new List<(string Name, string Dir)>();
            foreach (string dir in directories)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                if (!Directory.Exists(dir)) throw new ContentException("content data directory " + dir + " does not exist");
                found.Add((NameOf(dir), dir));
            }
            found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = 1; i < found.Count; i++)
                if (found[i].Name == found[i - 1].Name)
                    throw new ContentException("two content data directories are both cartridge '" + found[i].Name + "': " + found[i - 1].Dir + " and " + found[i].Dir);
            names = new string[found.Count];
            this.directories = new string[found.Count];
            layers = new string[found.Count][];
            for (int i = 0; i < found.Count; i++)
            {
                names[i] = found[i].Name;
                this.directories[i] = found[i].Dir;
                layers[i] = LayersIn(found[i].Dir);
            }
        }

        // Directories from WARCORE_CONTENT_DIRS, else WARCORE_CONTENT_DIR (one directory), else every Content/<name>/Data of the
        // project found by looking upwards from the working directory.
        public static ContentCatalog Find()
        {
            string list = Environment.GetEnvironmentVariable(DirectoriesVariable);
            if (!string.IsNullOrEmpty(list))
            {
                string[] items = list.Split(Path.PathSeparator);
                if (Array.TrueForAll(items, string.IsNullOrEmpty)) throw new ContentException(DirectoriesVariable + " names no directory");
                return new ContentCatalog(items);
            }
            string single = Environment.GetEnvironmentVariable(FileContentSource.DirectoryVariable);
            if (!string.IsNullOrEmpty(single)) return new ContentCatalog(new[] { single });
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                string root = Path.Combine(dir.FullName, ContentRoot);
                if (Directory.Exists(root))
                {
                    var data = new List<string>();
                    foreach (string sub in Directory.GetDirectories(root))
                    {
                        string candidate = Path.Combine(sub, "Data");
                        if (Directory.Exists(candidate)) data.Add(candidate);
                    }
                    if (data.Count == 0) throw new ContentException("no content data directory under " + root);
                    return new ContentCatalog(data);
                }
                dir = dir.Parent;
            }
            throw new ContentException("content folder " + ContentRoot + " not found (searched upwards from " + Directory.GetCurrentDirectory()
                + "; set " + DirectoriesVariable + " or " + FileContentSource.DirectoryVariable + ")");
        }

        public IReadOnlyList<string> Layers(string cartridge) => Array.AsReadOnly(layers[IndexOf(cartridge)]);

        // The layer a new map of this cartridge starts with, or null when the cartridge has none. Read on each call, so a broken
        // editor.json or layer only fails the cartridge that has it.
        public string DefaultLayer(string cartridge) => DefaultOf(IndexOf(cartridge));

        // The cartridge that holds the layer; ContentException when none or more than one does.
        public string CartridgeOf(string layerId) => names[Holder(layerId)];

        // The data directory of a cartridge, as it was given to the constructor.
        public string DirectoryOf(string cartridge) => directories[IndexOf(cartridge)];

        public ContentPack Load(string layerId) => ContentLoader.Load(new FileContentSource(directories[Holder(layerId)]), layerId);

        private int Holder(string layerId)
        {
            if (string.IsNullOrEmpty(layerId)) throw new ContentException("content layer id is empty");
            int at = -1;
            for (int i = 0; i < names.Length; i++)
            {
                if (Array.BinarySearch(layers[i], layerId, StringComparer.Ordinal) < 0) continue;
                if (at >= 0) throw new ContentException("content layer '" + layerId + "' is in two cartridges: " + names[at] + " and " + names[i]);
                at = i;
            }
            if (at < 0) throw new ContentException("content layer '" + layerId + "' is in no cartridge (looked in: " + string.Join(", ", names) + ")");
            return at;
        }

        private int IndexOf(string cartridge)
        {
            int i = Array.IndexOf(names, cartridge);
            if (i < 0) throw new ContentException("no cartridge '" + cartridge + "' (have: " + string.Join(", ", names) + ")");
            return i;
        }

        private static string NameOf(string dir)
        {
            var info = new DirectoryInfo(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return info.Name == "Data" && info.Parent != null ? info.Parent.Name : info.Name;
        }

        private static string[] LayersIn(string dir)
        {
            var ids = new List<string>();
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                if (!string.Equals(Path.GetExtension(file), ".json", StringComparison.Ordinal)) continue;
                string id = Path.GetFileNameWithoutExtension(file);
                if (id == EditorFile || id == ColorsFile) continue;
                ids.Add(id);
            }
            string[] sorted = ids.ToArray();
            Array.Sort(sorted, StringComparer.Ordinal);
            return sorted;
        }

        // editor.json { "defaultLayer": "<layer id of this directory>" }; without it the first layer (ordinal) that has both
        // terrain rules and a match section, or null.
        private string DefaultOf(int i)
        {
            string path = Path.Combine(directories[i], EditorFile + ".json");
            if (File.Exists(path))
            {
                string where = "cartridge '" + names[i] + "' editor settings";
                JsonNode root = ContentJson.Parse(File.ReadAllText(path), where);
                if (root.Kind != JsonKind.Object) throw new ContentException(where + " must be a JSON object");
                foreach (var member in root.Members)
                    if (member.Name != "defaultLayer") throw new ContentException(where + " has unknown field '" + member.Name + "'");
                JsonNode layer = root.Find("defaultLayer");
                if (layer == null) return null;
                if (layer.Kind != JsonKind.String) throw new ContentException(where + " field 'defaultLayer' must be a string");
                if (Array.BinarySearch(layers[i], layer.Text, StringComparer.Ordinal) < 0)
                    throw new ContentException(where + ": default layer '" + layer.Text + "' is not a layer of this cartridge");
                return layer.Text;
            }
            var source = new FileContentSource(directories[i]);
            foreach (string id in layers[i])
            {
                ContentPack pack = ContentLoader.Load(source, id);
                if (pack.Terrain != null && pack.HasMatch) return id;
            }
            return null;
        }
    }
}
