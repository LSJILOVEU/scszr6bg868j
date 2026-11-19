# Unity compile check

`check.py` compiles the engine-free assemblies and the EditMode test assembly against the API surface
Unity uses, without Unity. It exists because the headless suite (`tools/headless`) builds on net8.0 with
NUnit 3.14, so code such as `Convert.ToHexString` or `Has.Count.EqualTo(3).And.Contain(5L)` passes CI
there and then fails to compile in Unity (CS0117, CS1503). CI runs it as the `unity-compat` check
(`.github/workflows/unity-compat.yml`).

```sh
python3 tools/unity-compat/check.py            # about 15 s after the first NuGet restore
python3 tools/unity-compat/check.py --keep     # keep the generated projects for inspection
python3 tools/unity-compat/check.py --work DIR # generate into DIR (kept)
```

Needs Python 3 (standard library only) and the .NET 8 SDK. Exit 0: everything compiles. Exit 1: compile
errors, printed one per line as `file(line,col): error CSxxxx: ...`. Exit 2: setup problem.

## What it checks

- Assemblies and their files come from the `.asmdef` and `.asmref` files under `Assets/`, with Unity's
  rule that a file belongs to the nearest folder owning one. New files and folders are picked up
  without editing anything here; nothing in `tools/headless` is read or changed.
- Checked: every assembly with `noEngineReferences: true` whose references are all checked too, and
  every test assembly (`optionalUnityReferences: TestAssemblies`) that references only those. Today
  that is Sim, Combat, Battle, Content, Control, Interim, Presentation, Verification and
  WarCore.Sim.Tests (with `Tests/MapContent` through its `.asmref`). Assemblies with
  `defineConstraints` are skipped.
- Each project targets `netstandard2.1` with C# 9. `allowUnsafeCode` follows the asmdef (the
  headless Sim and test projects allow unsafe code; Unity follows the asmdef).
- Test assemblies compile against the net45 build of NUnit 3.5.0, closest to the NUnit bundled with
  the Unity test framework.
- Defined symbols: `UNITY_5_3_OR_NEWER`, `UNITY_INCLUDE_TESTS`, `DEBUG`, `TRACE`. So the
  headless-only `#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER` blocks are left out, as in Unity.

## What it does not check

- Assemblies that use UnityEngine or UnityEditor (`Assets/WarCore/Unity`, `Skin`,
  `Verification/Unity`): no Unity assemblies are available. Changes there still need a Unity run.
- Code inside `#if UNITY_EDITOR` blocks: `UNITY_EDITOR` is not defined because those blocks call
  Unity APIs.
- Runtime differences under Mono, for example exception message text. This is a compile check only.
- Unity's exact API profile. Unity compiles editor assemblies against its own .NET Framework 4.x plus
  .NET Standard 2.1 profile; `netstandard2.1` is a close stand-in, not a copy. An API that exists only
  in .NET Framework 4.x would be reported here although Unity accepts it. Not seen on the current tree.
- Warnings. Only errors fail the check.
