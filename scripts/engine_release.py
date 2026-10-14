"""Prepare an isolated official-engine candidate and review its bound evidence."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
REPORTS = ('engine-validation.json', 'compression-validation.json', 'selection-validation.json', 'encoding-validation.json', 'pause-validation.json', 'archive-editing-validation.json', 'volume-validation.json')
PROJECTS = ('CompressionValidation', 'SelectionValidation', 'EncodingValidation', 'PauseValidation', 'ArchiveEditingValidation', 'VolumeValidation')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def official_asset(release):
    version = release['tag_name']
    if not re.fullmatch(r'\d{2}\.\d{2}', version) or release['prerelease'] or release.get('draft', False):
        raise ValueError('An official stable release is required.')
    name = f'7z{version.replace(".", "")}-x64.exe'
    assets = [asset for asset in release['assets'] if asset['name'] == name]
    source = f'https://github.com/ip7z/7zip/releases/download/{version}/{name}'
    if len(assets) != 1 or assets[0]['browser_download_url'] != source or not re.fullmatch(r'sha256:[0-9a-f]{64}', assets[0].get('digest') or ''):
        raise ValueError('Official x64 asset URL and SHA-256 are required.')
    return version, source, assets[0]['digest'][7:]


def cases(report):
    values = report.get('cases')
    if values is None:
        values = [dict(name=name, passed=value) for name, value in report['checks'].items()]
    if report.get('total') != len(values) or not values or any(value['passed'] is not True for value in values):
        raise ValueError('Compatibility checks are incomplete or failed.')
    passed = report.get('passed')
    if passed is not True and (isinstance(passed, bool) or passed != len(values)):
        raise ValueError('Compatibility checks did not pass.')
    return values


def bound_files(version):
    return [f'workspace/engines/{version}/{name}' for name in ('7z.dll', '7z.exe', 'License.txt', 'manifest.json')] + [
        'workspace/artifacts/worker/ArchiveEngineWorker.exe', 'public-key.pem'] + ['workspace/artifacts/' + name for name in REPORTS]


def validate_reports(workspace, version):
    dll_hash = sha(workspace / 'engines' / version / '7z.dll')
    totals = {}
    for name, minimum in zip(REPORTS, (20, 17, 20, 20, 4, 20, 20)):
        report = load(workspace / 'artifacts' / name)
        values = cases(report)
        if sum(value['name'].startswith(version + ' ') for value in values) < minimum:
            raise ValueError('Required target-version coverage is missing: ' + name)
        if name in ('engine-validation.json', 'compression-validation.json', 'pause-validation.json', 'archive-editing-validation.json', 'volume-validation.json') and report['engineHashes'][version].lower() != dll_hash:
            raise ValueError('Compatibility report belongs to another engine binary.')
        totals[name] = len(values)
    return totals


def review(candidate, public_key, worker, repository=None):
    record = load(candidate / 'candidate-review.json')
    version = record['version']
    if record['schema'] != 1 or record['status'] != 'ready-for-signing' or not re.fullmatch(r'\d{2}\.\d{2}', version):
        raise ValueError('Candidate is not ready.')
    if repository is not None and record['repository'] != repository:
        raise ValueError('Candidate repository differs from the configured update channel.')
    if set(record['hashes']) != set(bound_files(version)) or any(sha(candidate / name) != digest for name, digest in record['hashes'].items()):
        raise ValueError('Prepared candidate or its evidence changed.')
    if sha(public_key) != record['hashes']['public-key.pem'] or sha(worker) != record['hashes']['workspace/artifacts/worker/ArchiveEngineWorker.exe']:
        raise ValueError('Product public key or worker differs from the reviewed candidate.')
    manifest = load(candidate / 'workspace/engines' / version / 'manifest.json')
    if manifest['version'] != version or manifest['protocol'] != 1 or manifest['architecture'] != 'x64' or manifest['dllSha256'].lower() != sha(candidate / 'workspace/engines' / version / '7z.dll'):
        raise ValueError('Candidate manifest is invalid.')
    validate_reports(candidate / 'workspace', version)
    return record


def command(arguments, log):
    with log.open('w', encoding='utf-8') as output:
        process = subprocess.run(list(map(str, arguments)), stdout=output, stderr=subprocess.STDOUT)
    if process.returncode:
        raise RuntimeError('Compatibility command failed; candidate remains disabled. See ' + str(log))


def prepare(source_root, tools_root, output, version=None, engine_directory=None, dotnet=None, worker=None):
    if output.exists():
        raise ValueError('Keep existing candidates immutable; choose a new output directory.')
    dotnet = dotnet or tools_root / '.tools/dotnet/dotnet.exe'
    worker = worker or tools_root / 'artifacts/worker/ArchiveEngineWorker.exe'
    repository = load(source_root / 'config/update-channel.json')['repository']
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
        raise ValueError('Update repository is invalid.')
    if engine_directory is not None:
        manifest = load(engine_directory / 'manifest.json')
        version = version or manifest['version']
        source = f'https://github.com/ip7z/7zip/releases/download/{version}/7z{version.replace(".", "")}-x64.exe'
        if not re.fullmatch(r'\d{2}\.\d{2}', version) or manifest['version'] != version or manifest['architecture'] != 'x64' or manifest['source'] != source or manifest['dllSha256'].lower() != sha(engine_directory / '7z.dll'):
            raise ValueError('Prepared local engine manifest and binary do not match.')
        origin = dict(mode='prepared-local', source=source)
    else:
        endpoint = 'https://api.github.com/repos/ip7z/7zip/releases/' + ('tags/' + version if version else 'latest')
        with urllib.request.urlopen(endpoint, timeout=30) as response:
            metadata = json.load(response)
        actual, source, expected_hash = official_asset(metadata)
        if version and version != actual:
            raise ValueError('Official release version mismatch.')
        version = actual; origin = dict(mode='official-download', source=source, installerSha256=expected_hash)
    output.mkdir(parents=True)
    workspace = output / 'workspace'; (workspace / 'artifacts/worker').mkdir(parents=True)
    logs = output / 'logs'; logs.mkdir()
    shutil.copyfile(worker, workspace / 'artifacts/worker/ArchiveEngineWorker.exe')
    shutil.copyfile(source_root / 'config/update-public-key.pem', output / 'public-key.pem')
    for baseline in ('26.04', '26.03'):
        directory = workspace / 'engines' / baseline; directory.mkdir(parents=True)
        for name in ('7z.dll', '7z.exe', 'License.txt', 'manifest.json'):
            shutil.copyfile(tools_root / 'engines' / baseline / name, directory / name)
    target = workspace / 'engines' / version; target.mkdir(parents=True, exist_ok=True)
    (target / 'manifest.json').write_text(json.dumps(dict(version=version, architecture='x64', protocol=0, source=source,
        dllSha256=sha(target / '7z.dll') if (target / '7z.dll').exists() else '0' * 64)), encoding='utf-8')
    if engine_directory is None:
        installer = output / 'official-installer.exe'
        with urllib.request.urlopen(source, timeout=60) as response, installer.open('xb') as destination:
            shutil.copyfileobj(response, destination)
        if sha(installer) != expected_hash:
            raise ValueError('Official download checksum mismatch.')
        command([tools_root / 'engines/26.04/7z.exe', 'x', installer, '-o' + str(target), '-y', '7z.dll', '7z.exe', 'License.txt'], logs / 'official-extraction.log')
    else:
        for name in ('7z.dll', '7z.exe', 'License.txt'):
            shutil.copyfile(engine_directory / name, target / name)
    manifest = dict(version=version, architecture='x64', protocol=0, source=source, dllSha256=sha(target / '7z.dll'))
    (target / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    seeds = workspace / 'tests/fixtures/generated'; seeds.mkdir(parents=True)
    for file in (tools_root / 'tests/fixtures/generated').glob('*'):
        if file.is_file() and file.stat().st_size < 4 * 1024 * 1024:
            shutil.copyfile(file, seeds / file.name)
    versions = ','.join(dict.fromkeys((version, '26.04', '26.03')))
    command([sys.executable, tools_root / 'tests/engine_validation.py', '--root', workspace, '--versions', versions, '--dotnet', dotnet,
             '--smoke', tools_root / 'tests/EngineSmoke/bin/Debug/net10.0-windows/EngineSmoke.dll'], logs / 'engine.log')
    for project in PROJECTS:
        command([dotnet, tools_root / 'tests' / project / 'bin/Debug/net10.0-windows' / (project + '.dll'), workspace, versions], logs / (project + '.log'))
    totals = validate_reports(workspace, version)
    manifest['protocol'] = 1
    (target / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    record = dict(schema=1, status='ready-for-signing', version=version, repository=repository, origin=origin, totals=totals,
                  hashes={name: sha(output / name) for name in bound_files(version)})
    (output / 'candidate-review.json').write_text(json.dumps(record, indent=2), encoding='utf-8')
    return review(output, source_root / 'config/update-public-key.pem', worker, repository)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('prepare', 'review'))
    parser.add_argument('--candidate', type=Path, required=True)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--tools-root', type=Path, default=ROOT)
    parser.add_argument('--version')
    parser.add_argument('--engine-directory', type=Path)
    parser.add_argument('--dotnet', type=Path)
    parser.add_argument('--worker', type=Path)
    args = parser.parse_args()
    if args.mode == 'prepare':
        result = prepare(args.source_root.resolve(), args.tools_root.resolve(), args.candidate.resolve(), args.version, args.engine_directory, args.dotnet, args.worker)
    else:
        result = review(args.candidate.resolve(), args.source_root / 'config/update-public-key.pem', args.worker or args.tools_root / 'artifacts/worker/ArchiveEngineWorker.exe', load(args.source_root / 'config/update-channel.json')['repository'])
    print(json.dumps(dict(status=result['status'], version=result['version'], totals=result['totals'])))
