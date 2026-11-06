using System;
using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;

namespace WarCore.Tests
{
    // The data directory can be named, so a packaged build or a tool started outside the project still finds it.
    public sealed class ContentRootTests
    {
        private string previousDirectory, outside, previousVariable;

        [SetUp]
        public void LeaveTheProject()
        {
            previousDirectory = Directory.GetCurrentDirectory();
            previousVariable = Environment.GetEnvironmentVariable(FileContentSource.DirectoryVariable);
            Environment.SetEnvironmentVariable(FileContentSource.DirectoryVariable, null);
            outside = Path.Combine(Path.GetTempPath(), "warcore-outside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
        }

        [TearDown]
        public void Return()
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Environment.SetEnvironmentVariable(FileContentSource.DirectoryVariable, previousVariable);
            Directory.Delete(outside, true);
        }

        private string ProjectData() => Path.Combine(FindProjectRoot(previousDirectory), FileContentSource.RwrDirectory);

        private static string FindProjectRoot(string from)
        {
            var dir = new DirectoryInfo(from);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, FileContentSource.RwrDirectory))) dir = dir.Parent;
            return dir.FullName;
        }

        [Test]
        public void OutsideTheProject_NothingIsFoundWithoutADirectory_AndTheErrorSaysHow()
        {
            Directory.SetCurrentDirectory(outside);
            ContentException error = Assert.Throws<ContentException>(() => FileContentSource.Rwr());
            Assert.That(error.Message, Does.Contain(FileContentSource.DirectoryVariable));
        }

        [Test]
        public void OutsideTheProject_AnExplicitDirectoryLoadsTheSameData()
        {
            ulong expected = RwrPack.Create().PolicyDigest;
            string data = ProjectData();
            Directory.SetCurrentDirectory(outside);
            Assert.That(ContentLoader.Load(FileContentSource.Rwr(data), "rwr"), Is.Not.Null);
            Assert.That(ContentLoader.Load(FileContentSource.Rwr(data), RwrPack.M5Layer).BuildCombat().PolicyDigest, Is.EqualTo(expected));
        }

        [Test]
        public void OutsideTheProject_TheEnvironmentVariableLoadsTheSameData_AndAnExplicitDirectoryWins()
        {
            ulong expected = RwrPack.Create().PolicyDigest;
            string data = ProjectData();
            Directory.SetCurrentDirectory(outside);
            Environment.SetEnvironmentVariable(FileContentSource.DirectoryVariable, data);
            Assert.That(RwrPack.Create().PolicyDigest, Is.EqualTo(expected));
            string empty = Path.Combine(outside, "empty");
            Directory.CreateDirectory(empty);
            Assert.That(FileContentSource.Rwr(empty).TryRead("rwr", out _, out _), Is.False, "the argument wins over the variable");
        }

        [Test]
        public void ANamedDirectoryThatDoesNotExist_IsAnErrorNamingIt()
        {
            string missing = Path.Combine(outside, "nope");
            ContentException error = Assert.Throws<ContentException>(() => FileContentSource.Rwr(missing));
            Assert.That(error.Message, Does.Contain(missing));
            Environment.SetEnvironmentVariable(FileContentSource.DirectoryVariable, missing);
            Assert.That(Assert.Throws<ContentException>(() => FileContentSource.Rwr()).Message, Does.Contain(missing));
        }
    }
}
