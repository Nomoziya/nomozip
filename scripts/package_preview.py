"""Package the preview and reviewable sources from an explicit allowlist."""
from pathlib import Path
import argparse
import zipfile

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--app-directory', default='artifacts/app')
parser.add_argument('--output-directory', default='artifacts')
parser.add_argument('--version', default='0.1-preview')
parser.add_argument('--skip-source', action='store_true')
parser.add_argument('--source-name', default='nomozip-source-preview.zip')
args = parser.parse_args()
OUTPUT = ROOT / args.output_directory
OUTPUT.mkdir(parents=True, exist_ok=True)


def archive(path, files, base):
    if path.exists():
        raise RuntimeError('Keep release archives immutable; use a fresh output directory.')
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as package:
        for file in sorted(files):
            package.write(file, file.relative_to(base).as_posix())


application = ROOT / args.app_directory
if (application / "coreclr.dll").exists():
    raise RuntimeError("Only shared-runtime lightweight releases are published; bundled payloads are reserved for legacy installer validation.")
app_files = [file for file in application.rglob("*") if file.is_file()]
if not (application / "ArchivePassword.exe").exists():
    raise RuntimeError("Build the application first.")
if any(file.suffix in ('.apv', '.pem', '.dpapi') and file.name != 'update-public-key.pem' for file in app_files):
    raise RuntimeError("Unexpected private material in app package.")
archive(OUTPUT / f"nomozip-{args.version}-win-x64.zip", app_files, application)
if args.skip_source:
    print(f"Packaged {len(app_files)} app files; source snapshot already packaged separately.")
    raise SystemExit(0)

sources = []
for folder in ("src", "tests", "tools", "scripts", "docs", "config", "resources", "design-system", "installer", ".github"):
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
archive(OUTPUT / args.source_name, sources, ROOT)
print(f"Packaged {len(app_files)} app files and {len(sources)} source files.")
