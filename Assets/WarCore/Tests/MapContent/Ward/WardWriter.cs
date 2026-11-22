using System.IO;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // MAP-BL0: writes the map file from the build code. Run by hand after a change to the layout or the build; WardFileTests
    // then locks the bytes. Not part of any suite.
    public sealed class WardWriter
    {
        [Test, Explicit("writes the map file"), Category("Manual")]
        public void WriteFile() => File.WriteAllBytes(WardWorld.MapPath, WardBuild.Document(41).Save());
    }
}
