# Mutation tools

`mutate.py` runs a list of source mutations against the headless test project and decides, per mutation,
whether the named tests catch it. `hygiene.py` rejects weak assertion forms in added test lines.
Both are Python 3, standard library only, Linux and macOS (not Windows).

```sh
python3 tools/mutation/mutate.py tools/mutation/lists/e1b-sample.txt --out /tmp/e1b.md
python3 tools/mutation/hygiene.py origin/main HEAD
python3 tools/mutation/selftest/selftest.py          # acceptance self-test, about 20 minutes
```

## mutate.py

```
mutate.py LIST [--repo DIR] [--out FILE] [--only ID[,ID...]] [--timeout SECONDS] [--log FILE]
```

- `--repo`: the repository (default: current directory). It must have no uncommitted changes.
- `--out`, `--log`: must be outside the repository (else exit 2, `bad-args`).
- `--out`: also write the table plus, per mutation, every failed test with its `label`/`site` and the
  mutated assembly's sha256 before and after (64 hex digits).
- `--only`: run only these ids of the list.
- `--timeout`: seconds per mutation for its build and test, default 900, minimum 600.
- `--log`: append progress lines to a file (stderr is kept for errors).

### Isolation and order

1. `git status --porcelain` of the repository not empty: exit 2, `dirty-repo`.
2. Lock: `fcntl.flock(LOCK_EX | LOCK_NB)` on `<git-common-dir>/warcore-mutation/lock`; held by another run:
   exit 2, `locked`. The kernel drops the lock when the process dies (also on `kill -9`).
3. Record the worktree path set W0 (`git worktree list --porcelain`), then clean up what dead runs left
   (see below) and check the set equals W0 minus their copies (W1), else exit 2, `worktree-leak`.
4. Write this run's marker `<git-common-dir>/warcore-mutation/<pid>.marker` (repository, copy path, pid, start
   time) and `fsync` it; only then `git worktree add --detach <copy> HEAD`, where the copy is
   `<system temp>/warcore-mutation-XXXXXXXX/wt` (`$TMPDIR` or `/tmp`). The source repository is never written.
5. Every child that writes the copy (`git worktree add`, `dotnet build`, `dotnet test`) starts through a gate:
   a new session (own process group) that blocks reading a pipe. The runner appends
   `{pgid, leader pid, leader start time}` (`ps -o lstart=`, raw text) to the marker, `fsync`s, then writes one
   byte; only then the gate `exec`s the command. If the runner dies first the gate reads EOF and exits without
   running anything. On timeout or SIGINT/SIGTERM the runner `killpg`s the whole group.
6. Every exit path (success and error) runs the same finish: all child groups gone, `git worktree remove --force`
   of the copy (never a global `git worktree prune`), marker removed, worktree set equal to W1
   (else exit 2, `worktree-leak`).
7. Paths are compared after `os.path.realpath` (the macOS temp directory is behind a symlink).

### Cleanup of dead runs

After taking the lock every marker belongs to a dead run. For each marker:

- Processes: for every recorded group, the live (non-zombie) members from `ps -A -o pid=,pgid=,stat=,lstart=`.
  No members: skip. The group is recognised when its leader is alive with the recorded start time, or the
  leader is gone and every member started no earlier than the recorded leader start time; then `killpg(SIGKILL)`
  and wait until the group is empty (60 s). Not recognised or not empty in time: exit 2, `stale-process`,
  nothing else touched.
- Copy: only the copy path of that marker. Registered and present: `git worktree remove --force`. A
  registration whose directory is gone: only the `<git-common-dir>/worktrees/<name>` directory whose `gitdir`
  file points at `<copy>/.git` is deleted. A remaining directory is deleted. Then the marker is deleted.
- Worktrees not named by a marker are never touched.

### Build and baseline

- Build: `dotnet build tools/headless/WarCore.Headless.Tests.csproj -c Release --disable-build-servers`
  (no MSBuild node or compiler server stays behind). Builds are reproducible: SDK projects are `Deterministic`
  and the copy path stays the same for the whole run (checked: two `--no-incremental` builds of the same source
  give the same sha256 for every assembly).
- Test: `dotnet test ... -c Release --no-build --filter <filter> --results-directory <new empty dir> --logger trx
  -- NUnit.TestOutputXml=<that dir>/nunit`. The exit code is not used.
- Result files: the directory must hold exactly one `.trx` and one NUnit XML; the trx `ResultSummary` outcome
  must not be `Aborted` or `Error` and must have `Counters`; the trx results and the XML must name the same tests
  (trx `className` + `.` + test name = NUnit `fullname`). Otherwise the results are missing.
- Baseline: build once without mutation and record, for every assembly, the sha256 of its new build output and
  of the copy in the test output directory. Then run each filter of the list once: missing results exit 2
  `baseline-result`; 0 executed (trx `Counters/@executed`) exit 2 `zero-executed`; any failure exit 2
  `baseline-red`. No mutation starts before all filters pass.

| source file | assembly | new build output | loaded copy |
|---|---|---|---|
| `Assets/WarCore/<X>/**`, X in Sim, Combat, Content, Control, Interim, Presentation, Battle | `WarCore.<X>` | `tools/headless/<X>/bin/Release/net8.0/WarCore.<X>.dll` | `tools/headless/bin/Release/net8.0/WarCore.<X>.dll` |
| `Assets/WarCore/Tests/EditMode/**`, `Assets/WarCore/Tests/MapContent/**`, `tools/headless/*.cs` | `WarCore.Sim.Tests` | `tools/headless/obj/Release/net8.0/WarCore.Sim.Tests.dll` | `tools/headless/bin/Release/net8.0/WarCore.Sim.Tests.dll` |

Any other file (for example `Unity/**`) has no assembly. A file the project does not compile
(`Tests/EditMode/AllocationMeterTests.cs`) maps to an assembly whose bytes do not change.

### One mutation

1. The file must be a regular file reached without symbolic links inside the copy (its real path equals
   `<copy>/<file>`), and the original text must occur exactly once in it, byte for byte (else invalid). The
   list is read with `\n` line ends; a multi-line original does not match a source file with CRLF line ends. Each expected name must match at least
   one test of the filter's baseline (else invalid).
2. Replace, build. Build failed: invalid (compile failed). No assembly, or its new output has the baseline
   sha256: invalid (build not effective). The loaded copy differs from the new output: invalid (build not
   effective: old assembly loaded).
3. `dotnet test --no-build` with the entry's filter, then judge (below).
4. Restore, always: write the original bytes back and check the file sha256 (else exit 2, `restore-mismatch`);
   build; every assembly's new output and loaded copy must be back to the baseline sha256 (else exit 2,
   `baseline-mismatch`); run the filter again: all green with the baseline executed count (else exit 2,
   `baseline-red`).

### Verdicts

- An **assertion failure** is read from the NUnit XML only: the test's `test-case` has `result="Failed"`, no
  `label` attribute, and no `site` or `site="Test"`. An exception in the test (`label="Error"`), a SetUp or
  TearDown failure (`label="Error"`; NUnit 3.14 writes no `site` on the test case for these), a OneTimeSetUp
  failure (`site="Parent"`), `label="Cancelled"` or `"Invalid"` are not. Messages are never read.
- An expected name matches a test when it is a substring (case-sensitive) of the test's full name, for example
  `WarCore.Tests.E1bRecipeTests.SoldiersMapFieldByField` matches both `SoldiersMapFieldByField(True)` and
  `(False)`, and `WarCore.Tests.E1bLockTests.` matches every test of that class.
- **killed**: executed count equal to the baseline and at least one test matching an expected name failed on an
  assertion.
- **survived**: every executed test has `result="Passed"` in the NUnit XML. Any other result (Failed,
  Inconclusive, Skipped, a missing attribute) counts as a failure, and only `Failed` with the attribute rule above
  is an assertion failure. The same "all Passed" rule makes a baseline or restore run green.
- **invalid**: compile failed, build not effective, old assembly loaded, results missing (including a crashed
  test host), executed count changed, timeout, expected tests failed but none on an assertion, only tests outside
  the expected list failed (they are listed), original text not found exactly once, an expected name matches no
  baseline test.

### Output

Markdown on stdout: the repository HEAD (full SHA), the baseline executed count of each filter, then one row per
mutation: id, verdict, failed tests (first 3), executed, seconds. `--out` adds the details.

### Exit codes

- 0: every mutation killed.
- 1: at least one survived or invalid.
- 2: stopped with an error; the first line of stderr is the error id: `dirty-repo`, `locked`, `stale-process`,
  `worktree-leak`, `zero-executed`, `baseline-result`, `baseline-red`, `baseline-build`, `restore-mismatch`,
  `baseline-mismatch`, `bad-list`, `bad-args`, `not-a-repo`, `git`, `interrupted`, `internal` (an unexpected
  Python exception; the cleanup still runs). A failing `ps`, an unreadable start time, or a group that does not
  exit within 60 s after `killpg` stops the run with `stale-process`. Unreadable result files (bad XML, missing
  `Counters`, `executed` or `TestMethod`) make that run's results missing, never a traceback.

## List format

Plain UTF-8 text in `tools/mutation/lists/` (extension `.txt`). F1b reads this format; change it only with a
version note here.

```
# comment
[id]
file = Assets/WarCore/Presentation/Bridge/MapRecipe.cs
filter = FullyQualifiedName~E1b|FullyQualifiedName~E1a
expect = WarCore.Tests.E1bRecipeTests.SoldiersMapFieldByField
expect = WarCore.Tests.E1bRecipeTests.M6DescriptionRunsLikeTheDemoRecipe
note = one line saying what the mutation breaks
original:
|         private const long SpeedMmPerSec = 6000;
replacement:
|         private const long SpeedMmPerSec = 6001;
```

- `[id]` starts an entry; ids are unique, `[A-Za-z0-9][A-Za-z0-9._-]*`.
- `file` (repository-relative, normalized), `filter` (passed to `dotnet test --filter` as is), `note`: once each.
- `expect`: one or more lines, matched as described under Verdicts.
- `original:` and `replacement:` are followed by block lines. A block line is `| ` plus the text, or a bare `|`
  for an empty line; the block ends at the first other line. Lines are joined with `\n` and there is no final
  newline, so a one-line block matches inside a line. `original` must not be empty; `replacement` may be empty
  (deletes the text).
- Blank lines and lines starting with `#` between entries are ignored.

## hygiene.py

```
hygiene.py BASE HEAD [--repo DIR]
```

Reads `git diff -U0 --no-renames BASE..HEAD` for `Assets/WarCore/Tests/**` and `tools/headless/*.cs` and checks
only added (`+`) lines; deleted lines do not offset them. Findings, printed as `<file>:<line in the new file>:
<rule>`:

- `or-not-equal`: `.Or.Not.EqualTo`
- `count-at-least-zero`: `GreaterThanOrEqualTo(0)` (any operand; write the exact expected value instead)
- `assert-pass`: `Assert.Pass(`

Exit 0 clean, 1 findings, 2 git error.

Limits: the check is textual and per line. It does not see other weak forms (`Assert.That(true)`, a bare
`.Or.`), a form split over lines or with extra spaces inside `.Or.Not.EqualTo`, and it also reports matches in
comments and strings and any `GreaterThanOrEqualTo(0)`, count or not. File names with characters git quotes are
not decoded.

## Self-test

`selftest/selftest.py [--repo DIR] [--only CASES] [--keep]` clones the repository into a temp directory and runs:
the seven verdicts of `lists/selftest.txt` (1 must kill, 2 comment only, 3 compile failure, 4 file excluded from
the project, 5 old assembly loaded, 6 exception with an assertion-like message, 7 TearDown exception), the
must-kill entry alone (exit 0), restore faults (`restore-mismatch`, `baseline-mismatch`), zero executed, a crashed
test host, only-unexpected failures, a changed executed count and an assertion in OneTimeSetUp
(`lists/selftest-extra.txt`), dirty repository, lock, `kill -9` during a hanging
mutant test (`lists/selftest-hang.txt`), `kill -9` before a child is registered and after `git worktree add` is released (with two unrelated
worktrees, one with its directory deleted, that must survive), refused paths (a symlinked list file, `--out`
and `--log` inside the repository, a non-UTF-8 list), and the hygiene cases. An unknown `--only` case name exits 2;
a run with no check fails.

Self-test switches (only for the self-test): environment variable `WARCORE_MUTATION_SELFTEST`, comma-separated:

- `stale-loaded@<id>`: after the mutant build, put the baseline bytes back into the loaded copy.
- `restore-short@<id>`: the restore writes the original minus its last byte.
- `restore-stale@<id>`: after the restore build, put the mutant bytes into the loaded copy.
- `pause-before-register=<stage>`: stop after starting the gate of `<stage>` and before recording its group.
- `pause-after-release=<stage>`: stop right after releasing the gate of `<stage>`.

Stages: `worktree-add`, `baseline-build`, `baseline-test`, `mutant-build`, `mutant-test`, `restore-build`,
`restore-test`. A paused run prints `PAUSED <point> <stage> <gate pid>` to stderr and sleeps until killed.
The switches are not passed to child processes.

## CI

`.github/workflows/mutation.yml` (workflow and final check both named `mutation`) runs on every pull request into
`main` and by hand (`workflow_dispatch`, base: merge base with `main`).

1. `plan`: base = first parent of the pull request merge commit. `ci/plan.py --base BASE --head HEAD` takes the files
   directly in `lists/` named `*.txt` that the change adds, modifies or renames (new path); deleted lists, `selftest/`
   and other files are ignored. Each list is cut, in entry order, into `ceil(n/10)` shards whose sizes differ by at
   most one; lists are never mixed. At most 24 shards per change. Errors exit 2 with the id on the first stderr line:
   `bad-name`, `bad-id` (list or id outside `[A-Za-z0-9._-]`), `empty-list`, `duplicate-id`, `too-many-shards`,
   `git`, `bad-args`. A list renamed with identical content is not re-run.
2. `hygiene`: `hygiene.py BASE HEAD` on the whole change.
3. `shard (<list> i/k)`: one Ubuntu job per shard, at most 6 at a time: `mutate.py <list> --only <ids> --timeout 600`.
   The table and log are printed and added to the job summary (logs of a public repository are public).
   The job limit of 180 minutes is the real bound (each mutation may take up to 3 x 600 s: mutant build and test,
   restore build, restore test); a shard over it is cancelled and counts as failed. Measured: about 1 minute of
   checkout, setup and baseline per shard, then 15 to 20 s per mutation with a narrow filter.
4. `mutation`: fails unless planning and hygiene passed and every shard passed (or none was planned).

Repository variable `MUTATION_CI` (Settings, Variables; not a secret, a pull request cannot change it): `off` skips
the shards with a notice, planning, hygiene and the final check still run. Then post local `mutate.py` results in
the pull request as before.

`mutate.py`, `hygiene.py` and `plan.py` come from the base commit (checked out as `base/`); the pull request
(`pr/`) only supplies code, tests and lists. A change to these tools is judged by the old copy until it is merged,
and `tools/automerge/decide.py` leaves pull requests touching them (`mutate.py`, `hygiene.py`, `ci/`) for a human.
When the base has no `plan.py` (only the pull request adding it) the pull request copy is used, with a notice.
List names and ids reach the shell only through environment variables.

Not covered: lists the change did not touch are not re-run (a code change that breaks an old list is not seen
here); a pull request's own build steps run in the same job and could tamper with the job; the check is not one of
the required checks of `tools/automerge/decide.py`, but a red or running `mutation` still blocks the automatic merge
(any other check must be success or skipped). On a private repository every shard counts against the Actions
minutes of the account.

Offline tests: `python3 -I tools/mutation/ci/test_plan.py`; their own mutation check:
`python3 -I tools/mutation/ci/mutate.py` (exit 1 if a mutant survives).
