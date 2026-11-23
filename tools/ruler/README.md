# Ruler: the module map and the debt counters

`ruler.py` reads the game code under `Assets/WarCore` and the content packs without building or running anything
(Python 3, standard library only). It answers two things:

- which module every file belongs to, and which modules each module names;
- how much debt of five kinds the code carries, with every item of that debt written down as a row of a table.

```sh
python3 tools/ruler/ruler.py              # the counters
python3 tools/ruler/ruler.py --map        # every module with its files; what is behind each counter
python3 tools/ruler/ruler.py --check      # exit 1 when the code and the tables disagree
python3 tools/ruler/ruler.py --self-test  # the scanner on a few lines it must not misread
python3 tools/ruler/ruler.py --json F     # everything as JSON (F = - for stdout)
```

## The rules it holds

1. **One file, one module.** `modules.tsv` lists the sixteen modules, each with the one question it answers, and gives
   every game file to exactly one of them. Tests (`Tests/`, `Verification/`, `Unity/Tests/`) check the game and belong
   to no module.
2. **One question, one home.** `one-home.tsv` lists the questions that are answered in more than one place: the home,
   a mark that finds the copies, and every copy there is today (`code-copies`). It also lists the salts that more than
   one per-unit trait is drawn with (`shared-salts`).
3. **Finished means switched on.** `features.tsv` lists the finished features with the content field that switches each
   on, `played.tsv` the stages a person plays with the packs they run on. A feature that no stage has is marked `off`
   (`features-off`).
4. **Nothing idle.** `unused.tsv` lists the files that nothing a person can run reaches (`unused-lines`).
5. **One value, one pack.** A field that two packs of one family write with the same value is a copy; `limits.tsv` holds
   their count (`pack-copies`).

`--check` compares the code with the tables in both directions. Debt that no row names fails: a file without a module,
a new copy, a new unused file, a feature that no stage has. Debt that a row names and the code no longer has fails as
well, until the row is lowered or deleted in the same change. So the tables always say what is left, and nothing that
was paid off can come back unnoticed.

## How it reads the code

No compiler: tokens and brace depth. A file names another file when it uses the name of a type written there. A type
written across several files (a partial type) is named part by part: by a member that part declares, in a file that
names the type too, or that accesses the member when no other type declares one of that name. "Unused" follows those
names from `Assets/WarCore/Unity`, the files with assembly attributes, `tools/replay` and `tools/mapcheck`; code that
only tests reach is unused.

## What it does not see

- Unused code inside a file that is in use: a method nobody calls, a system that is built but that no stage's content
  switches on. The count is of whole files.
- A file whose type has a name that is an ordinary word elsewhere counts as used.
- `features.tsv` and `played.tsv` are kept by hand. A feature missing from the first is not counted; the packs of a
  stage in the second are read off the stage's code by a person, not derived.

The size literals have their own ledger (`tools/size-guard/ledger.tsv`); the ruler only prints its figure.

## Changing the tables

- New file: add it to `modules.tsv` under the module whose question it answers. If it answers a new question, that is a
  new module: decide that first.
- New feature: add a row to `features.tsv` and switch it on in a played stage in the same change.
- A second answer to a listed question: do not add a row; use the home.
- A copy removed, a feature switched on, unused code deleted or connected: lower or delete the row in the same change.
