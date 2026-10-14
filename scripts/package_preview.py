"""Package the preview and reviewable sources from an explicit allowlist."""
from pathlib import Path
import argparse
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def archive(path, files, base):
    if path.exists():
        raise RuntimeError('Keep release archives immutable; use a fresh output directory.')
    with zipfile.ZipFile(path, "x", compression=zipfile.ZIP_DEFLATED) as package:
        for file in sorted(files):
            package.write(file, file.relative_to(base).as_posix())


def private_material(file):
    return ('private' in file.name.lower() or file.suffix.lower() in {'.apv', '.dpapi', '.pfx', '.key'}
            or (file.suffix.lower() == '.pem' and file.name != 'update-public-key.pem'))


def application_files(application):
    files = [file for file in application.rglob('*') if file.is_file()]
    if any(file.name.lower() in {'coreclr.dll', 'hostfxr.dll', 'nomozipmodernmenu.dll'} for file in files):
        raise RuntimeError('Only shared-runtime lightweight releases are packaged; bundled runtime and menu identity prototypes are not release payloads.')
    if not (application / 'ArchivePassword.exe').is_file():
        raise RuntimeError('Build the application first.')
    if any(private_material(file) for file in files):
        raise RuntimeError('Unexpected private material in app package.')
    return files


def source_files(root):
    files = []
    for folder in ('src', 'tests', 'tools', 'scripts', 'docs', 'config', 'resources', 'design-system', 'installer', '.github'):
        files.extend(file for file in (root / folder).rglob('*') if file.is_file()
                     and not any(part in ('bin', 'obj', 'generated', '__pycache__') for part in file.relative_to(root).parts))
    for name in ('README.md', 'PROJECT_PLAN.md', 'THIRD_PARTY.md', 'global.json', 'Directory.Build.props', 'NuGet.Config', '.gitignore', '启动软件.cmd'):
        files.append(root / name)
    files.extend(file for file in (root / 'engines').rglob('*') if file.is_file() and file.name in ('manifest.json', 'License.txt'))
    if any(private_material(file) for file in files):
        raise RuntimeError('Unexpected private material in source package.')
    if any(not file.is_file() for file in files):
        raise RuntimeError('Required source files are missing.')
    return files


def package(root, application, output, version, source_name, skip_source=False):
    # Inspect both inputs and output names before creating either archive.
    if any(Path(name).name != name or '/' in name or '\\' in name for name in (version, source_name)):
        raise ValueError('Package names must be file names.')
    app_files = application_files(application)
    sources = [] if skip_source else source_files(root)
    targets = [output / f'nomozip-{version}-win-x64.zip'] + ([] if skip_source else [output / source_name])
    if len(set(targets)) != len(targets) or any(path.exists() for path in targets):
        raise RuntimeError('Keep release archives immutable; use a fresh output directory.')
    output.mkdir(parents=True, exist_ok=True)
    archive(targets[0], app_files, application)
    if not skip_source:
        archive(targets[1], sources, root)
    return len(app_files), len(sources)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--app-directory', default='artifacts/app')
    parser.add_argument('--output-directory', default='artifacts')
    parser.add_argument('--version', default='0.1-preview')
    parser.add_argument('--skip-source', action='store_true')
    parser.add_argument('--source-name', default='nomozip-source-preview.zip')
    args = parser.parse_args()
    apps, sources = package(ROOT, ROOT / args.app_directory, ROOT / args.output_directory, args.version, args.source_name, args.skip_source)
    print(f'Packaged {apps} app files and {sources} source files.')


if __name__ == '__main__':
    main()
