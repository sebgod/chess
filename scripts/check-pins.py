#!/usr/bin/env python
"""Check a repo's NuGet pins against what is published, and against each other.

Two questions, asked of every <PackageVersion> in a Directory.Packages.props:

1. Is the pin behind? A floating pin (X.Y.*) is compared with the newest published X'.Y'.
2. Does every library the app loads agree on the shared dependencies? Each pinned package's
   published .nuspec lists the versions it was BUILT against. When the app pins a shared
   dependency (DIR.Lib, say) at a newer version than a library was built against, NuGet quietly
   loads the library on the app's version: fine across additive minors, broken across a MAJOR
   (a removed member fails at run time, MissingMethodException, never at build time).

Needs only nuget.org (no GitHub, no sibling clones), so any repo can copy this file as it is.
See docs/pinning.md for the rule and what to do with each finding.

    python scripts/check-pins.py [path/to/Directory.Packages.props] [--only Prefix,Other] [--majors] [--markdown]

"Behind" means a newer Major.Minor within the pin's MAJOR: a routine bump. A newer major (a .NET 11
package for a .NET 10 app, say) is a deliberate upgrade, never a routine one, so it is only listed
with --majors.

Exit code 1 when a library was built against a different MAJOR of a dependency the app pins;
0 otherwise (a pin behind, or a minor gap, is reported but does not fail).
"""
import json
import re
import sys
import urllib.request
import xml.etree.ElementTree as ET

FLAT = "https://api.nuget.org/v3-flatcontainer"


def fetch(url):
    with urllib.request.urlopen(url, timeout=30) as response:
        return response.read()


def versions(package):
    try:
        return json.loads(fetch(f"{FLAT}/{package.lower()}/index.json"))["versions"]
    except Exception:
        return []


def key(version):
    core, _, pre = version.partition("-")
    nums = [int(p) if p.isdigit() else 0 for p in core.split(".")]
    return nums + [0] * (4 - len(nums)), pre == "", pre


def resolve(pin, published):
    """The version a pin restores to: the highest match for X.Y.*, the exact one otherwise."""
    if pin.endswith(".*"):
        prefix = pin[:-1]
        matches = [v for v in published if v.startswith(prefix) and "-" not in v]
        return max(matches, key=key) if matches else None
    return pin if pin in published else None


def newest(pin, published):
    stable = [v for v in published if "-" not in v]
    pool = published if ("-" in pin or not stable) else stable
    return max(pool, key=key) if pool else None


def major_minor(version):
    parts = version.split("-")[0].split(".")
    return int(parts[0]), int(parts[1]) if len(parts) > 1 and parts[1].isdigit() else 0


def built_against(package, version):
    """{dependency id: version} from the package's published nuspec, every target framework merged."""
    try:
        xml = fetch(f"{FLAT}/{package.lower()}/{version.lower()}/{package.lower()}.nuspec")
    except Exception:
        return {}
    deps = {}
    for element in ET.fromstring(xml).iter():
        if element.tag.endswith("dependency") and element.get("id") and element.get("version"):
            # "[11.61.6461, )" or "11.61.6461": the lower bound is what it was built against.
            deps[element.get("id")] = re.sub(r"[\[\]() ]", "", element.get("version")).split(",")[0]
    return deps


def main(argv):
    path = "Directory.Packages.props"
    only = None
    markdown = "--markdown" in argv
    majors = "--majors" in argv
    args = [a for a in argv if a not in ("--markdown", "--majors")]
    if "--only" in args:
        i = args.index("--only")
        only = tuple(args[i + 1].split(","))
        del args[i:i + 2]
    if args:
        path = args[0]

    text = open(path, encoding="utf-8").read()
    pins = dict(re.findall(r'<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"', text))
    checked = {name: pin for name, pin in pins.items() if only is None or name.startswith(only)}

    behind, gaps, errors = [], [], 0
    resolved = {}
    for name, pin in sorted(checked.items()):
        published = versions(name)
        current = resolve(pin, published)
        latest = newest(pin, published)
        resolved[name] = current
        if latest and current and major_minor(latest) > major_minor(current):
            same_major = major_minor(latest)[0] == major_minor(current)[0]
            if same_major or majors:
                behind.append((name, pin, current, latest + ("" if same_major else "  (new major)")))

    for name, current in sorted(resolved.items()):
        if not current:
            continue
        for dep, dep_version in sorted(built_against(name, current).items()):
            if dep not in pins or dep == name:
                continue
            app = resolved.get(dep) or pins[dep]
            if not app or app.endswith(".*"):
                continue
            built, loaded = major_minor(dep_version), major_minor(app)
            if built == loaded:
                continue
            severity = "MAJOR" if built[0] != loaded[0] else "minor"
            errors += severity == "MAJOR"
            gaps.append((severity, name, current, dep, dep_version, app))

    def table(header, rows):
        if markdown:
            print("| " + " | ".join(header) + " |")
            print("|" + "---|" * len(header))
            for row in rows:
                print("| " + " | ".join(row) + " |")
        else:
            widths = [max(len(header[i]), *(len(r[i]) for r in rows)) for i in range(len(header))]
            print("  " + "  ".join(h.ljust(w) for h, w in zip(header, widths)))
            for row in rows:
                print("  " + "  ".join(c.ljust(w) for c, w in zip(row, widths)))
        print()

    print(("### " if markdown else "") + f"Pins behind nuget.org ({path})")
    if behind:
        table(["Package", "Pin", "Restores", "Newest"], [(n, p, c, l) for n, p, c, l in behind])
    else:
        print("None: every pin restores its package's newest Major.Minor.\n")

    print(("### " if markdown else "") + "Built against vs loaded with")
    if gaps:
        table(["Gap", "Library", "Version", "Dependency", "Built against", "App loads"],
              [(s, n, c, d, b, a) for s, n, c, d, b, a in gaps])
        if errors:
            print("A MAJOR gap is an error: rebuild that library against the app's version and release it "
                  "(docs/pinning.md). A minor gap is fine when the dependency's CHANGELOG calls the steps additive.")
    else:
        print("None: every library was built against the shared dependency versions the app pins.")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
