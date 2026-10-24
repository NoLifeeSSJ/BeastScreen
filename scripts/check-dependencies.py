"""Fail the dependency review if NuGet reports vulnerabilities or diagnostic errors."""
import json
import sys

with open(sys.argv[1], encoding="utf-8-sig") as source:
    report = json.load(source)
issues = []
for item in report.get("logs", []):
    if item.get("level", "").lower() in {"error", "warning"}:
        issues.append(item)
for project in report.get("projects", []):
    for framework in project.get("frameworks", []):
        for key in ("topLevelPackages", "transitivePackages"):
            for package in framework.get(key, []):
                if package.get("vulnerabilities"):
                    issues.append({"project": project["path"], "package": package["id"], "vulnerabilities": package["vulnerabilities"]})
if not report.get("projects"):
    issues.append("No projects were reviewed")
if issues:
    print(json.dumps(issues, indent=2))
    sys.exit(1)
print(f"Reviewed {len(report['projects'])} projects; no NuGet vulnerabilities reported. Native dependency audit remains separate.")
