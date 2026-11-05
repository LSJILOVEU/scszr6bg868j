using System.IO;
using NUnit.Framework;

// Unity runs EditMode tests with the project root as the working directory, and several tests open
// "Assets/...", "Tools/..." relative to it. The .NET test host starts in the output directory instead.
[SetUpFixture]
public sealed class HeadlessSetup
{
    [OneTimeSetUp]
    public void UseProjectRootAsWorkingDirectory()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Assets", "WarCore")))
            directory = directory.Parent;
        if (directory == null)
            throw new DirectoryNotFoundException("Cannot locate the Unity project root above the test binary.");
        Directory.SetCurrentDirectory(directory.FullName);
    }
}
