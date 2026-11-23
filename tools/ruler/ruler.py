#!/usr/bin/env python3
"""Module map and debt counters for the game code under Assets/WarCore (see README.md).

  python3 tools/ruler/ruler.py              the counters
  python3 tools/ruler/ruler.py --map        every module with its files and the modules it names; what is behind the counters
  python3 tools/ruler/ruler.py --check      exit 1 when the code and the tables beside this file disagree
  python3 tools/ruler/ruler.py --self-test  the scanner on a few lines it must not misread; the same output twice
  python3 tools/ruler/ruler.py --json F     everything as JSON (F = - for stdout)

Standard library only. The tables: modules.tsv, one-home.tsv, features.tsv, played.tsv, unused.tsv, limits.tsv.
"""
import argparse
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
SRC = os.path.join(ROOT, "Assets", "WarCore")
# Code that only checks the game: not part of any module, counted as tests.
TEST_DIRS = ("Tests/", "Verification/", "Unity/Tests/")
# Code outside Assets/WarCore that runs the game: its references keep game code in use.
OUTSIDE_ROOTS = ("tools/replay", "tools/mapcheck")
SIZE_LEDGER = os.path.join(ROOT, "tools", "size-guard", "ledger.tsv")


def rows(name):
    """Tab-separated rows of a table beside this file, without comments and blank lines."""
    out = []
    with open(os.path.join(HERE, name), encoding="utf-8") as handle:
        for number, line in enumerate(handle, 1):
            line = line.rstrip("\n")
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            out.append((number, line.split("\t")))
    return out


def fail_table(name, number, why):
    sys.exit("%s:%d: %s" % (name, number, why))


def whole(name, number, text):
    if not text.isdigit():
        fail_table(name, number, "'%s' is not a whole number" % text)
    return int(text)


# ---------------------------------------------------------------------------------------------------------------------
# Source files

def walk_cs(top):
    for folder, _, names in os.walk(top):
        for name in names:
            if name.endswith(".cs"):
                yield os.path.join(folder, name)


def source_files():
    """(game, tests): paths relative to Assets/WarCore, '/' separated, sorted."""
    game, tests = [], []
    for path in walk_cs(SRC):
        rel = os.path.relpath(path, SRC).replace(os.sep, "/")
        (tests if rel.startswith(TEST_DIRS) else game).append(rel)
    return sorted(game), sorted(tests)


def read(path):
    """A file's text; a path that is not absolute is taken from Assets/WarCore."""
    with open(os.path.join(SRC, path), encoding="utf-8-sig", errors="replace") as handle:
        return handle.read()


def line_count(text):
    return text.count("\n") + (0 if text.endswith("\n") or not text else 1)


def strip_code(text):
    """C# source with comments removed and the insides of string and char literals blanked."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        two = text[i:i + 2]
        if two == "//":
            j = text.find("\n", i)
            i = n if j < 0 else j
        elif two == "/*":
            j = text.find("*/", i + 2)
            out.append("\n" * text.count("\n", i, n if j < 0 else j))
            i = n if j < 0 else j + 2
        elif c == '"' or (c in "@$" and text[i + 1:i + 2] == '"') or (two in ('$@', '@$') and text[i + 2:i + 3] == '"'):
            verbatim = "@" in text[i:i + 2] and c != '"'
            i = text.index('"', i) + 1
            out.append('"')
            while i < n:
                if verbatim:
                    if text[i] == '"' and text[i + 1:i + 2] == '"':
                        i += 2
                        continue
                    if text[i] == '"':
                        break
                else:
                    if text[i] == "\\":
                        i += 2
                        continue
                    if text[i] == '"' or text[i] == "\n":
                        break
                if text[i] == "\n":
                    out.append("\n")
                i += 1
            out.append('"')
            i += 1
        elif c == "'":
            j = i + 1
            while j < n and text[j] != "'" and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            out.append("' '")
            i = j + 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


TOKEN = re.compile(r"[A-Za-z_]\w*|=>|\S")
WORD = re.compile(r"[A-Za-z_]\w*")
CALLED = re.compile(r"\.\s*([A-Za-z_]\w*)\s*[(<]")
ACCESSED = re.compile(r"\.\s*([A-Za-z_]\w*)")
EXTENSION = re.compile(r"\bstatic\s+[\w<>\[\]\?,\.\s]+?\s+(\w+)\s*(?:<[^>()]*>)?\s*\(\s*this\s")
DIRECTIVE = re.compile(r"^[ \t]*(?:using\s+(?:static\s+)?[\w.]+\s*;|namespace\s+[\w.]+)", re.M)
TYPE_WORDS = ("class", "struct", "interface", "enum")
KEYWORDS = frozenset("""abstract as base bool break byte case catch char checked class const continue decimal default delegate
do double else enum event explicit extern false finally fixed float for foreach get goto if implicit in init int interface
internal is lock long namespace new null object operator out override params partial private protected public readonly ref
return sbyte sealed set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked
unmanaged unsafe ushort using var virtual void volatile where while""".split())
# Members every type may have: naming one of them says nothing about which type is meant.
BASE_MEMBERS = frozenset("Equals GetHashCode ToString CompareTo Dispose GetEnumerator MoveNext Current Reset".split())


def after_angles(tokens, i):
    """Index after the <...> opening at i, or -1 when it does not hold a list of types."""
    depth = 0
    for j in range(i, min(len(tokens), i + 80)):
        token = tokens[j]
        if token == "<":
            depth += 1
        elif token == ">":
            depth -= 1
            if depth == 0:
                return j + 1
        elif token not in (",", ".", "[", "]", "?", "(", ")") and not WORD.fullmatch(token):
            return -1
    return -1


def declarations(code):
    """What a file declares, read from tokens and brace depth (no grammar).

    types:  top-level type name -> [names of its members and of the members of its nested types, names of its nested types]
    shared: names of interface members (an implementation is reached through the interface, not by its own name)
    spaces: the namespaces
    """
    tokens = TOKEN.findall(code)
    types, shared, spaces = {}, set(), set()
    stack = []          # one entry per open brace: (what the brace belongs to, the top-level type around it)
    pending = None      # a type or namespace whose brace has not come yet
    rounds = squares = 0
    i, n = 0, len(tokens)
    while i < n:
        token = tokens[i]
        nxt = tokens[i + 1] if i + 1 < n else ""
        kind, top = stack[-1] if stack else ("space", None)
        if token == "{":
            stack.append(pending or ("code", top))
            pending, rounds, squares = None, 0, 0
        elif token == "}":
            if stack:
                stack.pop()
            rounds = squares = 0
        elif pending:
            if token == ";":
                pending = None
        elif token == "(":
            rounds += 1
        elif token == ")":
            rounds -= 1
        elif token == "[":
            squares += 1
        elif token == "]":
            squares -= 1
        elif token == "namespace":
            j = i + 1
            while j < n and (tokens[j] == "." or WORD.fullmatch(tokens[j])):
                j += 1
            spaces.add("".join(tokens[i + 1:j]))
            pending = ("space", None)
            i = j
            continue
        elif token in TYPE_WORDS and kind != "code" and WORD.fullmatch(nxt) and nxt not in KEYWORDS:
            if top is None:
                types.setdefault(nxt, [set(), set()])
            else:
                types[top][1].add(nxt)
            pending = (token, top or nxt)
            i += 2
            continue
        elif kind in TYPE_WORDS and rounds == 0 and squares == 0:
            if token == "operator":
                while i + 1 < n and tokens[i + 1] != "(":
                    i += 1
            elif token in ("=", "=>"):
                # An initializer or an expression body: nothing in it is a declaration.
                depth, j = 0, i + 1
                while j < n:
                    inner = tokens[j]
                    if inner in ("(", "[", "{"):
                        depth += 1
                    elif inner in (")", "]", "}"):
                        if depth == 0:
                            break
                        depth -= 1
                    elif depth == 0 and (inner == ";" or (inner == "," and token == "=")):
                        break
                    j += 1
                i = j if j < n and tokens[j] == "}" else j + 1
                continue
            elif WORD.fullmatch(token) and token not in KEYWORDS:
                declared = nxt in ("(", ";", "=", "=>", "{", ",") or (kind == "enum" and nxt == "}")
                if nxt == "<":
                    j = after_angles(tokens, i + 1)
                    declared = j > 0 and j < n and tokens[j] == "("
                if declared:
                    types[top][0].add(token)
                    if kind == "interface":
                        shared.add(token)
        i += 1
    return types, shared, spaces


class Source:
    def __init__(self, rel, text):
        self.rel = rel
        self.lines = line_count(text)
        self.code = strip_code(text)
        self.types, self.shared, self.spaces = declarations(self.code)
        self.extensions = set(EXTENSION.findall(self.code))
        self.assembly_root = "[assembly:" in self.code


# ---------------------------------------------------------------------------------------------------------------------
# Modules: every game file belongs to exactly one

def glob_regex(pattern):
    parts = []
    for piece in re.split(r"(\*\*|\*)", pattern):
        parts.append(".*" if piece == "**" else "[^/]*" if piece == "*" else re.escape(piece))
    return re.compile("^" + "".join(parts) + "$")


def load_modules():
    modules, exact, globs = {}, {}, []
    for number, cells in rows("modules.tsv"):
        if cells[0] == "M" and len(cells) == 6:
            modules[cells[2]] = {"number": cells[1], "key": cells[2], "block": cells[3], "name": cells[4], "answers": cells[5]}
        elif cells[0] == "F" and len(cells) == 3:
            if cells[1] not in modules:
                fail_table("modules.tsv", number, "unknown module '%s'" % cells[1])
            if "*" in cells[2]:
                globs.append((cells[2], glob_regex(cells[2]), cells[1], number))
            elif exact.setdefault(cells[2], cells[1]) != cells[1]:
                fail_table("modules.tsv", number, "'%s' is given to two modules" % cells[2])
        else:
            fail_table("modules.tsv", number, "expected an M row (6 cells) or an F row (3 cells)")
    return modules, exact, globs


def assign(game, exact, globs):
    """file -> module key, the files no rule names, and the rules that name nothing."""
    owner, unhomed, used = {}, [], set()
    for rel in game:
        if rel in exact:
            owner[rel] = exact[rel]
            used.add(rel)
            continue
        hits = [(len(pattern), module, pattern) for pattern, regex, module, _ in globs if regex.match(rel)]
        if not hits:
            unhomed.append(rel)
            continue
        longest = max(length for length, _, _ in hits)
        winners = {module for length, module, _ in hits if length == longest}
        if len(winners) > 1:
            sys.exit("modules.tsv: '%s' matches rules of %s with equally long patterns" % (rel, sorted(winners)))
        owner[rel] = winners.pop()
        used.update(pattern for length, _, pattern in hits if length == longest)
    stale = sorted(set(exact) - used) + sorted(pattern for pattern, _, _, _ in globs if pattern not in used)
    return owner, unhomed, stale


# ---------------------------------------------------------------------------------------------------------------------
# References between files, and what nothing playable reaches

def reference_graph(sources):
    """file -> the files it names.

    A type written in one file is named by its name. A type written across several files (a partial type, or one name in
    two namespaces) is named part by part: by a member that part declares, in a file that names the type too, or that
    accesses the member when no other type declares one of that name. A chance word is then not a door between modules.
    """
    parts, extended, shared, spaces = {}, {}, set(BASE_MEMBERS), set()
    for source in sources.values():
        shared |= source.shared
        spaces |= source.spaces
        for name, (members, nested) in source.types.items():
            parts.setdefault(name, {})[source.rel] = members | nested
        for name in source.extensions:
            extended.setdefault(name, set()).add(source.rel)
    single = {name: next(iter(files)) for name, files in parts.items() if len(files) == 1}
    several = {name: {rel: names - shared - parts.keys() for rel, names in files.items()}
               for name, files in parts.items() if len(files) > 1}
    declarer = {}       # member name -> the one type that declares it (None when several do)
    for name, files in parts.items():
        for names in files.values():
            for member in names:
                declarer[member] = name if declarer.get(member, name) == name else None
    # The longest namespace first; with no namespace at all the pattern matches nothing.
    qualified = re.compile(r"\b(?:%s)\." % "|".join([re.escape(space) for space in sorted(spaces, key=len, reverse=True)] or ["(?!)"]))
    edges = {}
    for source in sources.values():
        code = qualified.sub("", DIRECTIVE.sub("", source.code))
        words = set(WORD.findall(code))
        accessed = set(ACCESSED.findall(code))
        targets = {single[name] for name in words & single.keys()}
        for name in set(CALLED.findall(code)) & extended.keys():
            targets |= extended[name]
        for name, files in several.items():
            named = name in words
            for rel, names in files.items():
                hit = names & words
                if (named and (hit or not names)) or any(declarer.get(member) == name for member in hit & accessed):
                    targets.add(rel)
        targets.discard(source.rel)
        edges[source.rel] = targets
    return edges


def reachable(edges, starts):
    seen, queue = set(starts), list(starts)
    while queue:
        for target in edges.get(queue.pop(), ()):
            if target not in seen:
                seen.add(target)
                queue.append(target)
    return seen


# ---------------------------------------------------------------------------------------------------------------------
# One question, one home

SALT = re.compile(r"\b(\w*Salt\w*)\s*=\s*(0x[0-9a-fA-F]+)UL")


def load_one_home():
    questions, salts = {}, {}
    for number, cells in rows("one-home.tsv"):
        if cells[0] == "Q" and len(cells) == 6:
            questions[cells[1]] = {"id": cells[1], "name": cells[2], "home": cells[3], "regex": re.compile(cells[4]),
                                   "hint": cells[5], "rows": {}}
        elif cells[0] == "C" and len(cells) >= 5 and cells[1] in questions and cells[4] in ("debt", "other"):
            questions[cells[1]]["rows"][cells[2]] = (whole("one-home.tsv", number, cells[3]), cells[4])
        elif cells[0] == "S" and len(cells) == 3:
            salts[cells[1].lower()] = whole("one-home.tsv", number, cells[2])
        else:
            fail_table("one-home.tsv", number, "expected Q (6 cells), C (question, file, count, debt|other) or S (salt, count)")
    return list(questions.values()), salts


def scan_one_home(sources, game):
    questions, salt_rows = load_one_home()
    problems, report = [], []
    for question in questions:
        found = {rel: len(question["regex"].findall(sources[rel].code)) for rel in game}
        for rel in game:
            allowed = question["rows"].get(rel, (0, ""))[0]
            if rel == question["home"] or found[rel] == allowed:
                continue
            if found[rel] > allowed:
                problems.append("%s: %s answers '%s' again (%d, its row allows %d). The home is %s. %s"
                                % (question["id"], rel, question["name"], found[rel], allowed, question["home"], question["hint"]))
            else:
                problems.append("%s: %s now has %d (its row says %d): lower or delete the row in tools/ruler/one-home.tsv"
                                % (question["id"], rel, found[rel], allowed))
        for rel in question["rows"]:
            if rel not in sources:
                problems.append("%s: %s is gone: delete its row in tools/ruler/one-home.tsv" % (question["id"], rel))
        if question["home"] not in sources:
            problems.append("%s: its home %s does not exist" % (question["id"], question["home"]))
        debt = {rel: count for rel, (count, tag) in sorted(question["rows"].items()) if tag == "debt"}
        report.append({"id": question["id"], "name": question["name"], "home": question["home"],
                       "copies": sum(debt.values()), "where": debt})
    # A unit's trait is drawn from Mix(its key ^ a salt): two traits on one salt are one draw, not two.
    drawn = {}
    for rel in game:
        for name, value in SALT.findall(sources[rel].code):
            drawn.setdefault(value.lower(), []).append("%s (%s)" % (name, rel))
    salts = [{"salt": value, "traits": traits} for value, traits in sorted(drawn.items()) if len(traits) > 1]
    for entry in salts:
        allowed = salt_rows.pop(entry["salt"], 1)
        if len(entry["traits"]) > allowed:
            problems.append("salt %s is drawn by %s: give every trait a salt of its own" % (entry["salt"], ", ".join(entry["traits"])))
        elif len(entry["traits"]) < allowed:
            problems.append("salt %s is drawn by %d traits now (its row says %d): lower the row in tools/ruler/one-home.tsv"
                            % (entry["salt"], len(entry["traits"]), allowed))
    for value in salt_rows:
        problems.append("salt %s is no longer shared: delete its row in tools/ruler/one-home.tsv" % value)
    return report, salts, problems


# ---------------------------------------------------------------------------------------------------------------------
# Content packs: what is switched on in the stages people play, and fields written twice

def load_packs():
    packs = {}
    for folder, _, names in os.walk(os.path.join(SRC, "Content")):
        if os.path.basename(folder) != "Data":
            continue
        for name in sorted(names):
            if not name.endswith(".json"):
                continue
            with open(os.path.join(folder, name), encoding="utf-8-sig") as handle:
                data = json.load(handle)
            if isinstance(data, dict) and isinstance(data.get("id"), str) and ("extends" in data or "combat" in data):
                packs[data["id"]] = data
    return packs


def chain(packs, pack_id):
    out = []
    while pack_id is not None:
        if pack_id not in packs or pack_id in out:
            sys.exit("content pack '%s' is missing or extends itself" % pack_id)
        out.append(pack_id)
        pack_id = packs[pack_id].get("extends")
    return out


def merged(packs, pack_id):
    """A pack with everything it extends, merged as the game's loader merges: a section that is a list goes entry by
    entry on "id", a section that is an object field by field; the value of a field is replaced whole."""
    result = {}
    for layer in reversed(chain(packs, pack_id)):
        for key, value in packs[layer].items():
            if key in ("id", "extends"):
                continue
            if isinstance(value, list):
                entries = result.setdefault(key, [])
                for item in value:
                    entry = next((e for e in entries if e.get("id") == item.get("id")), None)
                    if entry is None:
                        entries.append(dict(item))
                    else:
                        entry.update(item)
            elif isinstance(value, dict):
                result.setdefault(key, {}).update(value)
            else:
                result[key] = value
    return result


def proof_holds(data, proof):
    """proof: section.field, section[].field (any entry of a list section) or either with =value."""
    path, _, wanted = proof.partition("=")
    nodes = [data]
    for part in path.split("."):
        key = part[:-2] if part.endswith("[]") else part
        found = []
        for node in nodes:
            if isinstance(node, dict) and key in node:
                value = node[key]
                found.extend(value if part.endswith("[]") and isinstance(value, list) else [value])
        nodes = found
    if wanted:
        return any(str(node).lower() == wanted.lower() for node in nodes)
    return any(node not in (None, False, 0, "", [], {}) for node in nodes)


def scan_features(packs, modules):
    stages, problems = [], []
    for number, cells in rows("played.tsv"):
        if len(cells) not in (3, 4):
            fail_table("played.tsv", number, "expected: stage, name, packs and, when its code switches features on, their ids")
        data = {}
        for item in cells[2].split(","):
            layer, _, only = item.partition(":")
            if layer not in packs:
                fail_table("played.tsv", number, "no content pack '%s'" % layer)
            full = merged(packs, layer)
            data.update({key: full[key] for key in (only.split("+") if only else full) if key in full})
        stages.append({"id": cells[0], "name": cells[1], "packs": cells[2], "data": data,
                       "code": cells[3].split(",") if len(cells) == 4 else [], "line": number})
    features = []
    for number, cells in rows("features.tsv"):
        if len(cells) not in (4, 5) or cells[4:] not in ([], ["off"]):
            fail_table("features.tsv", number, "expected: id, module, proof, name and, for a feature no stage has, off")
        if cells[1] not in modules:
            fail_table("features.tsv", number, "unknown module '%s'" % cells[1])
        on = [stage["id"] for stage in stages if proof_holds(stage["data"], cells[2])]
        code = [stage["id"] for stage in stages if cells[0] in stage["code"] and stage["id"] not in on]
        features.append({"id": cells[0], "module": cells[1], "proof": cells[2], "name": cells[3], "on": on, "code": code,
                         "packs": sorted(pack_id for pack_id in packs if proof_holds(merged(packs, pack_id), cells[2]))})
        if not on and not code and len(cells) == 4:
            problems.append("feature %s (%s) is on in no played stage: put %s in a pack a stage of tools/ruler/played.tsv runs on, "
                            "or it is not finished" % (cells[0], cells[3], cells[2].replace("[]", "")))
        elif (on or code) and len(cells) == 5:
            problems.append("feature %s is on now (%s): take 'off' from its row in tools/ruler/features.tsv" % (cells[0], ", ".join(on + code)))
    known = {feature["id"] for feature in features}
    for stage in stages:
        for feature in stage["code"]:
            if feature not in known:
                fail_table("played.tsv", stage["line"], "unknown feature '%s'" % feature)
    return [{"id": s["id"], "name": s["name"], "packs": s["packs"], "code": s["code"]} for s in stages], features, problems


def pack_copies(packs):
    """Fields a pack writes itself with the very value another pack of its family writes itself."""
    seen = {}
    for pack_id, data in packs.items():
        family = chain(packs, pack_id)[-1]
        for key, value in data.items():
            if key in ("id", "extends"):
                continue
            if isinstance(value, list):
                fields = [("%s[%s].%s" % (key, item.get("id"), name), leaf) for item in value for name, leaf in item.items() if name != "id"]
            elif isinstance(value, dict):
                fields = [(key + "." + name, leaf) for name, leaf in value.items()]
            else:
                fields = [(key, value)]
            for path, leaf in fields:
                seen.setdefault((family, path, json.dumps(leaf, sort_keys=True)), []).append(pack_id)
    return sorted((path, sorted(ids)) for (_, path, _), ids in seen.items() if len(ids) > 1)


# ---------------------------------------------------------------------------------------------------------------------

def size_debt():
    try:
        with open(SIZE_LEDGER, encoding="utf-8") as handle:
            for line in handle:
                if line.startswith("debt-max\t"):
                    return int(line.split("\t")[1])
    except OSError:
        pass
    return None


def measure():
    game, tests = source_files()
    sources = {rel: Source(rel, read(rel)) for rel in game}
    modules, exact, globs = load_modules()
    owner, unhomed, stale = assign(game, exact, globs)
    problems = ["%s has no module: give it a row under one module in tools/ruler/modules.tsv (--map lists the modules)" % rel
                for rel in unhomed]
    problems += ["modules.tsv: the rule '%s' names no file: delete it" % rule for rule in stale]

    outside = {}
    for top in OUTSIDE_ROOTS:
        for path in walk_cs(os.path.join(ROOT, top)):
            rel = "//" + os.path.relpath(path, ROOT).replace(os.sep, "/")
            if "/obj/" not in rel and "/bin/" not in rel:
                outside[rel] = Source(rel, read(path))
    edges = reference_graph({**sources, **outside})
    reached = reachable(edges, [rel for rel in game if rel.startswith("Unity/") or sources[rel].assembly_root] + list(outside))
    unused = {rel: sources[rel].lines for rel in game if rel not in reached}
    listed = {}
    for number, cells in rows("unused.tsv"):
        if len(cells) != 2:
            fail_table("unused.tsv", number, "expected: file, lines")
        listed[cells[0]] = whole("unused.tsv", number, cells[1])
    for rel, lines in sorted(unused.items()):
        if rel not in listed:
            problems.append("%s (%d lines) is reached by nothing a person can run: connect it to a played stage or delete it" % (rel, lines))
        elif lines > listed[rel]:
            problems.append("%s is unused and grew from %d to %d lines" % (rel, listed[rel], lines))
        elif lines < listed[rel]:
            problems.append("%s has %d lines now: lower its row in tools/ruler/unused.tsv" % (rel, lines))
    problems += ["%s is in use or gone: delete its row in tools/ruler/unused.tsv" % rel for rel in sorted(listed) if rel not in unused]

    per_module = {key: {"files": [], "lines": 0, "unused_lines": 0, "names": {}} for key in modules}
    for rel in game:
        if rel not in owner:
            continue
        entry = per_module[owner[rel]]
        entry["files"].append({"path": rel, "lines": sources[rel].lines, "used": rel in reached})
        entry["lines"] += sources[rel].lines
        entry["unused_lines"] += unused.get(rel, 0)
        for target in edges[rel]:
            other = owner.get(target)
            if other and other != owner[rel]:
                entry["names"][other] = entry["names"].get(other, 0) + 1
    for entry in per_module.values():
        # Most named first, then by key: the edges are sets, and their order must not reach the output.
        entry["names"] = dict(sorted(entry["names"].items(), key=lambda item: (-item[1], item[0])))

    one_home, salts, found = scan_one_home(sources, game)
    problems += found
    packs = load_packs()
    stages, features, found = scan_features(packs, modules)
    problems += found
    copies = pack_copies(packs)
    copied = sum(len(ids) - 1 for _, ids in copies)
    limit = 0
    for number, cells in rows("limits.tsv"):
        if cells[0] != "pack-copies" or len(cells) != 2:
            fail_table("limits.tsv", number, "expected: pack-copies, its count")
        limit = whole("limits.tsv", number, cells[1])
    if copied > limit:
        problems.append("pack-copies went up to %d (tools/ruler/limits.tsv says %d): a value belongs in one content pack, "
                        "the others extend it (--map lists the copies)" % (copied, limit))
    elif copied < limit:
        problems.append("pack-copies went down to %d: lower its row in tools/ruler/limits.tsv from %d" % (copied, limit))

    game_lines = sum(source.lines for source in sources.values())
    test_lines = sum(line_count(read(rel)) for rel in tests)
    return {
        "numbers": {
            "code-copies": sum(question["copies"] for question in one_home),
            "shared-salts": sum(len(entry["traits"]) - 1 for entry in salts),
            "pack-copies": copied,
            "unused-lines": sum(unused.values()),
            "features-off": sum(1 for feature in features if not feature["on"] and not feature["code"]),
        },
        "info": {"game-files": len(game), "game-lines": game_lines, "test-files": len(tests), "test-lines": test_lines,
                 "test-per-game-permille": test_lines * 1000 // max(game_lines, 1), "size-debt": size_debt(),
                 "unused-files": len(unused), "packs": len(packs), "features": len(features),
                 "questions-with-copies": sum(1 for question in one_home if question["copies"])},
        "modules": [{**modules[key], **per_module[key]} for key in modules],
        "unused": unused, "one_home": one_home, "shared_salts": salts, "stages": stages, "features": features,
        "pack_copies": [{"path": path, "packs": ids} for path, ids in copies], "problems": problems,
    }


def print_numbers(result):
    info, numbers = result["info"], result["numbers"]
    print("code-copies   %5d  answers written again, over %d questions (one-home.tsv)" % (numbers["code-copies"], info["questions-with-copies"]))
    print("shared-salts  %5d  traits drawn with another trait's salt (one-home.tsv)" % numbers["shared-salts"])
    print("features-off  %5d  of %d finished features, on in no played stage (features.tsv)" % (numbers["features-off"], info["features"]))
    print("unused-lines  %5d  in %d files that nothing a person can run reaches (unused.tsv)" % (numbers["unused-lines"], info["unused-files"]))
    print("pack-copies   %5d  fields written twice with one value in %d content packs (limits.tsv)" % (numbers["pack-copies"], info["packs"]))
    print("size-debt     %5s  size literals still to derive (tools/size-guard/ledger.tsv)" % info["size-debt"])
    print("tests         %d.%03d  test lines for every game line (%d game lines in %d files)"
          % (info["test-per-game-permille"] // 1000, info["test-per-game-permille"] % 1000, info["game-lines"], info["game-files"]))


def print_map(result):
    for module in result["modules"]:
        print("%s %s (%s, %s): %s" % (module["number"], module["key"], module["name"], module["block"], module["answers"]))
        print("    %d files, %d lines, %d lines unused" % (len(module["files"]), module["lines"], module["unused_lines"]))
        if module["names"]:
            print("    names: " + ", ".join("%s %d" % item for item in module["names"].items()))
        for entry in module["files"]:
            print("    %5d  %s%s" % (entry["lines"], entry["path"], "" if entry["used"] else "  (unused)"))
        print()
    print("answers written again, by question:")
    for question in result["one_home"]:
        print("    %3d  %-14s %s (home: %s)" % (question["copies"], question["id"], question["name"], question["home"]))
    print("salts drawn by more than one trait:")
    for entry in result["shared_salts"]:
        print("    %s  %s" % (entry["salt"], ", ".join(entry["traits"])))
    stages = result["stages"]
    print("features by stage (+ on, c switched on in the stage's code, . off): " + " ".join(stage["id"] for stage in stages))
    for feature in result["features"]:
        marks = "".join("+" if stage["id"] in feature["on"] else "c" if stage["id"] in feature["code"] else "." for stage in stages)
        print("    %s  %-16s %-7s %s" % (marks, feature["id"], feature["module"], feature["name"]))
    print("fields written twice with one value:")
    for entry in result["pack_copies"]:
        print("    %-44s %s" % (entry["path"], ", ".join(entry["packs"])))


def self_test():
    """The scanner on a few lines it must not misread; then the whole measurement under two hash seeds."""
    code = strip_code('a = "x // y { "; /* b { */ c = @"q "" { "; d = \'{\'; // e {\nf = 1;')
    assert "{" not in code and "f = 1" in code and "y" not in code and "b" not in code, code
    types, shared, spaces = declarations(strip_code("""
        namespace Game.Core {
            public interface IStep { void Step(int tick); int Order { get; } }
            public sealed partial class World : IStep {
                private readonly System.Collections.Generic.Dictionary<int, long> table = new System.Collections.Generic.Dictionary<int, long>();
                public const int Low = 1, High = 2;
                public int Count => table.Count;
                public long Size { get; private set; } = 3;
                public void Step(int tick) { int local = Helper(tick); }
                private static T Pick<T>(T a, T b) where T : struct { return a; }
                int IStep.Order => 0;
                public static bool operator ==(World a, World b) => true;
                public static bool operator !=(World a, World b) => false;
                private int Helper(int x) => x;
                private enum Mode { Off, On = 2, Auto }
                private struct Slot { internal int Index; }
            }
        }"""))
    assert spaces == {"Game.Core"} and shared == {"Step", "Order"}, (spaces, shared)
    assert types["World"][0] == {"table", "Low", "High", "Count", "Size", "Step", "Pick", "Order", "Helper", "Off", "On", "Auto", "Index"}, types["World"][0]
    assert types["World"][1] == {"Mode", "Slot"} and "local" not in types["World"][0], types["World"]
    files = {
        "a.cs": "namespace G { public partial class World { public void Run() { Tick(); } } }",
        "b.cs": "namespace G { public partial class World { private void Tick() { } } }",
        "c.cs": "namespace G { public partial class World { public void Never() { } } }",
        "d.cs": "namespace G { public class Lone { } }",
        "e.cs": "using G; namespace H { class Start { void Go(World w) { w.Run(); } } }",
    }
    edges = reference_graph({rel: Source(rel, text) for rel, text in files.items()})
    assert reachable(edges, ["e.cs"]) == {"e.cs", "a.cs", "b.cs"}, edges
    packs = {"base": {"id": "base", "ai": {"a": 1}, "units": [{"id": "u", "speed": 5}]},
             "top": {"id": "top", "extends": "base", "ai": {"b": 2}, "units": [{"id": "u", "dash": 3}]}}
    top = merged(packs, "top")
    assert top == {"ai": {"a": 1, "b": 2}, "units": [{"id": "u", "speed": 5, "dash": 3}]}, top
    assert proof_holds(top, "units[].dash") and proof_holds(top, "ai.b=2") and not proof_holds(top, "ai.c"), top
    # Sets are walked in hash order, which changes from run to run: none of it may reach the output.
    outputs = {subprocess.run([sys.executable, os.path.abspath(__file__), "--json", "-", "--map"], check=True, capture_output=True,
                              env={**os.environ, "PYTHONHASHSEED": seed}).stdout for seed in ("1", "2")}
    assert len(outputs) == 1, "the output changes with PYTHONHASHSEED"
    print("ruler: self-test ok")


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--map", action="store_true")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--json")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    result = measure()
    if args.json:
        text = json.dumps(result, ensure_ascii=False, indent=1)
        if args.json == "-":
            print(text)
        else:
            with open(args.json, "w", encoding="utf-8") as handle:
                handle.write(text + "\n")
    if args.map:
        print_map(result)
    if not args.map and args.json != "-":
        print_numbers(result)
    if args.check:
        for problem in result["problems"]:
            print("ruler: " + problem, file=sys.stderr)
        if result["problems"]:
            sys.exit(1)
        print("ruler: ok")


if __name__ == "__main__":
    main()
