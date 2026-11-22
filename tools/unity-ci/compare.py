"""Compare two Unity test-result XML files (NUnit 3 format) test by test.

Usage: python3 compare.py <a.xml> <b.xml>
Prints totals of each, tests present in only one file, and tests whose result differs. Exit 1 if anything differs.
"""
import sys
import xml.etree.ElementTree as ET


def results(path):
    root = ET.parse(path).getroot()
    out = {}
    for c in root.iter("test-case"):
        out[c.get("fullname")] = c.get("result")
    return root.attrib, out


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    (ta, a), (tb, b) = results(argv[1]), results(argv[2])
    for name, t in (("a", ta), ("b", tb)):
        print(f"{name}: total={t.get('total')} passed={t.get('passed')} failed={t.get('failed')} "
              f"inconclusive={t.get('inconclusive')} skipped={t.get('skipped')}")
    only_a = sorted(set(a) - set(b))
    only_b = sorted(set(b) - set(a))
    diff = sorted(n for n in set(a) & set(b) if a[n] != b[n])
    print(f"only in a: {len(only_a)}, only in b: {len(only_b)}, different result: {len(diff)}")
    for n in only_a[:30]:
        print(f"  only a: {n}")
    for n in only_b[:30]:
        print(f"  only b: {n}")
    for n in diff[:100]:
        print(f"  {a[n]} -> {b[n]}: {n}")
    return 1 if (only_a or only_b or diff) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
