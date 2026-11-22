using System.Runtime.CompilerServices;

// The test assembly defines its own world features, so it sees the internal feature contracts.
[assembly: InternalsVisibleTo("WarCore.Sim.Tests")]
// AIM-2: the Unity view tests draw the legacy house stage (Showcase.LegacyHouse), which stays internal.
[assembly: InternalsVisibleTo("WarCore.Unity.Tests")]
