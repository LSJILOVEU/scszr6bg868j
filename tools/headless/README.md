# Headless Sim tests (no Unity)

Builds `WarCore.Sim`, `WarCore.Presentation` and the EditMode tests (`WarCore.Sim.Tests`) on .NET 8 with the same
assembly split as the asmdefs, and runs them with NUnit. Use it as a fast pre-check from any Linux/macOS shell or
cloud container. It does **not** replace the Unity gates: G23 (IL2CPP/Mono hash equality), G24 (IL2CPP p99) and
zero-GC evidence still run in Unity.

```sh
# once per machine (Ubuntu 24.04: from the distro repo; macOS: brew install dotnet@8)
sudo apt-get install -y dotnet-sdk-8.0

cd tools/headless
dotnet test -c Release                                      # whole suite, ~10 min on 4 cores
dotnet test -c Release --filter "FullyQualifiedName~M0c2S3"  # one slice
```

Notes
- `AllocationMeterTests.cs` is excluded (needs Unity's profiler/GC hooks).
- `HeadlessSetup.cs` sets the working directory to the project root, as Unity does.
- `ShapeReadSymbolTests.cs` is headless only: it binds the sources with Roslyn and checks that the Unity-side
  shape-read lock (`BoxContractTests.ShapeSensitiveReadsAreLockedPerFile`, matched by receiver name) sees every read of
  `Collider.Radius/Height/ShapeKind` and every `With*` rebuild, by symbol.
- Tiered JIT is off: tier-up allocates a few bytes and trips the zero-allocation tests.
- Concurrent GC is off (`ConcurrentGarbageCollection=false`, guarded in `HeadlessSetup.cs`): a background GC retires
  thread allocation contexts and `GC.GetAllocatedBytesForCurrentThread` then jumps by their unused remainder. That
  artifact was both the S4 allocation flake and the old "CoreCLR-only" `TenThousandIdentity*` failures both now run in CI with no exclusion.
- CI runs with `--filter "TestCategory!=StackBound&TestCategory!=Manual"`; `[Explicit]` tests carry `Category("StackBound")` or `Category("Manual")`.
- CoreCLR is not Mono/IL2CPP: a pass here proves logic, not runtime-specific behaviour
  (stack depth, allocation counts, JIT/AOT differences).

## Stack budget

`StackBudgetTests` (Tests/EditMode) bounds every `stackalloc` site in Assets/WarCore to 16 KiB and keeps them out of
loops; it runs with the suite. Its `Category=StackBound` tests are `[Explicit]` because a stack overflow kills the test
host: `dotnet test ... --filter "TestCategory=StackBound"` runs the S2 fixture on a 128 KiB thread and the S3/S4 scripts
on 64 KiB threads (CI does this on Linux as a separate step). Measured on CoreCLR x64 before the fix the S2 fixture tick
used 242,864 B and overflowed 128/256 KiB threads; after it 32,632 B. Mono raises managed thread stacks below ~140 KB
to 143,360 B, so its 64 KiB runs are regression insurance only.
