using System;
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

        // Environment variable that names the data directory itself (the folder that holds rwr.json and the other layers).
        public const string DirectoryVariable = "WARCORE_CONTENT_DIR";

        // Where the data is: an explicit directory first, then the environment variable, then the project folder found by
        // looking upwards from the working directory (so a test host or a tool started in a sub-folder finds the same files).
        // A packaged build has no project folder above its working directory and names the directory in one of the first two ways.
        public static FileContentSource Rwr(string dataDirectory = null)
        {
            if (string.IsNullOrEmpty(dataDirectory)) dataDirectory = Environment.GetEnvironmentVariable(DirectoryVariable);
            if (!string.IsNullOrEmpty(dataDirectory))
            {
                if (!Directory.Exists(dataDirectory)) throw new ContentException("content data directory " + dataDirectory + " does not exist");
                return new FileContentSource(dataDirectory);
            }
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, RwrDirectory);
                if (Directory.Exists(candidate)) return new FileContentSource(candidate);
                dir = dir.Parent;
            }
            throw new ContentException("content data directory " + RwrDirectory + " not found (searched upwards from " + Directory.GetCurrentDirectory()
                + "; pass a directory or set " + DirectoryVariable + ")");
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
