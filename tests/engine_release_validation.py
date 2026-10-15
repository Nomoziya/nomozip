"""Real candidate/signature pipeline; isolated keys and a local GitHub CLI fixture only."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from engine_release import official_asset, bound_files, review, prepare, REPORTS

checks = {}


def check(name, value):
    checks[name] = bool(value)


def rejected(action):
    try:
        action()
    except (ValueError, RuntimeError, KeyError):
        return True
    return False


def call(arguments):
    result = subprocess.run(list(map(str, arguments)), cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
    (OWN / ('command-' + uuid.uuid4().hex + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
    return result


def run(candidate, mode, *arguments):
    return call([PS, '-NoLogo', '-NoProfile', '-NonInteractive', '-File', HARNESS / 'scripts/maintain-engine-release.ps1',
        '-Mode', mode, '-CandidateDirectory', candidate, '-ToolsRoot', ROOT, '-Dotnet', DOTNET, '-Python', sys.executable,
        '-GitHubCli', HARNESS / 'gh-fixture.ps1', '-Architecture', ARCH, *arguments])


def clone(candidate):
    destination = HARNESS / 'artifacts' / uuid.uuid4().hex[:8]
    for name in bound_files('26.04') + ['candidate-review.json', 'signed-review.json', 'release-notes.md', f'assets/ArchivePassword-engine-26.04-win-{ARCH}.zip', 'assets/SHA256SUMS.txt']:
        path = destination / name; path.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(candidate / name, path)
    return destination


def reset(candidate, **flags):
    value = dict(architecture=ARCH,tag=TAG,candidate=str(candidate), exists=False, published=False, corrupt=False, extra=False, apiFailure=False, wrongLatest=False)
    value.update(flags); (HARNESS / 'gh-state.json').write_text(json.dumps(value), encoding='utf-8')
    (HARNESS / 'gh-calls.jsonl').write_text('', encoding='utf-8')


def remove_test_assets(candidate):
    for name in (f'ArchivePassword-engine-26.04-win-{ARCH}.zip', 'SHA256SUMS.txt'):
        (candidate / 'assets' / name).unlink()
    (candidate / 'assets').rmdir()
    (candidate / 'signed-review.json').unlink()


def calls():
    return [json.loads(line) for line in (HARNESS / 'gh-calls.jsonl').read_text(encoding='utf-8').splitlines()]


def validate():
    config = HARNESS / 'config'; config.mkdir(parents=True)
    (config / 'update-channel.json').write_text(json.dumps(dict(repository='Synthetic/nomozip-tests')), encoding='utf-8')
    scripts = HARNESS / 'scripts'; scripts.mkdir()
    for name in ('engine_release.py', 'maintain-engine-release.ps1'):
        shutil.copyfile(ROOT / 'scripts' / name, scripts / name)
    key = OWN / 'test-private.pem'; public = config / 'update-public-key.pem'
    generated = call([DOTNET, PACKAGER, 'keygen', key, public]); check('isolated RSA identity created', generated.returncode == 0)
    fixed = [ROOT / 'config/update-public-key.pem', ROOT / 'artifacts/worker/ArchiveEngineWorker.exe', ROOT / 'artifacts/app/ArchivePassword.exe', ENGINE / 'manifest.json']
    fixed_hashes = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in fixed}
    metadata = dict(tag_name='26.04', prerelease=False, assets=[dict(name=f'7z2604-{ARCH}.exe', browser_download_url=f'https://github.com/ip7z/7zip/releases/download/26.04/7z2604-{ARCH}.exe', digest='sha256:' + 'a' * 64)])
    check('stable official asset exact URL and digest accepted', official_asset(metadata,ARCH)[0] == '26.04')
    for name, changed in [('prerelease', dict(metadata, prerelease=True)), ('foreign source', dict(metadata, assets=[dict(metadata['assets'][0], browser_download_url='https://example.com/file.exe')])), ('missing digest', dict(metadata, assets=[dict(metadata['assets'][0], digest=None)]))]:
        check('upstream rejection ' + name, rejected(lambda: official_asset(changed,ARCH)))
    candidate = HARNESS / 'artifacts/c'
    prepared = run(candidate, 'Prepare', '-Version', '26.04', '-EngineDirectory', ENGINE)
    if prepared.returncode:
        raise RuntimeError('Actual Prepare command failed; inspect owned command logs.')
    record = review(candidate, public, WORKER, 'Synthetic/nomozip-tests')
    check('real twelve-matrix candidate prepared in isolated workspace', record['status'] == 'ready-for-signing' and len(record['totals']) == 12 and set(record['totals']) == set(REPORTS))
    check('matrix versions deduplicated and extraction baseline still complete', record['totals']['engine-validation.json'] == 101)
    duplicate = run(candidate, 'Prepare', '-Version', '26.04', '-EngineDirectory', ENGINE)
    check('existing prepared candidate cannot be overwritten', duplicate.returncode != 0 and 'Keep prior candidates immutable' in duplicate.stdout + duplicate.stderr)
    signed = run(candidate, 'Sign', '-PrivateKey', key)
    if signed.returncode:
        raise RuntimeError('Actual Sign command failed; inspect owned command logs.')
    package = candidate / f'assets/ArchivePassword-engine-26.04-win-{ARCH}.zip'
    check('real signed package accepted by client verifier', call([DOTNET, PACKAGER, 'inspect', package, public, ARCH]).returncode == 0)
    with zipfile.ZipFile(package) as archive:
        report = json.loads(archive.read('engine/compatibility-report.json'))
        check('signed compatibility includes selection encoding pause editing volumes SFX formats architecture and worker binding', set(report['maintenance']) == {'workerSha256', 'selection-validation.json', 'encoding-validation.json', 'pause-validation.json', 'archive-editing-validation.json', 'volume-validation.json','sfx-validation.json','format-validation.json','architecture-validation.json','conversion-validation.json','preview-validation.json'})
        module=__import__('base64').b64decode(report['sfx']['moduleBase64'])
        check('official module is inside existing signed compatibility payload',report['sfx']['version']=='26.04' and hashlib.sha256(module).hexdigest()==report['sfx']['sha256']==hashlib.sha256((ENGINE/'7z.sfx').read_bytes()).hexdigest())
        check('package retains legacy five-entry client format', len(archive.namelist()) == 5 and archive.testzip() is None)
    second = run(candidate, 'Sign', '-PrivateKey', key)
    check('signed assets never overwritten', second.returncode != 0 and 'Keep signed candidate assets immutable' in second.stdout + second.stderr)
    for relative, name in [('workspace/engines/26.04/7z.dll', 'changed DLL'), ('workspace/artifacts/pause-validation.json', 'changed pause evidence'), ('workspace/artifacts/archive-editing-validation.json', 'changed editing evidence'), ('workspace/artifacts/volume-validation.json', 'changed volume evidence'),('workspace/engines/26.04/7z.sfx','changed SFX module'),('workspace/artifacts/sfx-validation.json','changed SFX evidence'),('workspace/artifacts/format-validation.json','changed format evidence'),('workspace/artifacts/architecture-validation.json','changed architecture evidence'),('workspace/artifacts/conversion-validation.json','changed conversion evidence'),('workspace/artifacts/preview-validation.json','changed preview evidence')]:
        altered = clone(candidate); path = altered / relative; path.write_bytes(path.read_bytes() + b'changed')
        check('review rejects ' + name, rejected(lambda: review(altered, public, WORKER, 'Synthetic/nomozip-tests')))
        refused = run(altered, 'Sign', '-PrivateKey', OWN / 'absent-private.pem')
        check(name + ' fails before private key use', refused.returncode != 0 and 'Prepared candidate review failed' in refused.stdout + refused.stderr)
    altered = clone(candidate); manifest = altered / 'candidate-review.json'; value = json.loads(manifest.read_text()); value['repository'] = 'Other/repo'; manifest.write_text(json.dumps(value))
    check('configured channel mismatch rejected', rejected(lambda: review(altered, public, WORKER, 'Synthetic/nomozip-tests')))
    check('different trusted public key rejected', rejected(lambda: review(candidate, ROOT / 'config/update-public-key.pem', WORKER, 'Synthetic/nomozip-tests')))
    # A new bound snapshot with all-true cases but no target pause coverage must still be refused by the signer itself.
    altered = clone(candidate); pause = altered / 'workspace/artifacts/pause-validation.json'; value = json.loads(pause.read_text()); value['checks'] = {'unrelated': True}; value['total'] = 1; value['passed'] = True; pause.write_text(json.dumps(value))
    snapshot = altered / 'candidate-review.json'; value = json.loads(snapshot.read_text()); value['hashes']['workspace/artifacts/pause-validation.json'] = hashlib.sha256(pause.read_bytes()).hexdigest(); snapshot.write_text(json.dumps(value))
    refused_path = altered / 'refused.zip'
    refusal = call([DOTNET, PACKAGER, 'pack-maintenance', altered / 'workspace/engines/26.04', '26.04', altered / 'workspace/artifacts/engine-validation.json', key, refused_path, public, altered / 'workspace/artifacts/compression-validation.json', snapshot])
    check('standalone signer refuses incomplete supplemental coverage', refusal.returncode != 0 and not refused_path.exists())
    # Real DPAPI protection of this test-only key, with plaintext temporary file cleanup.
    protected = OWN / 'test-private.dpapi'; protect = OWN / 'protect-test-key.ps1'
    protect.write_text('param([string]$InputKey,[string]$OutputKey)\n$taskBytes=[IO.File]::ReadAllBytes($InputKey)\n[IO.File]::WriteAllBytes($OutputKey,[Security.Cryptography.ProtectedData]::Protect($taskBytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))\n[Array]::Clear($taskBytes,0,$taskBytes.Length)\n', encoding='utf-8')
    if call([PS, '-NoProfile', '-File', protect, '-InputKey', key, '-OutputKey', protected]).returncode:
        raise RuntimeError('Synthetic DPAPI test identity could not be protected.')
    protected_candidate = clone(candidate); remove_test_assets(protected_candidate)
    encrypted = run(protected_candidate, 'Sign', '-ProtectedPrivateKey', protected)
    check('DPAPI test key signs and temporary plaintext is removed', encrypted.returncode == 0 and not list((HARNESS / 'artifacts/signing-temporary').glob('*.pem')))
    wrong_key = OWN / 'wrong-private.pem'; wrong_public = OWN / 'wrong-public.pem'
    call([DOTNET, PACKAGER, 'keygen', wrong_key, wrong_public])
    wrong_candidate = clone(candidate); remove_test_assets(wrong_candidate)
    check('wrong RSA identity produces no update package', run(wrong_candidate, 'Sign', '-PrivateKey', wrong_key).returncode != 0 and not (wrong_candidate / 'assets').exists())
    wrong_key.unlink()
    broken = OWN / 'invalid-worker.exe'; broken.write_bytes(b'not an executable'); failed = HARNESS / 'artifacts/failed-matrix'
    check('real worker failure stops candidate enablement', rejected(lambda: prepare(HARNESS, ROOT, failed, '26.04', ENGINE, DOTNET, broken, architecture=ARCH)) and json.loads((failed / 'workspace/engines/26.04/manifest.json').read_text())['protocol'] == 0 and not (failed / 'candidate-review.json').exists())
    reset(candidate)
    draft = run(candidate, 'Draft', '-TargetCommit', 'a' * 40)
    check('offline CLI draft uploads only exact signed assets', draft.returncode == 0 and (candidate / 'draft-review.json').exists())
    created = [args for args in calls() if args[:2] == ['release', 'create']]
    check('draft binds full commit and remains outside latest channel', len(created) == 1 and '--draft' in created[0] and '--latest=false' in created[0] and 'a' * 40 in created[0] and '--notes-file' in created[0] and '--clobber' not in created[0])
    reset(candidate, exists=True)
    refusal = run(candidate, 'Draft', '-TargetCommit', 'a' * 40)
    check('existing release tag triggers no remote mutation', refusal.returncode != 0 and not any(args[:2] == ['release', 'create'] for args in calls()))
    reset(candidate, exists=True)
    refusal = run(candidate, 'Publish')
    check('publish requires explicit approval before any GitHub request', refusal.returncode != 0 and not calls())
    for flags, name in [(dict(exists=True, corrupt=True), 'remote byte mismatch'), (dict(exists=True, extra=True), 'unexpected remote asset'), (dict(apiFailure=True), 'API failure')]:
        reset(candidate, **flags); refusal = run(candidate, 'Publish', '-ApprovePublish')
        check('publish refusal ' + name, refusal.returncode != 0 and not any(args[:2] == ['release', 'edit'] for args in calls()))
    reset(candidate, exists=True)
    published = run(candidate, 'Publish', '-ApprovePublish')
    check('offline approved publish switches stable engine channel', published.returncode == 0 and json.loads((candidate / 'publish-review.json').read_text(encoding='utf-8-sig'))['status'] == 'published')
    edits = [args for args in calls() if args[:2] == ['release', 'edit']]
    check('publish targets engine tag with stable latest flags', len(edits) == 1 and edits[0][2] == TAG and ('--latest' if ARCH=='x64' else '--latest=false') in edits[0] and '--draft=false' in edits[0] and '--prerelease=false' in edits[0])
    check('remote assets downloaded again after publication', len([args for args in calls() if args[:2] == ['release', 'download']]) == 2)
    reset(candidate, exists=True, wrongLatest=True)
    check('wrong latest channel is reported as publication failure', (run(candidate, 'Publish', '-ApprovePublish').returncode != 0) == (ARCH=='x64'))
    check('main program worker pinned key and engine manifest unchanged', all(hashlib.sha256(path.read_bytes()).hexdigest() == value for path, value in fixed_hashes.items()))
    return candidate


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--powershell', default='pwsh'); parser.add_argument('--architecture',choices=('x64','arm64'),default='x64'); args = parser.parse_args(); PS = args.powershell
    ARCH=args.architecture;TAG='engine-26.04'+('-arm64' if ARCH=='arm64' else '');ENGINE=ROOT/('engines/arm64/26.04' if ARCH=='arm64' else 'engines/26.04')
    DOTNET = ROOT / '.tools/dotnet/dotnet.exe'; WORKER = ROOT / 'artifacts/worker/ArchiveEngineWorker.exe'; PACKAGER = ROOT / 'tools/UpdatePackager/bin/Debug/net10.0-windows/UpdatePackager.dll'
    OWN = ROOT / 'artifacts' / ('er-' + uuid.uuid4().hex[:8]); OWN.mkdir(parents=True); HARNESS = OWN / 's'; HARNESS.mkdir()
    shutil.copyfile(ROOT / 'tests/fixtures/engine-release-gh.ps1', HARNESS / 'gh-fixture.ps1')
    candidate = None
    try:
        candidate = validate()
    except Exception as error:
        checks['validation exception ' + str(error)] = False
    finally:
        for name in ('test-private.pem', 'wrong-private.pem', 'test-private.dpapi'):
            key_path = OWN / name
            if key_path.exists():
                key_path.unlink()
    result = dict(passed=sum(checks.values()), total=len(checks), cases=[dict(name=name, passed=value) for name, value in checks.items()],
                  fixtureDirectory=str(OWN), candidate=str(candidate) if candidate else None, productionSigningOrPublishing=False,
                  scope='Real engine matrices, RSA/DPAPI signing and client verification; local GitHub fixture only, no production key or remote write.')
    (ROOT / 'artifacts/engine-release-validation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('passed', 'total', 'fixtureDirectory')}))
    if not checks or not all(checks.values()):
        raise SystemExit(1)
