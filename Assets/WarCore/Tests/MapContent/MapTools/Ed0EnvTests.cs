using System.IO;
using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    // ED-0 acceptance 6: where the catalog finds its directories.
    public sealed class Ed0EnvTests
    {
        private Ed0Dirs dirs;

        [SetUp] public void Make() => dirs = new Ed0Dirs();
        [TearDown] public void Clean() => dirs.Dispose();

        private string Cartridge(string relative, string layer)
        {
            string dir = dirs.Dir(relative);
            Ed0Dirs.Layer(dir, layer);
            return dir;
        }

        [Test]
        public void TheListVariableNamesEveryDirectoryAndSkipsEmptyItems()
        {
            string a = Cartridge("Aa", "a"), b = Cartridge("Bb/Data", "b");
            Ed0Dirs.UseList("", a, "", b, "");
            ContentCatalog catalog = null;
            Assert.DoesNotThrow(() => catalog = ContentCatalog.Find());
            Assert.That(catalog.Cartridges, Is.EqualTo(new[] { "Aa", "Bb" }));
        }

        [Test]
        public void AListOfOnlyEmptyItemsIsRefused()
        {
            Ed0Dirs.UseList("", "");
            var e = Assert.Throws<ContentException>(() => ContentCatalog.Find());
            StringAssert.Contains(ContentCatalog.DirectoriesVariable, e.Message);
        }

        [Test]
        public void AMissingDirectoryInTheListIsRefused()
        {
            string a = Cartridge("Aa", "a"), missing = Path.Combine(dirs.Root, "gone");
            Ed0Dirs.UseList(a, missing);
            var e = Assert.Throws<ContentException>(() => ContentCatalog.Find());
            StringAssert.Contains(missing, e.Message);
        }

        [Test]
        public void TheListVariableWinsOverTheSingleOne()
        {
            string list = Cartridge("Listed", "l"), single = Cartridge("Single", "s");
            Ed0Dirs.UseList(list);
            System.Environment.SetEnvironmentVariable(FileContentSource.DirectoryVariable, single);
            Assert.That(ContentCatalog.Find().Cartridges, Is.EqualTo(new[] { "Listed" }));
        }

        [Test]
        public void TheSingleVariableAloneIsOneCartridge()
        {
            string single = Cartridge("Single/Data", "s");
            System.Environment.SetEnvironmentVariable(FileContentSource.DirectoryVariable, single);
            ContentCatalog catalog = ContentCatalog.Find();
            Assert.That(catalog.Cartridges, Is.EqualTo(new[] { "Single" }));
            Assert.That(catalog.Load("s").Id, Is.EqualTo("s"));
        }

        [Test]
        public void AMissingSingleDirectoryIsRefused()
        {
            string missing = Path.Combine(dirs.Root, "gone");
            System.Environment.SetEnvironmentVariable(FileContentSource.DirectoryVariable, missing);
            Assert.Throws<ContentException>(() => ContentCatalog.Find());
        }

        [Test]
        public void WithoutVariablesTheProjectIsScanned()
        {
            Assert.That(ContentCatalog.Find().Cartridges, Is.EqualTo(new[] { "Ancient", "Rwr" }));
        }

        [Test]
        public void OutsideAnyProjectTheScanFails()
        {
            string previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(dirs.Dir("outside"));
                var e = Assert.Throws<ContentException>(() => ContentCatalog.Find());
                StringAssert.Contains(ContentCatalog.DirectoriesVariable, e.Message);
            }
            finally { Directory.SetCurrentDirectory(previous); }
        }

        [Test]
        public void FromASubfolderTheProjectIsStillFound()
        {
            string previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Path.Combine(previous, "Assets", "WarCore", "Maps"));
                Assert.That(ContentCatalog.Find().Cartridges, Is.EqualTo(new[] { "Ancient", "Rwr" }));
            }
            finally { Directory.SetCurrentDirectory(previous); }
        }
    }
}
