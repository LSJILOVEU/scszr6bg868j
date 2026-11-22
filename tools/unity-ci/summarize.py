"""Print a Markdown summary of Unity test-result XML files (NUnit 3 format) found in a folder.

Usage: python3 summarize.py <folder>
Lists totals per file and up to 50 failed test names. Exits 0 even when tests failed; the test step decides the job result.
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET

MAX_FAILED = 50


def summarize(path):
    root = ET.parse(path).getroot()
    a = root.attrib
    lines = [
        f"### {os.path.basename(path)}",
        "",
        "| result | total | passed | failed | inconclusive | skipped | seconds |",
        "|---|---|---|---|---|---|---|",
        f"| {a.get('result')} | {a.get('total')} | {a.get('passed')} | {a.get('failed')} | "
        f"{a.get('inconclusive')} | {a.get('skipped')} | {float(a.get('duration', 0)):.0f} |",
    ]
    failed = [c.get("fullname") for c in root.iter("test-case") if c.get("result") == "Failed"]
    if failed:
        lines += ["", f"Failed ({len(failed)}):", ""]
        lines += [f"- `{name}`" for name in failed[:MAX_FAILED]]
        if len(failed) > MAX_FAILED:
            lines.append(f"- ... and {len(failed) - MAX_FAILED} more")
    return "\n".join(lines)


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2
    files = sorted(glob.glob(os.path.join(argv[1], "*.xml")))
    if not files:
        print("No Unity test-result XML found (the editor did not finish; see the test step log).")
        return 0
    for f in files:
        try:
            print(summarize(f))
        except ET.ParseError as e:
            print(f"### {os.path.basename(f)}\n\nUnreadable XML: {e}")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
