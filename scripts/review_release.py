"""Review local lightweight assets. Product/CI acceptance is recorded separately."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile
from package_preview import ROOT, application_files, source_files, validate_architecture


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def review(root, application, release, installer, version, published_source=None, architecture='x64'):
    if architecture not in ('x64','arm64'):
        raise ValueError('Unsupported release architecture.')
    names = {f'nomozip-{version}-light-setup-win-{architecture}.exe', f'nomozip-{version}-light-win-{architecture}.zip', f'nomozip-source-{version}.zip'}
    checks = {'exact four lightweight assets': {p.name for p in release.iterdir()} == names | {'SHA256SUMS.txt'}}
    checks['native payload matches architecture'] = validate_architecture(application, architecture)
    assets = {name: digest(release / name) for name in sorted(names)}
    lines = (release / 'SHA256SUMS.txt').read_text(encoding='utf-8').splitlines()
    checks['exact checksum lines'] = sorted(lines) == sorted(f'{value}  {name}' for name, value in assets.items())
    checks['installer equals built input'] = (release / f'nomozip-{version}-light-setup-win-{architecture}.exe').read_bytes() == installer.read_bytes()
    with zipfile.ZipFile(release / f'nomozip-{version}-light-win-{architecture}.zip') as archive:
        files = {p.relative_to(application).as_posix(): p for p in application_files(application)}
        checks['payload CRC'] = archive.testzip() is None
        checks['payload exact current files'] = len(archive.namelist()) == len(files) and set(archive.namelist()) == set(files) and all(archive.read(name) == path.read_bytes() for name, path in files.items())
        payload_bytes = sum(p.stat().st_size for p in files.values())
    with zipfile.ZipFile(release / f'nomozip-source-{version}.zip') as archive:
        sources = {p.relative_to(root).as_posix(): p for p in source_files(root)}
        checks['source CRC'] = archive.testzip() is None
        checks['source exact allowlist and current files'] = len(archive.namelist()) == len(sources) and set(archive.namelist()) == set(sources) and all(archive.read(name) == path.read_bytes() for name, path in sources.items())
        if published_source is not None:
            checks['source equals uploaded checkout'] = all((published_source / name).is_file() and archive.read(name) == (published_source / name).read_bytes() for name in sources)
    return dict(passed=all(checks.values()), checks=checks, version=version, payloadBytes=payload_bytes,
                payloadFiles=len(files), sourceFiles=len(sources), assets=assets,
                validationScope='Local package integrity and source consistency; product CI and actual system experience are separate.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app-directory', required=True, type=Path)
    parser.add_argument('--release-directory', required=True, type=Path)
    parser.add_argument('--installer-path', required=True, type=Path)
    parser.add_argument('--version', required=True)
    parser.add_argument('--report', required=True, type=Path)
    parser.add_argument('--published-source', type=Path)
    parser.add_argument('--architecture', choices=('x64','arm64'), default='x64')
    args = parser.parse_args()
    try:
        result = review(ROOT, args.app_directory, args.release_directory, args.installer_path, args.version, args.published_source, args.architecture)
    except (OSError, RuntimeError, ValueError, zipfile.BadZipFile) as error:
        result = dict(passed=False, error=type(error).__name__, validationScope='Local package review')
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('passed', 'version', 'payloadBytes', 'payloadFiles', 'sourceFiles') if key in result}, ensure_ascii=False))
    if not result['passed']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
