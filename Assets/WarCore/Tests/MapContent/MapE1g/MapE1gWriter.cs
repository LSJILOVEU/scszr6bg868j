using System.IO;
using NUnit.Framework;

namespace WarCore.Tests
{
    // MAP-E1g: writes the two generated files from the build code. Run by hand after a change to the layout or the build;
    // MapE1gFileTests then locks the bytes. Not part of any suite.
    public sealed class MapE1gWriter
    {
        [Test, Explicit("writes the map files"), Category("Manual")]
        public void WriteFiles()
        {
            File.WriteAllText(MapE1gBuild.GenPath, MapE1gBuild.GenText(), new System.Text.UTF8Encoding(false));
            File.WriteAllBytes(MapE1gBuild.MapPath, MapE1gBuild.Build().Save());
        }
    }
}
