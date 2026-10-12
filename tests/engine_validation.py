"""Real-engine integration checks. All passwords and files are synthetic fixtures."""
import binascii
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / "tests/fixtures/generated"
WORKER = ROOT / "artifacts/worker/ArchiveEngineWorker.exe"
PASSWORD = "测试密碼 ' $ & 空格"
VERSIONS = ("26.04", "26.03")
REPORT = []


def request(version, operation, archive=None, output=None, password=None):
    command = [str(WORKER), str(ROOT / "engines" / version / "7z.dll")]
    data = dict(protocol=1, operation=operation, archivePath=str(archive), outputPath=str(output), password=password)
    process = subprocess.run(command, input=json.dumps(data, ensure_ascii=False) + "\n", encoding="utf-8", capture_output=True, timeout=60)
    messages = [json.loads(line) for line in process.stdout.splitlines()]
    assert messages and messages[-1]["kind"] == "result", (process.returncode, process.stderr)
    return messages[-1]


def check(name, condition, details=None):
    REPORT.append(dict(name=name, passed=bool(condition), details=details))
    print(("PASS " if condition else "FAIL ") + name, flush=True)
    if not condition:
        print(json.dumps(details, ensure_ascii=False), flush=True)


def create_archive(name, kind, source, encrypted=False, headers=False, extra=(), password=PASSWORD):
    path = FIXTURES / name
    if not path.exists() and not ("-v500b" in extra and Path(str(path) + ".001").exists()):
        command = [str(ROOT / "engines/26.04/7z.exe"), "a", "-y", "-t" + kind, str(path), str(source), *extra]
        if encrypted:
            command += ["-p" + password]
        if headers:
            command += ["-mhe=on"]
        subprocess.run(command, check=True, capture_output=True)
    return path


def rar_fixture(name):
    path = FIXTURES / name
    if not path.exists():
        url = "https://raw.githubusercontent.com/libarchive/libarchive/master/libarchive/test/" + name + ".uu"
        with urllib.request.urlopen(url, timeout=30) as response:
            data = response.read().decode("ascii").splitlines()
        payload = bytearray()
        begin = False
        for line in data:
            if line.startswith("begin "):
                begin = True
            elif begin and line == "end":
                break
            elif begin and line:
                payload += binascii.a2b_uu(line)
        path.write_bytes(payload)
    return path


def main():
    FIXTURES.mkdir(parents=True, exist_ok=True)
    source = FIXTURES / "中文示例.txt"
    source.write_text("Hello archive! 中文内容。\n" * 80, encoding="utf-8")
    fixtures = [
        (create_archive("plain.7z", "7z", source), None),
        (create_archive("plain.zip", "zip", source), None),
        (create_archive("encrypted.7z", "7z", source, True), PASSWORD),
        (create_archive("encrypted-header.7z", "7z", source, True, True), PASSWORD),
        (create_archive("encrypted-legacy.zip", "zip", source, True, password="Fixture-Password-123"), "Fixture-Password-123"),
        (create_archive("encrypted-aes-ascii.zip", "zip", source, True, extra=("-mem=AES256",), password="AES ' $ & spaces"), "AES ' $ & spaces"),
    ]
    rar = rar_fixture("test_read_format_rar.rar")
    encrypted_rar = rar_fixture("test_read_format_rar5_encrypted_filenames.rar")
    multi_password_rar = rar_fixture("test_read_format_rar5_encrypted.rar")
    hostile = FIXTURES / "traversal.zip"
    with zipfile.ZipFile(hostile, "w") as archive:
        archive.writestr("../escaped.txt", "should never escape")
    corrupt = FIXTURES / "corrupt.7z"
    corrupt.write_bytes(b"7z\xbc\xaf\x27\x1c" + bytes(60))
    volume = create_archive("split.7z", "7z", source, extra=("-v500b", "-mx=0"))
    missing = FIXTURES / "missing-volume"
    missing.mkdir(exist_ok=True)
    first = missing / "split.7z.001"
    first.write_bytes((FIXTURES / "split.7z.001").read_bytes())

    for version in VERSIONS:
        caps = request(version, "capabilities")
        check(version + " capabilities", caps["status"] == "success" and any(value["name"] == "Rar5" for value in caps["formats"]))
        for index, (archive, password) in enumerate(fixtures):
            listed = request(version, "list", archive, password=password)
            check(version + " lists " + archive.name, listed["status"] == "success"
                  and any(item["path"] == source.name and item["size"] == source.stat().st_size
                          and item["encrypted"] == (password is not None) for item in listed.get("items", [])), listed)
            if password is not None:
                wrong = request(version, "test", archive, password="definitely-wrong")
                check(version + " rejects wrong " + archive.name, wrong["status"] == "error" and wrong.get("code") in ("wrong_password", "password_or_corrupt"), wrong)
                required = request(version, "test", archive)
                check(version + " requests password " + archive.name, required.get("code") == "password_required", required)
            tested = request(version, "test", archive, password=password)
            check(version + " tests " + archive.name, tested["status"] == "success", tested)
            output = FIXTURES / "outputs" / (version + "-" + str(index) + "-" + str(time.time_ns()))
            extracted = request(version, "extract", archive, output, password)
            files = list(output.rglob("*.txt")) if output.exists() else []
            check(version + " extracts " + archive.name, extracted["status"] == "success" and len(files) == 1 and files[0].read_bytes() == source.read_bytes(), extracted)
        rar_output = FIXTURES / "outputs" / (version + "-rar-" + str(time.time_ns()))
        rar_result = request(version, "extract", rar, rar_output)
        check(version + " extracts real RAR", rar_result["status"] == "success" and any(rar_output.rglob("test.txt")), rar_result)
        listed_rar = request(version, "list", rar)
        check(version + " lists real RAR", listed_rar["status"] == "success" and len(listed_rar.get("items", [])) > 0, listed_rar)
        encrypted_output = FIXTURES / "outputs" / (version + "-rar5-" + str(time.time_ns()))
        rar5_result = request(version, "extract", encrypted_rar, encrypted_output, "password")
        check(version + " extracts encrypted RAR5", rar5_result["status"] == "success" and (encrypted_output / "a.txt").read_bytes() == b"This is from a.txt", rar5_result)
        listed_rar5 = request(version, "list", encrypted_rar, password="password")
        check(version + " lists encrypted RAR5", listed_rar5["status"] == "success" and any(item["path"] == "a.txt" for item in listed_rar5.get("items", [])), listed_rar5)
        locked_listing = request(version, "list", fixtures[3][0])
        check(version + " listing requests header password", locked_listing.get("code") == "password_required" and not locked_listing.get("items"), locked_listing)
        wrong_listing = request(version, "list", fixtures[3][0], password="wrong")
        check(version + " listing rejects wrong header password", wrong_listing.get("code") in ("wrong_password", "password_or_corrupt") and not wrong_listing.get("items"), wrong_listing)
        mixed = request(version, "test", multi_password_rar, password="password")
        check(version + " reports mixed-password partial failure", mixed["status"] == "error" and mixed.get("successfulItems", 0) > 0 and len(mixed.get("failures", [])) > 0, mixed)
        mixed_paths = {item["index"]: item["path"] for item in request(version, "list", multi_password_rar, password="password").get("items", [])}
        check(version + " partial failures identify actual archive paths", all(failure.get("path") and mixed_paths.get(failure["index"]) == failure["path"] for failure in mixed.get("failures", [])), mixed)
        unsafe_output = FIXTURES / "outputs" / (version + "-unsafe-" + str(time.time_ns()))
        unsafe = request(version, "extract", hostile, unsafe_output)
        check(version + " blocks archive traversal", unsafe.get("code") == "unsafe_path" and not (unsafe_output.parent / "escaped.txt").exists(), unsafe)
        damaged = request(version, "test", corrupt)
        check(version + " rejects corrupt archive without success", damaged["status"] == "error" and not damaged.get("code", "").startswith("wrong_password"), damaged)
        damaged_listing = request(version, "list", corrupt)
        check(version + " rejects corrupt listing", damaged_listing["status"] == "error" and not damaged_listing.get("items"), damaged_listing)
        absent = request(version, "test", first)
        check(version + " reports missing volume", absent.get("code") == "missing_volume", absent)

    # Cancellation is tested on real data, not an injected fake engine result.
    large_source = FIXTURES / "large.bin"
    if not large_source.exists():
        with large_source.open("wb") as output:
            block = os.urandom(1024 * 1024)
            for _ in range(64):
                output.write(block)
    large = create_archive("large.7z", "7z", large_source, extra=("-mx=7",))
    process = subprocess.Popen([str(WORKER), str(ROOT / "engines/26.04/7z.dll")], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
    process.stdin.write(json.dumps(dict(protocol=1, operation="test", archivePath=str(large))) + "\n")
    process.stdin.flush()
    first_progress = process.stdout.readline()
    process.stdin.write('{"protocol":1,"operation":"cancel"}\n')
    process.stdin.flush()
    tail, stderr = process.communicate(timeout=10)
    events = [json.loads(line) for line in (first_progress + tail).splitlines()]
    check("26.04 cooperative cancellation", events[-1].get("status") == "cancelled", events[-1])

    dotnet = ROOT / ".tools/dotnet/dotnet.exe"
    smoke = ROOT / "tests/EngineSmoke/bin/Debug/net10.0-windows/EngineSmoke.dll"
    smoke_output = FIXTURES / "outputs" / ("managed-" + str(time.time_ns()))
    managed = subprocess.run([str(dotnet), str(smoke), str(WORKER), str(ROOT / "engines/26.04/7z.dll"), str(fixtures[3][0]), str(smoke_output)], input=PASSWORD + "\n", encoding="utf-8", capture_output=True, timeout=30)
    check("C# client pipe round trip", managed.returncode == 0 and (smoke_output / source.name).read_bytes() == source.read_bytes(), managed.stdout[-1000:])
    managed_cancel = subprocess.run([str(dotnet), str(smoke), str(WORKER), str(ROOT / "engines/26.04/7z.dll"), str(large), str(smoke_output), "cancel"], encoding="utf-8", capture_output=True, timeout=15)
    check("C# client cooperative cancellation", managed_cancel.returncode == 0 and json.loads(managed_cancel.stdout)["Status"] == "cancelled", managed_cancel.stdout)
    report = ROOT / "artifacts/engine-validation.json"
    import hashlib
    engine_hashes = {version: hashlib.sha256((ROOT / "engines" / version / "7z.dll").read_bytes()).hexdigest() for version in VERSIONS}
    report.write_text(json.dumps(dict(passed=sum(item["passed"] for item in REPORT), total=len(REPORT), engineHashes=engine_hashes, cases=REPORT), ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{sum(item['passed'] for item in REPORT)}/{len(REPORT)} passed. Report: {report}")
    return 0 if all(item["passed"] for item in REPORT) else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--versions", default="26.04,26.03")
    options = parser.parse_args()
    VERSIONS = tuple(options.versions.split(","))
    raise SystemExit(main())
