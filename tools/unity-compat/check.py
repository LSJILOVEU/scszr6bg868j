#!/usr/bin/env python3
"""Compile the engine-free assemblies and their EditMode tests the way Unity sees them.

The headless suite (tools/headless) builds on net8.0 with NUnit 3.14. Unity compiles the same sources
against a .NET Standard 2.1 API surface and its bundled NUnit 3.5, so code can pass CI and still fail to
compile in Unity. This script generates a throwaway project set in a temporary directory and runs
`dotnet build` on it:

- assemblies and their source files are derived from the .asmdef / .asmref files under Assets/
  (nearest owner directory wins, as in Unity), never from a hand-written list;
- an assembly is checked when it and everything it references are engine-free (noEngineReferences),
  or when it is a test assembly whose references are all engine-free;
- every project targets netstandard2.1 with LangVersion 9.0; test assemblies reference NUnit 3.5.0;
- UNITY_5_3_OR_NEWER, UNITY_INCLUDE_TESTS, DEBUG and TRACE are defined; UNITY_EDITOR is not, because
  those blocks call UnityEngine/UnityEditor APIs that are not available outside Unity.

Exit code: 0 when everything compiles, 1 on compile errors, 2 on setup errors.
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from xml.sax.saxutils import escape

REPO = Path(__file__).resolve().parents[2]
ASSETS = REPO / "Assets"
TARGET = "netstandard2.1"
LANG = "9.0"
NUNIT = "3.5.0"
DEFINES = "UNITY_5_3_OR_NEWER;UNITY_INCLUDE_TESTS;DEBUG;TRACE"
TEST_MARKERS = ("TestAssemblies",)
ERROR = re.compile(r": error [A-Z]+\d+:")


def read_json(path):
    with open(path, encoding="utf-8-sig") as handle:
        return json.load(handle)


def discover():
    """Return {name: info} for every asmdef, with the .cs files Unity would put in it."""
    owners = {}  # directory -> assembly name
    assemblies = {}
    for path in sorted(ASSETS.rglob("*.asmdef")):
        data = read_json(path)
        name = data["name"]
        assemblies[name] = {
            "dir": path.parent,
            "refs": list(data.get("references", [])),
            "engine_free": bool(data.get("noEngineReferences", False)),
            "unsafe": bool(data.get("allowUnsafeCode", False)),
            "test": any(m in data.get("optionalUnityReferences", []) for m in TEST_MARKERS)
            or "nunit.framework.dll" in data.get("precompiledReferences", []),
            "defines": list(data.get("defineConstraints", [])),
            "files": [],
        }
        owners[path.parent] = name
    for path in sorted(ASSETS.rglob("*.asmref")):
        target = read_json(path).get("reference", "")
        if target.startswith("GUID:"):
            raise SystemExit(f"unity-compat: GUID asmref not supported: {path.relative_to(REPO)}")
        owners[path.parent] = target
    for path in sorted(ASSETS.rglob("*.cs")):
        directory = path.parent
        while directory != ASSETS.parent and directory not in owners:
            directory = directory.parent
        name = owners.get(directory)
        if name in assemblies:
            assemblies[name]["files"].append(path)
    return assemblies


def select(assemblies):
    """Engine-free closure plus test assemblies that only reference it."""
    free = set()
    changed = True
    while changed:
        changed = False
        for name, info in assemblies.items():
            if name in free or not info["engine_free"] or info["defines"]:
                continue
            if all(ref in free for ref in info["refs"]):
                free.add(name)
                changed = True
    tests = {
        name for name, info in assemblies.items()
        if info["test"] and not info["defines"] and info["refs"] and all(ref in free for ref in info["refs"])
    }
    return sorted(free), sorted(tests)


def project(name, info, test):
    lines = [
        '<Project Sdk="Microsoft.NET.Sdk">',
        "  <PropertyGroup>",
        f"    <TargetFramework>{TARGET}</TargetFramework>",
        f"    <LangVersion>{LANG}</LangVersion>",
        f"    <AssemblyName>{escape(name)}</AssemblyName>",
        f"    <AllowUnsafeBlocks>{'true' if info['unsafe'] else 'false'}</AllowUnsafeBlocks>",
        "    <Nullable>disable</Nullable>",
        "    <ImplicitUsings>disable</ImplicitUsings>",
        "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
        "    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>",
        "    <GenerateDocumentationFile>false</GenerateDocumentationFile>",
        "    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>",
        "    <NoWarn>$(NoWarn);CS0618;CS0162;CS0219;CS0168;NU1701;NU1603;NU1903;NU1902</NoWarn>",
        f"    <DefineConstants>{DEFINES}</DefineConstants>",
        "  </PropertyGroup>",
        "  <ItemGroup>",
    ]
    for path in info["files"]:
        lines.append(f'    <Compile Include="{escape(str(path))}" />')
    for ref in info["refs"]:
        lines.append(f'    <ProjectReference Include="../{escape(ref)}/{escape(ref)}.csproj" />')
    if test:
        # Unity bundles a full-framework build of NUnit 3.5; the package's netstandard1.6 build lacks
        # attributes such as [Timeout], so compile against its net45 assembly instead.
        lines.append(f'    <PackageReference Include="NUnit" Version="{NUNIT}" ExcludeAssets="all" GeneratePathProperty="true" />')
        lines.append('    <Reference Include="nunit.framework">')
        lines.append("      <HintPath>$(PkgNUnit)/lib/net45/nunit.framework.dll</HintPath>")
        lines.append("    </Reference>")
    lines += ["  </ItemGroup>", "</Project>", ""]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--keep", action="store_true", help="keep the generated projects and print where")
    parser.add_argument("--work", help="use this directory instead of a temporary one (implies --keep)")
    args = parser.parse_args()

    if shutil.which("dotnet") is None:
        print("unity-compat: dotnet not found (needs the .NET 8 SDK)", file=sys.stderr)
        return 2

    assemblies = discover()
    free, tests = select(assemblies)
    if not tests:
        print("unity-compat: no test assembly selected; check the asmdef layout", file=sys.stderr)
        return 2

    work = Path(args.work).resolve() if args.work else Path(tempfile.mkdtemp(prefix="unity-compat-"))
    keep = args.keep or bool(args.work)
    work.mkdir(parents=True, exist_ok=True)
    started = time.monotonic()
    try:
        names = free + tests
        for name in names:
            folder = work / name
            folder.mkdir(exist_ok=True)
            (folder / f"{name}.csproj").write_text(project(name, assemblies[name], name in tests), encoding="utf-8")
        for name in names:
            info = assemblies[name]
            kind = "test" if name in tests else "lib"
            print(f"unity-compat: {name} ({kind}, {len(info['files'])} files)")
        env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
        output, failed = "", 0
        referenced = {ref for name in names for ref in assemblies[name]["refs"]}
        for name in [n for n in names if n not in referenced]:
            command = ["dotnet", "build", f"{name}/{name}.csproj", "-nologo", "-clp:NoSummary;ErrorsOnly", "-v:q"]
            result = subprocess.run(command, cwd=work, env=env, capture_output=True, text=True)
            output += (result.stdout + result.stderr).replace(str(REPO) + os.sep, "")
            failed = failed or result.returncode
        errors = sorted({re.sub(r"\s*\[[^\]]*\.csproj\]$", "", line.strip()) for line in output.splitlines() if ERROR.search(line)})
        elapsed = time.monotonic() - started
        if failed:
            if errors:
                print("\n".join(errors))
                print(f"unity-compat: FAIL, {len(errors)} compile error(s) in {elapsed:.0f} s")
            else:
                print(output.strip())
                print(f"unity-compat: FAIL, dotnet build exited {failed} in {elapsed:.0f} s")
            return 1
        print(f"unity-compat: OK, {len(names)} assemblies compile against {TARGET} + NUnit {NUNIT} in {elapsed:.0f} s")
        return 0
    finally:
        if keep:
            print(f"unity-compat: projects kept in {work}")
        else:
            shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
