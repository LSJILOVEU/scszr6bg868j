#!/usr/bin/env python3
"""Personal-information guard for a public repository.

Generic rules are in this file. The private term list (names, accounts, hostnames) is NOT in the repository:
it comes from the PII_TERMS environment variable (a GitHub secret in CI), or the file named by PII_TERMS_FILE,
or ~/.warcore-pii-terms. One term per line; '#' starts a comment; a 'w:' prefix means whole-word match.
Matches never print the matched text, only rule id and location.

Modes (exit 0 clean, 1 findings, 2 setup problem):
  --staged                 added lines and file names in the staged diff
  --commit-msg FILE        a commit message plus the identity and date git is about to use
  --range 'A..B'           every commit in the range (several revisions allowed, space separated): message, identity, dates, diff
  --text FILE [--label L]  free text (pull request title/body)
  --tree [DIR]             every tracked file in the working tree
  --self-test
Add --require-terms to fail when no term list is available.

A squash merge made on GitHub is authored by the repository owner's GitHub noreply address and committed by
GitHub. In --range, only inside GitHub Actions (GITHUB_REPOSITORY_OWNER and GITHUB_REPOSITORY_OWNER_ID set),
that exact identity is accepted and the message length limits are not applied to it (the owner login and id
are already public). The message is still scanned for terms and generic rules.
"""
import os, re, subprocess, sys, tempfile

ALLOWED_EMAILS = {"noreply@users.noreply.github.com", "noreply@github.com", "noreply@anthropic.com"}
ALLOWED_EMAIL_DOMAINS = ("example.com", "example.org", "example.net", "localhost")
MEDIA_EXT = (".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".psd",
             ".mp4", ".mov", ".avi", ".mkv", ".webm", ".pdf")
SELF_DIR = "tools/pii-guard/"
MAX_SUBJECT = 72
MAX_MESSAGE_LINES = 8
MAX_MESSAGE_CHARS = 400
MAX_FILE_BYTES = 5_000_000

U = "Use" + "rs"  # keep rule sources from matching themselves
LINE_RULES = [
    ("email", re.compile(r"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}")),
    ("home-path", re.compile(r"(?:/" + U + r"/|/home/|[A-Za-z]:\\" + U + r"\\|-" + U + r"-)[A-Za-z0-9._\-]+")),
    ("session-link", re.compile(r"claude\.ai/|anthropic\.com/(?:code|chat)|\bcse_[0-9A-Za-z]{6,}|\bsession_[0-9A-Za-z]{8,}|\bcmsg_[0-9A-Za-z]{8,}|\bchan_[0-9A-Za-z]{8,}")),
    ("timezone", re.compile(r"\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?\s?(?:[+\-](?!00:?00\b)\d{2}:?\d{2}\b)")),
    ("key", re.compile(r"ghp_[A-Za-z0-9]{30,}|gho_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|sk-[A-Za-z0-9_\-]{30,}|AKIA[0-9A-Z]{16}|xox[bpa]-[A-Za-z0-9\-]{10,}|AIza[0-9A-Za-z_\-]{35}|-----BEGIN [A-Z ]*PRIVATE KEY-----")),
]


def load_terms():
    raw = os.environ.get("PII_TERMS")
    if raw is None:
        path = os.environ.get("PII_TERMS_FILE") or os.path.expanduser("~/.warcore-pii-terms")
        raw = open(path, encoding="utf-8").read() if os.path.isfile(path) else ""
    terms = []
    for line in raw.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        word = line.startswith("w:")
        t = (line[2:] if word else line).strip().lower()
        if t:
            terms.append((word, t, re.compile(r"(?<![a-z0-9])" + re.escape(t) + r"(?![a-z0-9])") if word else None))
    return terms


def email_ok(match):
    e = match.lower()
    return e in ALLOWED_EMAILS or e.split("@", 1)[1].endswith(ALLOWED_EMAIL_DOMAINS)


def scan_line(text, terms, generic=True):
    """Return rule ids found in one line of text."""
    found = []
    if generic:
        for rid, rx in LINE_RULES:
            for m in rx.finditer(text):
                if rid == "email" and email_ok(m.group(0)):
                    continue
                found.append(rid)
                break
    low = text.lower()
    for word, t, rx in terms:
        if (rx.search(low) if word else t in low):
            found.append("term")
            break
    return found


class Report:
    def __init__(self):
        self.items = []

    def add(self, rid, where):
        self.items.append((rid, where))

    def finish(self):
        if not self.items:
            print("pii-guard: clean")
            return 0
        seen = set()
        for rid, where in self.items:
            if (rid, where) not in seen:
                seen.add((rid, where))
                print("pii-guard: [%s] %s" % (rid, where))
        print("pii-guard: %d finding(s). Matched text is not printed. Remove the information and retry." % len(seen))
        return 1


def git(*args, cwd=None, check=True):
    p = subprocess.run(["git"] + list(args), cwd=cwd, capture_output=True)
    if check and p.returncode != 0:
        raise SystemExit("pii-guard: git %s failed: %s" % (args[0], p.stderr.decode("utf-8", "replace").strip()))
    return p.stdout.decode("utf-8", "replace")


def scan_text_block(text, where, terms, rep, generic=True):
    for i, line in enumerate(text.splitlines(), 1):
        for rid in scan_line(line, terms, generic):
            rep.add(rid, "%s:%d" % (where, i))


def scan_path(path, terms, rep, label=""):
    generic = not path.lower().startswith(SELF_DIR)
    for rid in scan_line(path, terms, generic):
        rep.add(rid, "%sfile name %s" % (label, path if rid != "term" else "(redacted)"))
    if generic and path.lower().endswith(MEDIA_EXT):
        rep.add("media-file", "%s%s" % (label, path))


def scan_diff(diff, terms, rep, label=""):
    path, line_no = None, 0
    for raw in diff.splitlines():
        if raw.startswith("+++ "):
            path = raw[4:]
            path = path[2:] if path.startswith("b/") else path
            if path != "/dev/null":
                scan_path(path, terms, rep, label)
        elif raw.startswith("@@"):
            m = re.search(r"\+(\d+)", raw)
            line_no = int(m.group(1)) - 1 if m else 0
        elif raw.startswith("+") and not raw.startswith("+++"):
            line_no += 1
            generic = not (path or "").lower().startswith(SELF_DIR)
            for rid in scan_line(raw[1:], terms, generic):
                rep.add(rid, "%s%s:%d" % (label, path, line_no))
        elif raw.startswith(" "):
            line_no += 1


def check_message(msg, where, terms, rep, web_merge=False):
    lines = [l for l in msg.rstrip().splitlines()]
    if not lines or not lines[0].strip():
        rep.add("message-empty", where)
        return
    if not web_merge and len(lines[0]) > MAX_SUBJECT:
        rep.add("message-long-subject", where)
    if not web_merge and (len(lines) > MAX_MESSAGE_LINES or len(msg) > MAX_MESSAGE_CHARS):
        rep.add("message-too-long", where)
    scan_text_block(msg, where + " message", terms, rep)


def check_identity(name, email, date, role, where, terms, rep):
    if email.lower() not in ALLOWED_EMAILS:
        rep.add("identity-email", "%s %s" % (where, role))
    for rid in scan_line(name + " " + email, terms, generic=False):
        rep.add(rid, "%s %s identity" % (where, role))
    if not re.search(r"(?:\+00:?00|Z)$", date.strip()):
        rep.add("timezone", "%s %s date (use TZ=UTC)" % (where, role))


def repo_owner():
    """(login, numeric id) of the repository owner, only when running in GitHub Actions; else None."""
    login = os.environ.get("GITHUB_REPOSITORY_OWNER", "").strip().lower()
    oid = os.environ.get("GITHUB_REPOSITORY_OWNER_ID", "").strip()
    return (login, oid) if login and oid.isdigit() else None


def is_web_merge(an, ae, cn, ce, owner):
    if not owner:
        return False
    login, oid = owner
    return (ae.strip().lower() == "%s+%s@users.noreply.github.com" % (oid, login) and an.strip().lower() == login
            and cn == "GitHub" and ce.lower() == "noreply@github.com")


def mode_staged(terms, rep):
    names = git("diff", "--cached", "--name-only", "--diff-filter=ACMR").splitlines()
    for n in names:
        scan_path(n, terms, rep, "staged ")
    scan_diff(git("diff", "--cached", "-U0", "--no-color", "--no-ext-diff", "--diff-filter=ACMR"), terms, rep, "staged ")


def mode_commit_msg(path, terms, rep):
    msg = "".join(l for l in open(path, encoding="utf-8", errors="replace") if not l.startswith("#"))
    check_message(msg, "commit", terms, rep)
    for role, var in (("author", "GIT_AUTHOR_IDENT"), ("committer", "GIT_COMMITTER_IDENT")):
        ident = git("var", var).strip()
        m = re.match(r"(.*) <(.*)> (\d+) ([+\-]\d{4})$", ident)
        if not m:
            rep.add("identity-unparsed", role)
            continue
        check_identity(m.group(1), m.group(2), m.group(4).replace("+0000", "Z"), role, "commit", terms, rep)


def mode_range(rng, terms, rep, owner=False):
    owner = repo_owner() if owner is False else owner
    out = git("log", "--format=%H%x1f%an%x1f%ae%x1f%cn%x1f%ce%x1f%aI%x1f%cI%x1f%B%x1e", *rng.split())
    for rec in out.split("\x1e"):
        rec = rec.strip("\n")
        if not rec.strip():
            continue
        sha, an, ae, cn, ce, ad, cd, body = rec.split("\x1f", 7)
        w = "commit " + sha[:10]
        web = is_web_merge(an, ae, cn, ce, owner)
        if web:
            check_identity("warcore", "noreply@users.noreply.github.com", ad, "author", w, terms, rep)
        else:
            check_identity(an, ae, ad, "author", w, terms, rep)
        check_identity(cn, ce, cd, "committer", w, terms, rep)
        check_message(body, w, terms, rep, web_merge=web)
        scan_diff(git("diff-tree", "-p", "-U0", "--no-color", "--no-ext-diff", "--root", "-r", "--no-commit-id", sha), terms, rep, w + " ")


def mode_tree(root, terms, rep):
    names = git("ls-files", "-z", cwd=root).split("\0")
    for n in filter(None, names):
        scan_path(n, terms, rep)
        full = os.path.join(root, n)
        try:
            if os.path.getsize(full) > MAX_FILE_BYTES or not os.path.isfile(full):
                continue
            data = open(full, "rb").read()
        except OSError:
            continue
        if b"\0" in data:
            continue
        scan_text_block(data.decode("utf-8", "replace"), n, terms, rep, generic=not n.lower().startswith(SELF_DIR))


def self_test():
    ok = True

    def expect(name, cond):
        nonlocal ok
        print(("ok   " if cond else "FAIL ") + name)
        ok = ok and cond

    t = [(False, "zzname", None), (True, "qq", re.compile(r"(?<![a-z0-9])qq(?![a-z0-9])"))]
    expect("email flagged", "email" in scan_line("mail a" + "@b.co", t))
    expect("noreply allowed", not scan_line("noreply@users.noreply.github.com", t))
    expect("home path", "home-path" in scan_line("/" + U + "/someone/x", t))
    expect("home path dash form", "home-path" in scan_line("-" + U + "-someone-x", t))
    expect("session link", "session-link" in scan_line("see https://claude." + "ai/code/x", t))
    expect("timezone flagged", "timezone" in scan_line("10:00:00 +0100", t))
    expect("utc timezone ok", not scan_line("10:00:00 +0000", t))
    expect("key", "key" in scan_line("AKIA" + "A" * 16, t))
    expect("term substring", scan_line("xxZZNAMExx", t) == ["term"])
    expect("term word boundary hit", scan_line("a qq b", t) == ["term"])
    expect("term word boundary miss", not scan_line("aqqb", t))
    expect("plain code clean", not scan_line("var x = a[10:20]; // fine", t))
    r = Report(); check_message("x" * 80, "c", [], r)
    expect("long subject", any(i[0] == "message-long-subject" for i in r.items))
    r = Report(); check_message("ok\n" + "\n".join("l" for _ in range(12)), "c", [], r)
    expect("long message", any(i[0] == "message-too-long" for i in r.items))
    r = Report(); check_identity("n", "a" + "@b.co", "2026-01-01T00:00:00+00:00", "author", "c", [], r)
    expect("identity email", any(i[0] == "identity-email" for i in r.items))
    r = Report(); check_identity("warcore", "noreply@users.noreply.github.com", "2026-01-01T01:00:00+01:00", "author", "c", [], r)
    expect("identity date tz", any(i[0] == "timezone" for i in r.items))
    r = Report(); check_identity("warcore", "noreply@users.noreply.github.com", "2026-01-01T00:00:00+00:00", "author", "c", [], r)
    expect("identity ok", not r.items)
    gh = ("Owner1", "123+Owner1@users.noreply.github.com", "GitHub", "noreply@github.com")
    expect("web merge accepted for owner", is_web_merge(*gh, owner=("owner1", "123")))
    expect("web merge rejected for other login", not is_web_merge(*gh, owner=("other", "123")))
    expect("web merge rejected for other id", not is_web_merge(gh[0], "5550199+Owner1@users.noreply.github.com",
                                                            gh[2], gh[3], owner=("owner1", "123")))
    expect("web merge rejected outside Actions", not is_web_merge(*gh, owner=None))
    expect("web merge needs GitHub committer", not is_web_merge(gh[0], gh[1], "Owner1", gh[1], owner=("owner1", "123")))
    long_body = "x" * 80 + "\n" + "\n".join("* item" for _ in range(12))
    r = Report(); check_message(long_body, "c", [], r, web_merge=True)
    expect("web merge length not limited", not r.items)
    r = Report(); check_message(long_body + "\n/" + U + "/someone/x", "c", [], r, web_merge=True)
    expect("web merge message still scanned", any(i[0] == "home-path" for i in r.items))
    # end to end in a throwaway repo
    with tempfile.TemporaryDirectory() as d:
        env = dict(os.environ, TZ="UTC", GIT_AUTHOR_NAME="warcore", GIT_AUTHOR_EMAIL="noreply@users.noreply.github.com",
                   GIT_COMMITTER_NAME="warcore", GIT_COMMITTER_EMAIL="noreply@users.noreply.github.com")
        def g(*a):
            return subprocess.run(["git"] + list(a), cwd=d, env=env, capture_output=True, text=True)
        g("init", "-q", "-b", "main")
        open(os.path.join(d, "a.txt"), "w").write("hello\n")
        g("add", "-A"); g("commit", "-q", "-m", "init")
        open(os.path.join(d, "b.txt"), "w").write("path /" + U + "/someone/x\n")
        g("add", "-A"); g("commit", "-q", "-m", "second")
        open(os.path.join(d, "b.txt"), "w").write("clean\n")
        g("add", "-A"); g("commit", "-q", "-m", "third")
        cwd = os.getcwd(); os.chdir(d)
        try:
            r = Report(); mode_range("HEAD~2..HEAD", [], r)
            expect("range finds PII added then removed", any(i[0] == "home-path" for i in r.items))
            r = Report(); mode_range("HEAD~1..HEAD", [], r)
            expect("range clean after removal", not r.items)
            wenv = dict(env, GIT_AUTHOR_NAME="Owner1", GIT_AUTHOR_EMAIL="123+Owner1@users.noreply.github.com",
                        GIT_COMMITTER_NAME="GitHub", GIT_COMMITTER_EMAIL="noreply@github.com")
            open(os.path.join(d, "c.txt"), "w").write("merged\n")
            g("add", "-A")
            subprocess.run(["git", "commit", "-q", "-m", "x" * 80 + " (#7)\n\n" + "\n".join("* c" for _ in range(10))],
                           cwd=d, env=wenv, capture_output=True)
            owner_terms = [(False, "owner1", None)]
            r = Report(); mode_range("HEAD~1..HEAD", owner_terms, r, owner=("owner1", "123"))
            expect("range accepts owner web merge", not r.items)
            r = Report(); mode_range("HEAD~1..HEAD", owner_terms, r, owner=("someoneelse", "123"))
            expect("range rejects web merge for other owner", any(i[0] == "identity-email" for i in r.items))
            r = Report(); mode_range("HEAD~1..HEAD", owner_terms, r, owner=None)
            expect("range rejects web merge outside Actions", any(i[0] == "identity-email" for i in r.items))
            r = Report(); mode_tree(d, [], r)
            expect("tree clean", not r.items)
        finally:
            os.chdir(cwd)
    print("self-test " + ("passed" if ok else "FAILED"))
    return 0 if ok else 1


def main(argv):
    args = list(argv)
    require = "--require-terms" in args
    if require:
        args.remove("--require-terms")
    if not args:
        print(__doc__); return 2
    if args[0] == "--self-test":
        return self_test()
    terms = load_terms()
    if require and not terms:
        print("pii-guard: no term list available (set PII_TERMS or PII_TERMS_FILE); refusing.")
        return 2
    if not terms:
        print("pii-guard: note: no private term list found, generic rules only")
    rep = Report()
    mode = args[0]
    if mode == "--staged":
        mode_staged(terms, rep)
    elif mode == "--commit-msg":
        mode_commit_msg(args[1], terms, rep)
    elif mode == "--range":
        mode_range(args[1], terms, rep)
    elif mode == "--text":
        label = args[args.index("--label") + 1] if "--label" in args else "text"
        scan_text_block(open(args[1], encoding="utf-8", errors="replace").read(), label, terms, rep)
    elif mode == "--tree":
        mode_tree(args[1] if len(args) > 1 else ".", terms, rep)
    else:
        print(__doc__); return 2
    return rep.finish()


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
