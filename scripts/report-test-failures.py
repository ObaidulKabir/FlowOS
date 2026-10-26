#!/usr/bin/env python3
"""Turn failed tests in TRX files into GitHub Actions annotations and a job summary.

Usage: python3 scripts/report-test-failures.py test-results/*.trx
GitHub shows at most 10 error annotations per step, so the first 10 failures become
annotations and every failure is listed in the job summary.
"""
import os
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def one_line(text, limit=400):
    text = " ".join((text or "").split())
    return text[:limit] + ("…" if len(text) > limit else "")


def main(paths):
    failures, totals = [], {}
    for path in paths:
        try:
            root = ET.parse(path).getroot()
        except (ET.ParseError, FileNotFoundError):
            continue
        counters = root.find(".//t:ResultSummary/t:Counters", NS)
        if counters is not None:
            totals[os.path.basename(path)] = (counters.get("total"), counters.get("passed"), counters.get("failed"))
        for result in root.iterfind(".//t:UnitTestResult", NS):
            if result.get("outcome") != "Failed":
                continue
            message = result.findtext(".//t:ErrorInfo/t:Message", default="", namespaces=NS)
            stack = result.findtext(".//t:ErrorInfo/t:StackTrace", default="", namespaces=NS)
            first_frame = next((line.strip() for line in stack.splitlines() if " in " in line), "")
            failures.append((os.path.basename(path), result.get("testName"), one_line(message), one_line(first_frame, 200)))

    for suite, name, message, frame in failures[:10]:
        print(f"::error title={suite}: {name}::{message} {frame}".replace("\n", " "))

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    lines = ["## Test results", "", "| Suite | Total | Passed | Failed |", "|---|---|---|---|"]
    lines += [f"| {k} | {v[0]} | {v[1]} | {v[2]} |" for k, v in sorted(totals.items())]
    if failures:
        lines += ["", f"### {len(failures)} failed test(s)", "", "| Suite | Test | Message |", "|---|---|---|"]
        lines += [f"| {s} | `{n}` | {m.replace('|', '/')} |" for s, n, m, _ in failures]
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
    print("\n".join(lines))
    print(f"::notice title=Test totals::{'; '.join(f'{k}: {v[2]} failed of {v[0]}' for k, v in sorted(totals.items()))}")


if __name__ == "__main__":
    main(sys.argv[1:])
