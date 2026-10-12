"""Package the preview and reviewable sources from an explicit allowlist."""
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "artifacts"


def archive(path, files, base):
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as package:
        for file in sorted(files):
            package.write(file, file.relative_to(base).as_posix())


application = ROOT / "artifacts/app"
app_files = [file for file in application.rglob("*") if file.is_file()]
if not (application / "ArchivePassword.exe").exists():
    raise RuntimeError("Build the application first.")
if any("private" in file.name.lower() or file.suffix == ".apv" for file in app_files):
    raise RuntimeError("Unexpected private material in app package.")
archive(OUTPUT / "ArchivePassword-0.1-preview-win-x64.zip", app_files, application)

sources = []
for folder in ("src", "tests", "tools", "scripts", "docs", "config", "resources", "design-system", ".github"):
    for file in (ROOT / folder).rglob("*"):
        if file.is_file() and not any(part in ("bin", "obj", "generated", "__pycache__") for part in file.relative_to(ROOT).parts):
            sources.append(file)
for name in ("README.md", "PROJECT_PLAN.md", "THIRD_PARTY.md", "global.json", "Directory.Build.props", "NuGet.Config", ".gitignore", "启动软件.cmd"):
    sources.append(ROOT / name)
for file in (ROOT / "engines").rglob("*"):
    if file.is_file() and file.name in ("manifest.json", "License.txt"):
        sources.append(file)
if any("private" in file.name.lower() or file.suffix == ".apv" for file in sources):
    raise RuntimeError("Unexpected private material in source package.")
archive(OUTPUT / "nomozip-source-preview.zip", sources, ROOT)
print(f"Packaged {len(app_files)} app files and {len(sources)} source files.")
