#!/usr/bin/env python3
"""Build isolated migration Mods sets from verified, published GitHub assets.

Only the generate command writes fixtures. The inspect command reads an installed
Mods tree and prints a report; neither command installs mods into the game.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import zipfile


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT = REPO_ROOT / "build" / "migration-e2e"
SPECS = {
    "ipc": ("KGH1113/adofai-ipc", "AdofaiIpc", "v0.4.1"),
    "helper": ("KGH1113/TUFHelperLite", "TUFHelperLite", "v0.1.9"),
    "replay": ("KGH1113/TUFReplay", "TUFReplay", "v0.2.0-beta.4"),
}
SDK_FILES = (
    "AdofaiIpc.Contracts.dll",
    "AdofaiIpc.Loader.dll",
    "ipc/AdofaiIpc.Runtime.dll",
    "ipc/manifest.json",
)
SENTINEL = b"ADOFAI SDK migration preservation fixture v1\n"


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def gh(*args):
    return subprocess.check_output(["rtk", "gh", *args], text=True)


def safe_output(path):
    path = Path(path).expanduser().resolve()
    if any(part.lower() == "mods" for part in path.parts):
        raise ValueError("Output must be a separate fixture directory outside every Mods folder.")
    if "steamapps" in [part.lower() for part in path.parts]:
        raise ValueError("Output must be outside the Steam game installation.")
    if path == REPO_ROOT or path in REPO_ROOT.parents:
        raise ValueError("Output must be a child fixture directory, not a repository or parent.")
    return path


def fetch_release(out, key, kind, tag):
    repo, mod_id, _ = SPECS[kind]
    metadata = json.loads(gh("api", f"repos/{repo}/releases/tags/{tag}"))
    if metadata["draft"] or not metadata.get("published_at"):
        raise ValueError(f"{repo}@{tag} is not a published release.")
    destination = out / "downloads" / key
    destination.mkdir(parents=True, exist_ok=True)
    write_json(destination / "release.json", metadata)
    assets = {asset["name"]: asset for asset in metadata["assets"]}
    archive_name = mod_id + ".zip"
    required = [archive_name]
    if kind in ("ipc", "helper"):
        required.append(archive_name + ".sha256")
    if kind in ("ipc", "replay"):
        required.append(mod_id + ".update.json")
    receipts = []
    for name in required:
        if name not in assets:
            raise ValueError(f"Published release {repo}@{tag} has no {name}.")
        asset = assets[name]
        target = destination / name
        if target.exists():
            matches = target.stat().st_size == asset["size"]
            matches = matches and (
                not asset.get("digest") or asset["digest"] == "sha256:" + sha256(target)
            )
            if not matches:
                target.unlink()
        if not target.exists():
            gh("release", "download", tag, "--repo", repo, "--pattern", name, "--dir", str(destination))
        digest = sha256(target)
        if target.stat().st_size != asset["size"]:
            raise ValueError(f"Incorrect downloaded size: {target}")
        if asset.get("digest") and asset["digest"] != "sha256:" + digest:
            raise ValueError(f"Incorrect GitHub asset digest: {target}")
        receipts.append({
            "name": name, "url": asset["browser_download_url"], "bytes": target.stat().st_size,
            "sha256": digest, "githubDigest": asset.get("digest"), "assetId": asset["id"],
        })
    archive = destination / archive_name
    checksum = destination / (archive_name + ".sha256")
    if checksum.exists():
        expected = checksum.read_text().strip().split()[0].lower()
        if not re.fullmatch(r"[a-f0-9]{64}", expected) or expected != sha256(archive):
            raise ValueError(f"Checksum sidecar mismatch: {archive}")
    manifest = destination / (mod_id + ".update.json")
    if manifest.exists():
        value = read_json(manifest)
        digest = value.get("packageSha256", value.get("sha256", value.get("Sha256", "")))
        size = value.get("packageBytes", value.get("PackageSize", value.get("size", value.get("sizeBytes"))))
        version = value.get("version", value.get("Version"))
        if digest.lower() != sha256(archive) or (size is not None and int(size) != archive.stat().st_size):
            raise ValueError(f"Update manifest hash/size mismatch: {archive}")
        if version != tag.removeprefix("v"):
            raise ValueError(f"Update manifest version mismatch: {archive}")
    bundled_sdk_root = None
    with zipfile.ZipFile(archive) as zipped:
        seen = set()
        for entry in zipped.infolist():
            path = PurePosixPath(entry.filename)
            if ("\\" in entry.filename or path.is_absolute() or ".." in path.parts
                    or not path.parts or path.parts[0] != mod_id):
                raise ValueError(f"Unsafe or unexpected archive path: {entry.filename}")
            if stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError(f"Symlink is not allowed in fixture archives: {entry.filename}")
            if not entry.is_dir():
                if entry.filename.casefold() in seen:
                    raise ValueError(f"Duplicate archive file: {entry.filename}")
                seen.add(entry.filename.casefold())
        info = json.loads(zipped.read(mod_id + "/Info.json").decode("utf-8-sig"))
        if info["Id"] != mod_id or info["Version"] != tag.removeprefix("v"):
            raise ValueError(f"Packaged mod identity/version mismatch: {archive}")
        entry_assembly = info.get("AssemblyName", "")
        if not entry_assembly or mod_id.casefold() + "/" + entry_assembly.casefold() not in seen:
            raise ValueError(f"Packaged root entry assembly is missing: {archive}")
        if key.endswith("-new") and kind in ("helper", "replay"):
            for name in zipped.namelist():
                if not name.casefold().endswith("/" + SDK_FILES[0].casefold()):
                    continue
                prefix = name[:-len(SDK_FILES[0])]
                if all((prefix + relative).casefold() in seen for relative in SDK_FILES):
                    bundled_sdk_root = prefix
                    break
            if bundled_sdk_root is None:
                raise ValueError(f"A complete bundled SDK is missing from published package: {archive}")
            expected_entry = ("TUFHelperLite.Launcher.EntryPoint.Load" if kind == "helper"
                              else "TUFReplay.Bootstrap.Bootstrap.Load")
            if info.get("EntryMethod") != expected_entry:
                raise ValueError(f"Published new package still uses a legacy entry point: {archive}")
        bad = zipped.testzip()
        if bad:
            raise ValueError(f"ZIP CRC mismatch: {archive}: {bad}")
    receipt = {
        "repository": repo, "tag": tag, "releaseUrl": metadata["html_url"],
        "releaseId": metadata["id"], "publishedAt": metadata["published_at"],
        "prerelease": metadata["prerelease"], "assets": receipts,
        "archive": str(archive.relative_to(out)), "modId": mod_id, "info": info,
        "bundledSdkRoot": bundled_sdk_root,
    }
    write_json(destination / "provenance.json", receipt)
    return receipt


def case(name, title, helper=None, replay=None, ipc=None, mode="online", mutation=None):
    return dict(name=name, title=title, helper=helper, replay=replay, ipc=ipc, mode=mode, mutation=mutation)


def definitions():
    return [
        case("01-helper-old-ipc-old", "Helper 단독: 구버전 자동 전환", helper="old", ipc="old"),
        case("02-replay-old-ipc-old", "Replay 단독: 구버전 자동 전환", replay="old", ipc="old"),
        case("03-both-old-ipc-old", "둘 다 구버전: IPC 0.4.1 → 0.4.2", helper="old", replay="old", ipc="old"),
        case("04-both-old-no-ipc", "둘 다 구버전: 기존 IPC 자동 설치 경로", helper="old", replay="old"),
        case("05-helper-old-ipc-transition", "Helper 단독: IPC 0.4.2가 먼저 설치됨", helper="old", ipc="new"),
        case("06-replay-old-ipc-transition", "Replay 단독: IPC 0.4.2가 먼저 설치됨", replay="old", ipc="new"),
        case("07-both-old-ipc-transition", "둘 다 구버전: IPC 0.4.2가 먼저 설치됨", helper="old", replay="old", ipc="new"),
        case("08-helper-new-replay-old", "Helper 신버전 + Replay 구버전", helper="new", replay="old", ipc="old"),
        case("09-helper-old-replay-new", "Helper 구버전 + Replay 신버전", helper="old", replay="new", ipc="old"),
        case("10-both-new-ipc-old", "둘 다 신버전 + 남은 IPC 0.4.1", helper="new", replay="new", ipc="old"),
        case("11-both-new-ipc-transition", "둘 다 신버전 + IPC 0.4.2 즉시 정리", helper="new", replay="new", ipc="new"),
        case("12-helper-new-clean-install", "Helper 신버전 단독 신규 설치", helper="new"),
        case("13-replay-new-clean-install", "Replay 신버전 단독 신규 설치", replay="new"),
        case("14-both-new-clean-install", "둘 다 신버전 신규 설치", helper="new", replay="new"),
        case("15-ipc-transition-alone", "소비 모드 없이 IPC 0.4.2 정리", ipc="new"),
        case("16-order-helper-before-replay", "로드 순서: Helper → Replay → IPC", helper="new", replay="new", ipc="new", mutation="helper-first"),
        case("17-order-replay-before-helper", "로드 순서: Replay → Helper → IPC", helper="new", replay="new", ipc="new", mutation="replay-first"),
        case("18-offline-both-old", "오프라인: 구버전이 현재 설치로 실행", helper="old", replay="old", ipc="old", mode="offline"),
        case("19-offline-old-no-ipc", "오프라인: IPC 없는 구버전 최초 설치 실패", helper="old", replay="old", mode="offline-install-failure"),
        case("20-offline-both-new", "오프라인: 신버전 동봉 SDK로 실행", helper="new", replay="new", mode="offline"),
        case("21-offline-mixed-transition", "오프라인: 미전환 Replay 때문에 IPC 유지", helper="new", replay="old", ipc="new", mode="offline-retain"),
        case("22-incomplete-replay-sdk", "불완전 SDK: 정리 보류 및 오류 확인", helper="new", replay="new", ipc="new", mode="incomplete", mutation="missing-sdk"),
        case("23-interrupted-downloads", "중단된 다운로드 잔여물에서 복구", helper="old", replay="old", ipc="old", mutation="partial-downloads"),
        case("24-replay-beta-opt-out", "Replay 베타 수신 꺼짐: 정식 1.0.0으로 전환", replay="old", ipc="old", mutation="beta-off"),
        case("25-download-failure", "릴리스 조회 성공 후 ZIP 다운로드 실패", helper="old", replay="old", ipc="old", mode="download-failure"),
        case("26-renamed-old-replay", "폴더 이름이 다른 구버전 Replay도 IPC 정리 보류", helper="new", replay="old", ipc="new", mode="offline-retain", mutation="renamed-replay"),
        case("27-renamed-new-consumers", "폴더 이름이 다른 SDK 소비 모드", helper="new", replay="new", ipc="new", mutation="renamed-both"),
    ]


def extract(archive, mods):
    with zipfile.ZipFile(archive) as zipped:
        zipped.extractall(mods)
        for entry in zipped.infolist():
            mode = (entry.external_attr >> 16) & 0o777
            if mode and not entry.is_dir():
                (mods / entry.filename).chmod(mode)


def seed_user_data(mods):
    preserved = {}
    settings = {}
    for root in sorted(mods.iterdir()):
        if not (root / "Info.json").is_file():
            continue
        mod_id = read_json(root / "Info.json").get("Id")
        if mod_id not in ("TUFHelperLite", "TUFReplay"):
            continue
        paths = ["Data/migration-preservation.txt"]
        if mod_id == "TUFHelperLite":
            paths.append("Downloads/migration-preservation.txt")
            value = {"DownloadRoot": None, "TempDownloadMode": True}
        else:
            value = {"AutoRecord": False, "MicrophoneEnabled": False, "WebcamEnabled": False,
                     "MicrophoneOffsetMs": 37, "MicrophoneOffsetConventionVersion": 1}
            write_json(root / "UpdateSettings.json", {"ReceiveBetaUpdates": True})
        write_json(root / "Settings.json", value)
        settings[str((root / "Settings.json").relative_to(mods))] = value
        for relative in paths:
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(SENTINEL)
            preserved[str(path.relative_to(mods))] = sha256(path)
    return {"files": preserved, "settings": settings}


def verify_prepared_preservation(mods, preservation):
    checks = 0
    for relative, digest in preservation["files"].items():
        if sha256(mods / relative) != digest:
            raise ValueError("Prepared preservation marker mismatch: " + relative)
        checks += 1
    for relative, expected in preservation["settings"].items():
        actual = read_json(mods / relative)
        for key, value in expected.items():
            if actual.get(key) != value:
                raise ValueError(f"Prepared setting mismatch: {relative}: {key}")
            checks += 1
    return checks


def mutate(definition, mods):
    mutation = definition["mutation"]
    changes = []
    if mutation in ("helper-first", "replay-first"):
        first, second = (("TUFHelperLite", "TUFReplay") if mutation == "helper-first"
                         else ("TUFReplay", "TUFHelperLite"))
        for mod_id, after in ((first, []), (second, [first]), ("AdofaiIpc", [second])):
            path = mods / mod_id / "Info.json"
            value = read_json(path)
            value["LoadAfter"] = after
            write_json(path, value)
            changes.append(f"{mod_id}/Info.json LoadAfter={after}")
    elif mutation == "missing-sdk":
        for path in sorted((mods / "TUFReplay").rglob("AdofaiIpc.Loader.dll")):
            changes.append("removed " + str(path.relative_to(mods)))
            path.unlink()
    elif mutation == "partial-downloads":
        for relative in ("TUFHelperLite/Data/updates/download-interrupted.zip", "TUFReplay/Runtime/download-interrupted.zip"):
            path = mods / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"PK\x03\x04interrupted fixture; deliberately incomplete\n")
            changes.append("created " + relative)
    elif mutation == "beta-off":
        write_json(mods / "TUFReplay/UpdateSettings.json", {"ReceiveBetaUpdates": False})
        changes.append("TUFReplay/UpdateSettings.json ReceiveBetaUpdates=false")
    elif mutation in ("renamed-replay", "renamed-both"):
        kinds = ("TUFReplay",) if mutation == "renamed-replay" else ("TUFHelperLite", "TUFReplay")
        for mod_id in kinds:
            destination = mods / ("Renamed " + mod_id)
            (mods / mod_id).rename(destination)
            changes.append(f"renamed {mod_id} to {destination.name}; Info.Id unchanged")
    return changes


def expected_text(definition):
    mode = definition["mode"]
    if mode == "offline-install-failure":
        return "구버전 의존성 설치 실패를 로그로 남기고 시작이 실패할 수 있다. 사용자 파일은 보존한다. 온라인으로 복구한 뒤 재실행하면 정상 전환한다."
    if mode == "offline-retain":
        return "구버전 Replay가 남으므로 IPC 폴더는 유지한다. 신버전 Helper는 SDK로 실행한다. 온라인 재실행 후 두 모드 전환과 IPC 정리를 확인한다."
    if mode == "incomplete":
        return "Replay의 Loader DLL이 모든 위치에서 제거되어 로드가 실패할 수 있다. IPC는 미완성 소비 모드를 감지해 유지해야 한다. 원본 신버전 ZIP으로 복구한 다음 정리를 확인한다."
    if mode == "download-failure":
        return "업데이트 다운로드 오류 후 현재 설치로 실행한다. 실패한 업데이트는 승격하지 않으며 IPC를 성급하게 삭제하지 않는다. 네트워크 제한 해제 후 재실행하면 전환한다."
    if mode == "offline" and (definition["helper"] == "old" or definition["replay"] == "old"):
        return "업데이트 조회 실패 후 기존 버전과 IPC를 유지하며 실행한다. 온라인 재실행에서 전환한다."
    return "설치된 Helper/Replay는 게시된 새 버전의 SDK로 실행하고 사용자 파일과 설정을 보존한다. IPC가 있으면 소비 모드 전환 완료 후 실행 중 Mods/AdofaiIpc가 사라진다. 두 번째 실행에도 IPC가 재설치되지 않는다."


def create_fault_inputs(out, receipts):
    faults = out / "fault-inputs"
    faults.mkdir(exist_ok=True)
    result = {}
    for kind in ("helper", "replay"):
        if kind + "-new" not in receipts:
            continue
        receipt = receipts[kind + "-new"]
        original = out / receipt["archive"]
        mod_id = receipt["modId"]
        destination = faults / kind
        destination.mkdir(exist_ok=True)
        truncated = destination / "truncated.zip"
        truncated.write_bytes(original.read_bytes()[: min(1024, original.stat().st_size // 2)])
        incomplete = destination / "missing-payload.zip"
        with zipfile.ZipFile(original) as source, zipfile.ZipFile(incomplete, "w", zipfile.ZIP_DEFLATED) as target:
            for entry in source.infolist():
                if entry.filename.endswith(("TUFHelperLite.Core.dll", "/TUFReplay.dll")):
                    continue
                target.writestr(entry, source.read(entry))
        result[kind] = {
            "source": receipt["archive"], "originalSha256": sha256(original),
            "truncated": {"path": str(truncated.relative_to(out)), "sha256": sha256(truncated)},
            "missingPayload": {"path": str(incomplete.relative_to(out)), "sha256": sha256(incomplete)},
            "usage": "오프라인 테스트 입력. 고정 GitHub HTTPS URL을 쓰는 배포 바이너리는 이 파일을 자동으로 받지 않는다. 다운로더를 주입한 업데이터 테스트에서 사용하며 공개 릴리스 파일은 교체하지 않는다.",
        }
    write_json(faults / "inputs.json", result)


def generate(args):
    out = safe_output(args.out)
    cases_root = out / "cases"
    if cases_root.exists() and not args.replace:
        raise ValueError("Generated cases already exist; use --replace to reset only the fixture copies.")
    out.mkdir(parents=True, exist_ok=True)
    marker = out / ".adofai-migration-fixtures.json"
    if cases_root.exists() and not marker.exists():
        raise ValueError("Refusing to replace an unrecognized directory.")
    receipts = {}
    for kind, (_, _, old_tag) in SPECS.items():
        receipts[kind + "-old"] = fetch_release(out, kind + "-old", kind, old_tag)
    if not args.baseline_only:
        for kind in SPECS:
            tag = getattr(args, kind + "_tag")
            receipts[kind + "-new"] = fetch_release(out, kind + "-new", kind, tag)
    write_json(marker, {"schema": 1, "generatedBy": "scripts/testing/migration_e2e.py"})
    if cases_root.exists():
        shutil.rmtree(cases_root)
    cases_root.mkdir()
    cases = []
    skipped = []
    for definition in definitions():
        needed = [kind + "-" + definition[kind] for kind in SPECS if definition[kind]]
        if any(key not in receipts for key in needed):
            skipped.append(definition["name"])
            continue
        directory = cases_root / definition["name"]
        mods = directory / "Mods"
        mods.mkdir(parents=True)
        for key in needed:
            extract(out / receipts[key]["archive"], mods)
        changes = mutate(definition, mods) if definition["mutation"] != "beta-off" else []
        preservation = seed_user_data(mods)
        if definition["mutation"] == "beta-off":
            changes = mutate(definition, mods)
        metadata = {**definition, "sources": needed, "modifications": changes,
                    "expected": expected_text(definition), "manualGameRun": False,
                    "newVersions": {kind: receipts[kind + "-new"]["tag"].removeprefix("v")
                                    for kind in SPECS if kind + "-new" in receipts},
                    "newEntryAssemblies": {kind: receipts[kind + "-new"]["info"]["AssemblyName"]
                                           for kind in SPECS if kind + "-new" in receipts}}
        metadata["preparedFiles"] = sum(1 for path in mods.rglob("*") if path.is_file())
        metadata["preparedPreservationChecks"] = verify_prepared_preservation(mods, preservation)
        metadata["installFolders"] = {
            read_json(path / "Info.json")["Id"]: path.name for path in mods.iterdir()
            if (path / "Info.json").is_file()
        }
        write_json(directory / "fixture.json", metadata)
        write_json(directory / "preservation.json", preservation)
        (directory / "README.md").write_text(
            f"# {definition['title']}\n\n"
            f"- 준비: 실제 공개 ZIP에서 추출. 게임 E2E 실행: 아직 안 함.\n"
            f"- 네트워크 조건: `{definition['mode']}`.\n"
            f"- 출처: {', '.join(needed)} (`../../provenance.json`).\n"
            f"- ZIP 추출 후 수정: {json.dumps(changes, ensure_ascii=False)}. 모든 세트에 설정/보존 표본 추가.\n"
            f"- 예상 결과: {metadata['expected']}\n\n"
            "게임을 완전히 종료한 뒤 이 폴더의 Mods를 별도 백업한 게임 Mods 자리에 복사한다. "
            "각 세트는 독립적이다. 첫 실행 중 정리 상태를 보고, 게임을 완전히 종료한 다음 같은 설치로 재실행한다. "
            "자세한 절차와 실패 주입 제한은 저장소 docs/migration-e2e.md를 따른다.\n",
            encoding="utf-8",
        )
        cases.append(metadata)
    create_fault_inputs(out, receipts)
    write_json(out / "provenance.json", receipts)
    report = {"schema": 1, "fixtureRoot": str(out), "preparedCases": len(cases), "cases": cases,
              "skippedUntilPublished": skipped, "automated": {
                  "publishedReleaseMetadata": True, "githubAssetDigests": True,
                  "packageChecksumOrManifest": True, "zipCrcAndSafePaths": True,
                  "modIdentityAndVersion": True, "generatedFolderSets": True,
                  "preparedPreservationChecksPassed": sum(item["preparedPreservationChecks"] for item in cases),
                  "unityGameE2e": False, "windowsLoadedFileDeletion": False}}
    write_json(out / "report.json", report)
    index = ["# SDK 전환 테스트 Mods 세트", "", f"준비된 사례: {len(cases)}개. 게임 E2E: 아직 실행하지 않음.", "",
             "실행 안내: [migration-e2e.md](" + str(REPO_ROOT / "docs/migration-e2e.md") + ").",
             "실제 출처와 해시는 [provenance.json](provenance.json), 자동 확인 결과는 [report.json](report.json)을 확인한다.", "",
             "| 세트 | 내용 | 네트워크 |", "| --- | --- | --- |"]
    for item in cases:
        index.append(f"| [{item['name']}](cases/{item['name']}/README.md) | {item['title']} | {item['mode']} |")
    if skipped:
        index.extend(["", "미게시 신버전 때문에 제외한 사례: " + ", ".join(skipped)])
    (out / "README.md").write_text("\n".join(index) + "\n", encoding="utf-8")
    print(json.dumps({"fixtureRoot": str(out), "preparedCases": len(cases), "skippedCases": len(skipped),
                      "provenance": str(out / "provenance.json"), "manualGameE2e": "not run"}, ensure_ascii=False))


def inspect(args):
    fixture = Path(args.case).expanduser().resolve()
    mods = Path(args.mods).expanduser().resolve()
    metadata = read_json(fixture / "fixture.json")
    preserved = read_json(fixture / "preservation.json")
    checks = []

    def check(label, passed, detail=""):
        checks.append({"check": label, "passed": bool(passed), "detail": detail})

    for relative, expected in preserved["files"].items():
        path = mods / relative
        check("preserved " + relative, path.is_file() and sha256(path) == expected)
    for relative, expected in preserved["settings"].items():
        try:
            value = read_json(mods / relative)
            value = value.get("Setting", value)
            for key, expected_value in expected.items():
                check(f"setting {relative} {key}", value.get(key) == expected_value,
                      f"expected={expected_value!r}, actual={value.get(key)!r}")
        except (OSError, ValueError) as error:
            check("read " + relative, False, str(error))
    if args.expect_migrated:
        for kind in ("helper", "replay"):
            if not metadata[kind]:
                continue
            mod_id = SPECS[kind][1]
            root = mods / metadata.get("installFolders", {}).get(mod_id, mod_id)
            if kind not in metadata["newVersions"]:
                raise ValueError("Migration expectations require fixtures generated with published new release tags.")
            try:
                info = read_json(root / "Info.json")
            except (OSError, ValueError) as error:
                check(mod_id + " root Info", False, str(error))
                continue
            expected = metadata["newVersions"][kind]
            check(mod_id + " root Info version", info.get("Version") == expected,
                  f"expected={expected}, actual={info.get('Version')}")
            entry_assembly = metadata.get("newEntryAssemblies", {}).get(kind)
            if entry_assembly:
                check(mod_id + " root entry assembly", info.get("AssemblyName") == entry_assembly
                      and (root / entry_assembly).is_file(), str(info.get("AssemblyName")))
            direct = ("TUFHelperLite.Launcher.EntryPoint.Load" if kind == "helper"
                      else "TUFReplay.Bootstrap.Bootstrap.Load")
            check(mod_id + " direct SDK entry", info.get("EntryMethod") == direct,
                  str(info.get("EntryMethod")))
            for relative in SDK_FILES:
                check(mod_id + " SDK " + relative, (root / relative).is_file())
            state_path = root / "Runtime/state.json"
            try:
                state = read_json(state_path)
                check(mod_id + " runtime Current", state.get("Current") == expected,
                      f"expected={expected}, actual={state.get('Current')}")
                check(mod_id + " runtime Trial cleared", not state.get("Trial"))
            except (OSError, ValueError) as error:
                check(mod_id + " runtime state", False, str(error))
        check("Mods/AdofaiIpc absent", not (mods / "AdofaiIpc").exists())
    if args.expect_ipc_retained:
        check("Mods/AdofaiIpc retained", (mods / "AdofaiIpc").is_dir())
    retired = mods.parent / ".adofai-ipc-retired"
    remnants = [str(path) for path in retired.iterdir()] if retired.is_dir() else []
    result = {"case": metadata["name"], "mods": str(mods), "readOnly": True,
              "passed": all(item["passed"] for item in checks), "checks": checks,
              "retiredRemnants": remnants, "unityBehaviorVerified": False,
              "note": "Files alone do not prove IPC endpoints/game behavior; observe game logs and exercise both mods."}
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0 if result["passed"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    create = commands.add_parser("generate")
    create.add_argument("--out", default=str(DEFAULT_OUT))
    create.add_argument("--ipc-tag", default="v0.4.2")
    create.add_argument("--helper-tag", default="v1.0.0")
    create.add_argument("--replay-tag", default="v1.0.0")
    create.add_argument("--baseline-only", action="store_true")
    create.add_argument("--replace", action="store_true", help="Reset generated case copies under the fixture root.")
    verify = commands.add_parser("inspect")
    verify.add_argument("--case", required=True, help="Prepared case directory, not its Mods child.")
    verify.add_argument("--mods", required=True, help="Installed Mods path to read without changing it.")
    expectations = verify.add_mutually_exclusive_group()
    expectations.add_argument("--expect-migrated", action="store_true")
    expectations.add_argument("--expect-ipc-retained", action="store_true")
    args = parser.parse_args()
    try:
        return generate(args) if args.command == "generate" else inspect(args)
    except (OSError, ValueError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        print(str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
