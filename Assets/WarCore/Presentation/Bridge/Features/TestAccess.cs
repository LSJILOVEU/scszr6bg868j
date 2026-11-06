using System.Runtime.CompilerServices;

// The test assembly defines its own world features, so it sees the internal feature contracts.
[assembly: InternalsVisibleTo("WarCore.Sim.Tests")]
