using System.IO;

namespace WarCore.Content
{
    public interface IContentSource
    {
        // Returns false when the layer does not exist; location is only used in error messages.
        bool TryRead(string layerId, out string text, out string location);
    }

    // Reads <directory>/<layerId>.json.
    public sealed class FileContentSource : IContentSource
    {
        public const string RwrDirectory = "Assets/WarCore/Content/Rwr/Data";
        private readonly string directory;
        public FileContentSource(string directory) { this.directory = directory; }

        // The data lives in the Unity project; look upwards from the working directory so a test host or a tool started
        // in a sub-folder finds the same files.
        public static FileContentSource Rwr()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, RwrDirectory);
                if (Directory.Exists(candidate)) return new FileContentSource(candidate);
                dir = dir.Parent;
            }
            throw new ContentException("content data directory " + RwrDirectory + " not found (searched upwards from " + Directory.GetCurrentDirectory() + ")");
        }

        public bool TryRead(string layerId, out string text, out string location)
        {
            location = Path.Combine(directory, layerId + ".json");
            text = null;
            if (!File.Exists(location)) return false;
            text = File.ReadAllText(location);
            return true;
        }
    }
}
