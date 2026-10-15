"""Exercise package integrity and real Windows/NSIS release commands using owned fixtures."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
import zipfile
import struct

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from package_preview import package, application_files, source_files
from review_release import review

checks = {}


def check(name, value):
    checks[name] = bool(value)


def rejected(action):
    try:
        action()
    except (RuntimeError, ValueError):
        return True
    return False


def hashes(folder):
    return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in folder.iterdir() if p.is_file()}


def checksum(release):
    (release / 'SHA256SUMS.txt').write_text(''.join(f'{value}  {name}\n' for name, value in sorted(hashes(release).items()) if name != 'SHA256SUMS.txt'), encoding='utf-8')


def rewrite(path, replace=None, omit=None, extra=None):
    with zipfile.ZipFile(path) as archive:
        data = {name: archive.read(name) for name in archive.namelist()}
    if replace:
        data.update(replace)
    if omit:
        del data[omit]
    if extra:
        data.update(extra)
    with zipfile.ZipFile(path, 'w') as archive:
        for name, value in data.items():
            archive.writestr(name, value)


def run(powershell, script, *arguments):
    result = subprocess.run([powershell, '-NoLogo', '-NoProfile', '-NonInteractive', '-File', str(script), *map(str, arguments)], cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if result.returncode:
        # Only synthetic paths and compiler/packager output, never real profile/key material.
        (OWN / ('command-' + uuid.uuid4().hex + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
    return result


def validate(app_directory, powershell):
    fixture = OWN / 'fixture'; fixture.mkdir()
    for name in ('README.md', 'PROJECT_PLAN.md', 'THIRD_PARTY.md', 'global.json', 'Directory.Build.props', 'NuGet.Config', '.gitignore', '启动软件.cmd'):
        (fixture / name).write_text('synthetic source ' + name, encoding='utf-8')
    for name in ('src/main.cs', 'config/update-public-key.pem', 'engines/26.04/manifest.json', 'engines/26.04/License.txt', 'engines/26.04/7z.dll', 'src/bin/ignored.dll', 'tests/fixtures/generated/ignored.apv', '.tools/ignored.key'):
        path = fixture / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('synthetic', encoding='utf-8')
    application = OWN / 'app'; application.mkdir(); (application / 'ArchivePassword.exe').write_bytes(b'synthetic exe'); (application / '使用说明.txt').write_text('测试说明', encoding='utf-8')
    image=bytearray(128);image[:2]=b'MZ';struct.pack_into('<I',image,60,64);image[64:68]=b'PE\0\0';struct.pack_into('<H',image,68,0x8664)
    for name in ('ArchivePassword.exe','ArchiveEngineWorker.exe','engines/26.04/7z.dll','engines/26.04/7z.exe'):
        path=application/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(image)
    (application/'engines/26.04/manifest.json').write_text(json.dumps(dict(architecture='x64',protocol=1)))
    installer = OWN / 'fixture-installer.exe'; installer.write_bytes(b'synthetic installer')
    version = '1.2.3-preview.4'

    def fresh():
        release = OWN / uuid.uuid4().hex
        package(fixture, application, release, version + '-light', 'nomozip-source-' + version + '.zip')
        shutil.copyfile(installer, release / f'nomozip-{version}-light-setup-win-x64.exe'); checksum(release)
        return release

    release = fresh(); result = review(fixture, application, release, installer, version, fixture)
    check('complete Unicode lightweight and source packages review', result['passed'] and result['payloadFiles'] == 6)
    selected = {p.relative_to(fixture).as_posix() for p in source_files(fixture)}
    check('source excludes tool secrets build outputs and generated vault fixtures', not any(name.startswith('.tools/') or '/bin/' in name or '/generated/' in name or name.endswith('7z.dll') for name in selected) and 'engines/26.04/manifest.json' in selected)
    before = hashes(release)
    check('repack keeps all existing assets unchanged', rejected(lambda: package(fixture, application, release, version + '-light', 'nomozip-source-' + version + '.zip')) and hashes(release) == before)
    # Existing source asset must be detected before a new app ZIP is created.
    occupied = OWN / 'occupied'; occupied.mkdir(); (occupied / 'source.zip').write_bytes(b'old source')
    check('occupied source archive prevents partial app creation', rejected(lambda: package(fixture, application, occupied, 'test', 'source.zip')) and hashes(occupied) == {'source.zip': hashlib.sha256(b'old source').hexdigest()})
    private = fixture / 'config/test.key'; private.write_text('synthetic only')
    output = OWN / 'private-reject'
    check('private source fails preflight without release assets', rejected(lambda: package(fixture, application, output, 'test', 'source.zip')) and not output.exists()); private.unlink()
    for name in ('coreclr.dll', 'hostfxr.dll', 'NomozipModernMenu.dll', 'passwords.apv', 'certificate.pfx'):
        path = application / name; path.write_bytes(b'synthetic only')
        check('payload exclusion ' + name, rejected(lambda: application_files(application))); path.unlink()
    release = fresh(); (release / 'SHA256SUMS.txt').write_text('bad checksum', encoding='utf-8')
    check('checksum mismatch rejected', not review(fixture, application, release, installer, version)['passed'])
    release = fresh(); rewrite(release / f'nomozip-{version}-light-win-x64.zip', replace={'ArchivePassword.exe': b'tampered'}); checksum(release)
    result = review(fixture, application, release, installer, version)
    check('changed payload rejected even with recalculated checksums', result['checks']['exact checksum lines'] and not result['checks']['payload exact current files'])
    release = fresh(); rewrite(release / f'nomozip-source-{version}.zip', omit='src/main.cs'); checksum(release)
    check('missing source rejected even with valid CRC and hashes', not review(fixture, application, release, installer, version)['passed'])
    release = fresh(); rewrite(release / f'nomozip-{version}-light-win-x64.zip', extra={'passwords.apv': b'synthetic'}); checksum(release)
    check('unexpected payload file rejected', not review(fixture, application, release, installer, version)['passed'])
    release = fresh(); (release / f'nomozip-{version}-light-setup-win-x64.exe').write_bytes(b'other installer'); checksum(release)
    check('installer copy differs from built input rejected', not review(fixture, application, release, installer, version)['passed'])
    release = fresh(); published = OWN / 'published'; shutil.copytree(fixture, published); (published / 'README.md').write_text('older source', encoding='utf-8')
    check('uploaded source mismatch rejected', not review(fixture, application, release, installer, version, published)['passed'])
    release = fresh(); (release / f'nomozip-{version}-light-win-x64.zip').write_bytes(b'broken ZIP')
    try:
        review(fixture, application, release, installer, version)
        check('corrupt ZIP rejected', False)
    except zipfile.BadZipFile:
        check('corrupt ZIP rejected', True)
    release = fresh(); (release / 'extra.txt').write_text('extra')
    check('extra release asset rejected', not review(fixture, application, release, installer, version)['passed'])

    actual = OWN / 'actual-release'
    built = run(powershell, ROOT / 'scripts/build-release.ps1', '-ApplicationDirectory', app_directory, '-OutputDirectory', actual, '-Python', sys.executable)
    if built.returncode:
        raise RuntimeError('Actual release command failed; inspect owned command logs.')
    result = json.loads(Path(str(actual) + '-validation.json').read_text(encoding='utf-8'))
    check('real one-command NSIS and four-asset packaging passes', result['passed'] and result['payloadFiles'] > 0 and len(list(actual.iterdir())) == 4)
    before = hashes(actual)
    duplicate = run(powershell, ROOT / 'scripts/build-release.ps1', '-ApplicationDirectory', app_directory, '-OutputDirectory', actual, '-Python', sys.executable)
    check('real command refuses prior directory and preserves assets', duplicate.returncode != 0 and hashes(actual) == before)
    project = ET.parse(ROOT / 'src/Desktop/ArchivePassword.Desktop.csproj').getroot()
    current = project.findtext('./PropertyGroup/Version'); file_version = project.findtext('./PropertyGroup/FileVersion')
    probe = OWN / 'read-version.ps1'; probe.write_text('param([string]$Target)\n(Get-Item -LiteralPath $Target).VersionInfo | Select-Object ProductVersion,FileVersion | ConvertTo-Json\n', encoding='utf-8')
    installed = json.loads(run(powershell, probe, '-Target', actual / f'nomozip-{current}-light-setup-win-x64.exe').stdout)
    check('installer name and PE display/file versions match source', installed['ProductVersion'] == current and installed['FileVersion'] == file_version)
    wrong = OWN / 'wrong-version.exe'
    rejected_version = run(powershell, ROOT / 'scripts/build-installer.ps1', '-ApplicationDirectory', app_directory, '-OutputFile', wrong, '-Version', '99.0.0.1', '-FrameworkDependent')
    check('real installer rejects mismatched public version before writing', rejected_version.returncode != 0 and 'Installer version must match the application' in rejected_version.stdout + rejected_version.stderr and not wrong.exists())
    rebuilt = OWN / 'default-build-release'
    fixed = [ROOT / 'artifacts/app/ArchivePassword.exe', ROOT / 'artifacts/worker/ArchiveEngineWorker.exe', ROOT / 'config/update-public-key.pem']
    fixed_hashes = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in fixed}
    default = run(powershell, ROOT / 'scripts/build-release.ps1', '-OutputDirectory', rebuilt, '-Python', sys.executable)
    check('default command builds isolated lightweight app and packages it', default.returncode == 0 and json.loads(Path(str(rebuilt) + '-validation.json').read_text(encoding='utf-8'))['passed'])
    check('default build leaves launcher worker and pinned public key unchanged', all(hashlib.sha256(path.read_bytes()).hexdigest() == value for path, value in fixed_hashes.items()))
    bundled = OWN / 'bundled-app'; bundled.mkdir(); shutil.copyfile(app_directory / 'ArchivePassword.exe', bundled / 'ArchivePassword.exe'); (bundled / 'coreclr.dll').write_bytes(b'synthetic bundled runtime')
    rejected_output = OWN / 'bundled-reject'
    reject = run(powershell, ROOT / 'scripts/build-release.ps1', '-ApplicationDirectory', bundled, '-OutputDirectory', rejected_output, '-Python', sys.executable)
    check('real release command rejects bundled payload before output', reject.returncode != 0 and 'Release input review failed' in reject.stdout + reject.stderr and not rejected_output.exists())
    console_fixture = OWN / 'console-entry-app'; shutil.copytree(app_directory, console_fixture)
    (console_fixture / 'nomozip-cli.exe').unlink()
    missing_output = OWN / 'missing-console-reject'
    reject = run(powershell, ROOT / 'scripts/build-release.ps1', '-ApplicationDirectory', console_fixture, '-OutputDirectory', missing_output, '-Python', sys.executable)
    check('missing console companion fails before release assets', reject.returncode != 0 and 'Console entry is missing' in reject.stdout + reject.stderr and not missing_output.exists())
    shutil.copyfile(ROOT / '.tools/dotnet/dotnet.exe', console_fixture / 'nomozip-cli.exe')
    wrong_output = OWN / 'wrong-console-version-reject'
    reject = run(powershell, ROOT / 'scripts/build-release.ps1', '-ApplicationDirectory', console_fixture, '-OutputDirectory', wrong_output, '-Python', sys.executable)
    check('wrong console version fails before release assets', reject.returncode != 0 and 'Console entry is missing or its version differs' in reject.stdout + reject.stderr and not wrong_output.exists())
    report_inside = OWN / 'report-inside'; reject = run(powershell, ROOT / 'scripts/build-release.ps1', '-ApplicationDirectory', app_directory, '-OutputDirectory', report_inside, '-ValidationReport', report_inside / 'report.json', '-Python', sys.executable)
    check('review report cannot pollute four-asset directory', reject.returncode != 0 and 'Keep the review report outside' in reject.stdout + reject.stderr and not report_inside.exists())


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--app-directory', type=Path, default=ROOT / 'artifacts/light-app')
    parser.add_argument('--powershell', default='pwsh')
    args = parser.parse_args()
    OWN = ROOT / 'artifacts/release-maintenance' / uuid.uuid4().hex; OWN.mkdir(parents=True)
    try:
        validate(args.app_directory.resolve(), args.powershell)
    except Exception as error:
        checks['validation exception ' + str(error)] = False
    result = dict(passed=sum(checks.values()), total=len(checks), cases=[dict(name=name, passed=value) for name, value in checks.items()],
                  fixtureDirectory=str(OWN), scope='Owned synthetic inputs and actual build/NSIS/package commands; no installation, publishing or certificate changes.')
    (ROOT / 'artifacts/release-maintenance-validation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('passed', 'total', 'fixtureDirectory')}))
    if not checks or not all(checks.values()):
        raise SystemExit(1)
