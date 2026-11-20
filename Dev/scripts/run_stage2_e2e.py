# @module: Housekeeping.Scripts.run_stage2_e2e
# @purpose: Two-Build Stage 2 4-Drive Sandbox E2E suite (TestBuild Wizard/Modules/Subpath/WebView2 + exebox-stream + Release fail-closed ExitCode=2 + hostDiffCount==0).
# @depends-on: argparse, ctypes, hashlib, json, os, shutil, socket, struct, subprocess, sys, time, urllib.error, urllib.request, uuid, zipfile, zlib
# @exports: run_stage2_e2e, main

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import os
import shutil
import socket
import struct
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
import zipfile
import zlib
from pathlib import Path
from typing import Any

DEV_ROOT = Path(__file__).resolve().parents[1]
OUTER_ROOT = DEV_ROOT.parent
FOUNDATION_TOOLS = (
    DEV_ROOT.parents[1] / "Foundation" / "Dev" / "tools"
    if len(DEV_ROOT.parents) > 1 and (DEV_ROOT.parents[1] / "Foundation" / "Dev" / "tools").is_dir()
    else Path(r"D:\Projects\Personal\Foundation\Dev\tools")
)
if str(FOUNDATION_TOOLS) not in sys.path:
    sys.path.insert(0, str(FOUNDATION_TOOLS))

from exebox_stream import start_exebox_live_stream, stop_exebox_live_stream  # noqa: E402
from sandbox_ctl import diff_host_snapshots, init_sandbox, resolve_sandbox_config, take_host_snapshot, utc_now_iso  # noqa: E402

SAFE_SYSTEM_ENV_KEYS = (
    "SYSTEMROOT", "WINDIR", "COMSPEC", "PATH", "PATHEXT", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS",
    "DOTNET_ROOT", "DOTNET_CLI_TELEMETRY_OPTOUT", "SystemDrive", "ProgramData", "ProgramFiles", "ProgramFiles(x86)",
    "CommonProgramFiles", "CommonProgramFiles(x86)", "ALLUSERSPROFILE", "PUBLIC",
)


def _get_thread_desktop() -> str:
    if os.name != "nt":
        return "non-windows"
    k32, u32 = ctypes.windll.kernel32, ctypes.windll.user32
    buf = ctypes.create_unicode_buffer(256)
    u32.GetUserObjectInformationW(u32.GetThreadDesktop(k32.GetCurrentThreadId()), 2, buf, ctypes.sizeof(buf), ctypes.byref(ctypes.c_ulong(0)))
    return buf.value or "unknown"


def _is_udp_5004_bound() -> bool:
    if os.name != "nt":
        return False
    try:
        ps = subprocess.check_output(["powershell.exe", "-NoProfile", "-Command", "if (Get-NetUDPEndpoint -LocalPort 5004 -EA SilentlyContinue) { 'BOUND' }"], text=True, timeout=6)
        return "BOUND" in ps or ":5004" in subprocess.check_output(["netstat", "-ano", "-p", "udp"], text=True, timeout=5)
    except (OSError, subprocess.SubprocessError):
        return False


def _free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return int(s.getsockname()[1])


def _build_sandbox_env(cfg: dict[str, Any]) -> dict[str, str]:
    env = {k: os.environ[k] for k in SAFE_SYSTEM_ENV_KEYS if os.environ.get(k)}
    prof = Path(cfg["sandboxProfileDir"])
    local_app, roam_app = prof / "AppData" / "Local", prof / "AppData" / "Roaming"
    temp_dir = local_app / "Temp"
    bundle_dir = temp_dir / "dotnet_bundle"
    for d in (prof, local_app, roam_app, temp_dir, bundle_dir):
        d.mkdir(parents=True, exist_ok=True)
    env.update({
        "FOUNDATION_SANDBOX_ROOT": str(cfg["sandboxRoot"]), "FOUNDATION_SANDBOX_PROFILE_DIR": str(prof),
        "USERPROFILE": str(prof), "HOME": str(prof), "LOCALAPPDATA": str(local_app), "APPDATA": str(roam_app),
        "TEMP": str(temp_dir), "TMP": str(temp_dir), "DOTNET_BUNDLE_EXTRACT_BASE_DIR": str(bundle_dir),
        "FOUNDATION_SUPPRESS_MODALS": "1",
    })
    for role in ("SANDBOX_C_DRIVE", "SANDBOX_D_DRIVE", "SANDBOX_REMOVABLE_DRIVE", "SANDBOX_DISC_DRIVE"):
        if cfg["drives"].get(role):
            env[role] = cfg["drives"][role]
    env.pop("APP_ISOLATED_TEST", None)
    return env


def _run_bounded(cmd: list[str], env: dict[str, str], log_dir: Path, tag: str, timeout_s: int = 30) -> tuple[int, str, str]:
    out_p, err_p = log_dir / f"{tag}.stdout.log", log_dir / f"{tag}.stderr.log"
    with out_p.open("w", encoding="utf-8") as fo, err_p.open("w", encoding="utf-8") as fe:
        proc = subprocess.Popen(cmd, env=env, stdout=fo, stderr=fe, text=True)
        try:
            rc = proc.wait(timeout=timeout_s)
        except subprocess.TimeoutExpired:
            proc.terminate()
            proc.wait(timeout=5)
            raise TimeoutError(f"Command '{tag}' timed out after {timeout_s}s")
    return rc, out_p.read_text(encoding="utf-8", errors="replace"), err_p.read_text(encoding="utf-8", errors="replace")


def _http_raw(url: str, method: str = "GET", body: Any = None, token: str | None = None, headers: dict[str, str] | None = None, timeout_s: float = 25.0) -> tuple[int, dict[str, str], bytes]:
    data = json.dumps(body).encode("utf-8") if body is not None else None
    hdrs = dict(headers or {})
    if data is not None:
        hdrs.setdefault("Content-Type", "application/json")
    if token:
        hdrs["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(url, data=data, headers=hdrs, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout_s) as resp:
            return resp.status, {k.lower(): v for k, v in resp.headers.items()}, resp.read()
    except urllib.error.HTTPError as err:
        return err.code, {k.lower(): v for k, v in err.headers.items()}, err.read()


def _http_json(url: str, method: str = "GET", body: Any = None, token: str | None = None, headers: dict[str, str] | None = None, timeout_s: float = 25.0) -> tuple[int, Any]:
    code, _, raw_bytes = _http_raw(url, method=method, body=body, token=token, headers=headers, timeout_s=timeout_s)
    txt = raw_bytes.decode("utf-8", errors="replace")
    return code, json.loads(txt) if txt.strip().startswith(("{", "[")) else {"raw": txt}


def _wait_http_ok(url: str, token: str | None = None, timeout_s: float = 25.0) -> Any:
    deadline, last_err = time.monotonic() + timeout_s, ""
    while time.monotonic() < deadline:
        try:
            code, payload = _http_json(url, token=token, timeout_s=2.5)
            if 200 <= code < 300:
                return payload
            last_err = f"HTTP {code}: {payload}"
        except (OSError, urllib.error.URLError, json.JSONDecodeError) as ex:
            last_err = str(ex)
        time.sleep(0.25)
    raise RuntimeError(f"Timed out waiting for {url}: {last_err}")


def _find_node(node: dict[str, Any] | None, ctype: str, text_sub: str = "", visible_only: bool = False, exact: bool = False) -> dict[str, Any] | None:
    if not node:
        return None
    vis_ok = not visible_only or bool(node.get("isVisible"))
    txt = node.get("text", "")
    if vis_ok and node.get("controlType", "").lower() == ctype.lower() and ((txt.lower() == text_sub.lower()) if exact else (text_sub.lower() in txt.lower())):
        return node
    return next((m for ch in node.get("children", []) if (m := _find_node(ch, ctype, text_sub, visible_only, exact)) is not None), None)


def _png_meta(raw: bytes) -> tuple[bool, int, int, int]:
    if not raw.startswith(b"\x89PNG\r\n\x1a\n") or len(raw) < 2048:
        return False, 0, 0, len(raw)
    w, h = struct.unpack(">II", raw[16:24])
    idat, pos = bytearray(), 8
    while pos + 8 <= len(raw):
        clen, ctype = struct.unpack(">I4s", raw[pos:pos + 8])
        if ctype == b"IDAT":
            idat.extend(raw[pos + 8:pos + 8 + clen])
        pos += 12 + clen
    decomp = zlib.decompress(bytes(idat))
    unique = len(set(decomp[i] for i in range(1, min(len(decomp), 240000), 17)))
    return (w >= 380 and h >= 300 and unique >= 12), w, h, len(raw)


def _capture_png_endpoint(ap: int, tok: str, dest: Path, min_wait_s: float = 1.6, timeout_s: float = 25.0, width_range: tuple[int, int] = (380, 4096)) -> dict[str, Any]:
    time.sleep(min_wait_s)
    deadline, last_meta, last_code, last_err = time.monotonic() + timeout_s, (False, 0, 0, 0), 0, ""
    while time.monotonic() < deadline:
        try:
            code, _, raw = _http_raw(f"http://127.0.0.1:{ap}/v1/snapshot.png", token=tok, timeout_s=10.0)
            last_code = code
            if code == 200 and raw:
                meta = _png_meta(raw)
                last_meta = meta
                if meta[0] and meta[3] > 6000 and width_range[0] <= meta[1] <= width_range[1]:
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    dest.write_bytes(raw)
                    return {"filePath": str(dest), "width": meta[1], "height": meta[2], "sizeBytes": meta[3]}
        except (OSError, urllib.error.URLError, zlib.error) as ex:
            last_err = str(ex)
        time.sleep(0.45)
    raise RuntimeError(f"GET /v1/snapshot.png '{dest.name}' did not stabilize: {last_meta} code={last_code} err={last_err}")


def _terminate_tree(proc: subprocess.Popen[str]) -> None:
    if proc.poll() is None:
        subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=8, check=False)


def _build_ephemeral_test_setup(version: str, work_dir: Path) -> Path:
    web_root = DEV_ROOT / "Housekeeping.Web"
    if not (DEV_ROOT / "Housekeeping.Server" / "wwwroot" / "index.html").is_file() and (web_root / "package.json").is_file():
        if not (web_root / "node_modules").is_dir():
            subprocess.run(["npm.cmd", "--prefix", str(web_root), "ci"], check=True)
        subprocess.run(["npm.cmd", "--prefix", str(web_root), "run", "build"], check=True)
    stage_dir = work_dir / "stage"
    app_dir, scripts_dir, pub_launch, pub_inst = stage_dir / "app", stage_dir / "scripts", work_dir / "pub_launch", work_dir / "pub_inst"
    for d in (app_dir, scripts_dir, pub_launch, pub_inst):
        d.mkdir(parents=True, exist_ok=True)
    auto_props = ["-c", "Release", "-r", "win-x64", "--self-contained", "false", "-t:Rebuild,Publish", "-p:HousekeepingTestAutomation=true", f"-p:Version={version}", "-nodeReuse:false", "/nologo"]
    for proj, out in ((DEV_ROOT / "Housekeeping.DesktopHost" / "Housekeeping.DesktopHost.csproj", app_dir),
                      (DEV_ROOT / "Housekeeping.Server" / "Housekeeping.Server.csproj", app_dir),
                      (DEV_ROOT / "Housekeeping.Launcher" / "Housekeeping.Launcher.csproj", pub_launch)):
        subprocess.run(["dotnet", "publish", str(proj), *auto_props, "-o", str(out)], check=True)
    shutil.copy2(pub_launch / "Housekeeping.exe", stage_dir / "Housekeeping.exe")
    for src, dst in ((OUTER_ROOT / "LICENSE", app_dir / "LICENSE"), (DEV_ROOT / "suite.manifest.json", app_dir / "suite.manifest.json"), (DEV_ROOT / "version.txt", app_dir / "version.txt")):
        if src.is_file():
            shutil.copy2(src, dst)
    if (ex_cfg := DEV_ROOT / "config" / "settings.example.json").is_file():
        (app_dir / "config").mkdir(parents=True, exist_ok=True)
        shutil.copy2(ex_cfg, app_dir / "config" / "settings.example.json")
    for bat in (DEV_ROOT / "scripts").glob("*.bat"):
        shutil.copy2(bat, scripts_dir / bat.name)
    assert not any(stage_dir.rglob("AGENTS.md")), "AGENTS.md must never be staged inside Build/Setup payload!"
    payload_zip = work_dir / "payload.zip"
    staged_files = sorted(p for p in stage_dir.rglob("*") if p.is_file())
    with zipfile.ZipFile(payload_zip, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for f in staged_files:
            zf.write(f, f.relative_to(stage_dir).as_posix())
    meta_json = work_dir / "metadata.json"
    meta_json.write_text(json.dumps({"AppName": "Housekeeping", "Version": version, "PayloadSha256": hashlib.sha256(payload_zip.read_bytes()).hexdigest(), "FileCount": len(staged_files)}), encoding="utf-8")
    out_exe = work_dir / f"Housekeeping-Setup-v{version}-win-x64-automation.exe"
    subprocess.run(["dotnet", "publish", str(DEV_ROOT / "Housekeeping.Installer" / "Housekeeping.Installer.csproj"), "-c", "Release", "-r", "win-x64", "-t:Rebuild,Publish", "-p:HousekeepingTestAutomation=true", f"-p:InstallerVersion={version}", f"-p:InstallerPayloadZip={payload_zip}", f"-p:InstallerPayloadArchive={payload_zip}", f"-p:InstallerMetadataJson={meta_json}", "-nodeReuse:false", "-o", str(pub_inst), "/nologo"], check=True)
    shutil.copy2(pub_inst / "Housekeeping.Installer.exe", out_exe)
    return out_exe


def _resolve_release_probe_binaries(version: str, rel_dir: Path) -> tuple[Path, Path]:
    builds_dir = OUTER_ROOT / "Builds" / f"v{version}"
    rel_launcher = builds_dir / "Housekeeping.exe"
    rel_setup = OUTER_ROOT / "Releases" / f"v{version}" / f"Housekeeping-Setup-v{version}-win-x64.exe"
    if rel_launcher.is_file() and rel_setup.is_file():
        assert not any(builds_dir.rglob("AGENTS.md")), "AGENTS.md found inside Builds\\!"
        return rel_launcher, rel_setup
    stage_dir, app_dir, pub_inst = rel_dir / "stage", rel_dir / "stage" / "app", rel_dir / "pub_inst"
    app_dir.mkdir(parents=True, exist_ok=True)
    pub_inst.mkdir(parents=True, exist_ok=True)
    rel_props = ["-c", "Release", "-r", "win-x64", "--self-contained", "false", "-t:Rebuild,Publish", f"-p:Version={version}", "-nodeReuse:false", "/nologo"]
    subprocess.run(["dotnet", "publish", str(DEV_ROOT / "Housekeeping.DesktopHost" / "Housekeeping.DesktopHost.csproj"), *rel_props, "-o", str(app_dir)], check=True)
    subprocess.run(["dotnet", "publish", str(DEV_ROOT / "Housekeeping.Launcher" / "Housekeeping.Launcher.csproj"), *rel_props, "-o", str(stage_dir)], check=True)
    subprocess.run(["dotnet", "publish", str(DEV_ROOT / "Housekeeping.Installer" / "Housekeeping.Installer.csproj"), "-c", "Release", "-r", "win-x64", "-t:Rebuild,Publish", f"-p:InstallerVersion={version}", "-nodeReuse:false", "-o", str(pub_inst), "/nologo"], check=True)
    return stage_dir / "Housekeeping.exe", pub_inst / "Housekeeping.Installer.exe"


def _exercise_all_8_modules(base_url: str, sandbox_d_dir: Path) -> dict[str, Any]:
    dirs = {k: sandbox_d_dir / k for k in ("Media", "ArchiveSrc", "ArchiveDst", "Roms", "Cloud", "Humble", "ManifestOut")}
    for d in dirs.values():
        d.mkdir(parents=True, exist_ok=True)
    (dirs["Media"] / "Sample Show - S01E01 - Pilot - DVD (Original).mkv").write_bytes(b"stage2-mkv-fixture")
    with zipfile.ZipFile(dirs["Roms"] / "Sample Game (USA).zip", "w", compression=zipfile.ZIP_DEFLATED) as rzf:
        rzf.writestr("Sample Game (USA).sfc", b"stage2-rom-fixture")
    code, mods = _http_json(f"{base_url}/api/v1/modules")
    assert code == 200 and len(mods.get("modules", [])) == 8
    mod_keys = [m.get("ModuleKey") or m["moduleKey"] for m in mods["modules"]]
    assert _http_json(f"{base_url}/api/v1/jobs/starter", "POST", {})[0] == 200
    for m in mod_keys:
        assert _http_json(f"{base_url}/api/v1/modules/{m}/toggle?enabled=true", "POST", {})[0] == 200
    cfg_code, cfg_res = _http_json(f"{base_url}/api/v1/config")
    assert cfg_code == 200
    c = cfg_res["config"]
    rom_cols = [{"Enabled": True, "ConsoleName": "SNES", "SubFolder": "snes", "ArchivesPath": str(dirs["Roms"])}]
    def _configure_block(b: dict[str, Any]) -> None:
        if "CollectionManifest" in b and b["CollectionManifest"]:
            b["CollectionManifest"]["TargetFolders"] = [{"Enabled": True, "LocationId": "SBX-D", "Label": "Sandbox", "Path": str(dirs["Media"])}]
            b["CollectionManifest"]["UnifiedManifestOutputFolder"] = str(dirs["ManifestOut"])
        if "ProxyGenerator" in b and b["ProxyGenerator"]:
            b["ProxyGenerator"]["Folders"] = [{"Enabled": True, "Path": str(dirs["Media"])}]
        if "ArchiveCompressor" in b and b["ArchiveCompressor"]:
            b["ArchiveCompressor"]["Mappings"] = [{"Enabled": True, "SourceFolder": str(dirs["ArchiveSrc"]), "DestinationFolder": str(dirs["ArchiveDst"])}]
        if "MediaTranscoder" in b and b["MediaTranscoder"]:
            b["MediaTranscoder"]["CustomFoldersToScan"] = [{"Enabled": True, "Path": str(dirs["Media"])}]
        if "RomLauncherGenerator" in b and b["RomLauncherGenerator"]:
            b["RomLauncherGenerator"]["LaunchersOutputRootPath"] = str(dirs["ManifestOut"])
            b["RomLauncherGenerator"]["Collections"] = rom_cols
        if "CloudSync" in b and b["CloudSync"]:
            b["CloudSync"]["Jobs"] = [{"Enabled": True, "JobName": "SbxCloud", "LocalPath": str(dirs["Cloud"]), "RemoteTarget": "sbx:", "SyncMode": "copy"}]
        if "HumbleSync" in b and b["HumbleSync"]:
            b["HumbleSync"]["LibraryPath"] = str(dirs["Humble"])
        if "CustomScripts" in b and b["CustomScripts"]:
            b["CustomScripts"]["Tasks"] = [{"Enabled": True, "Name": "SbxEcho", "CommandOrScriptPath": "cmd.exe", "Arguments": "/c echo STAGE2_OK"}]
    _configure_block(c)
    for job in c.get("DefinedJobs", []):
        _configure_block(job)
    assert _http_json(f"{base_url}/api/v1/config", "PUT", c)[0] == 200
    assert _http_json(f"{base_url}/api/v1/manifest/test-regex", "POST", {"SampleInput": "Sample Show - S01E01 - Pilot - DVD (Original).mkv", "Rules": []})[0] == 200
    assert _http_json(f"{base_url}/api/v1/rom/sync-preview", "POST", {})[0] == 200
    assert _http_json(f"{base_url}/api/v1/pipeline/run-queue", "POST", {"IsDryRun": True})[0] == 200
    dl = time.monotonic() + 30.0
    while time.monotonic() < dl:
        _, st = _http_json(f"{base_url}/api/v1/pipeline/state")
        if not st.get("IsRunning", st.get("isRunning", False)):
            break
        time.sleep(0.35)
    assert _http_json(f"{base_url}/api/v1/reports/scan?wait=true", "POST", {})[0] == 200 and _http_json(f"{base_url}/api/v1/history")[0] == 200
    return {"moduleCount": len(mod_keys), "modules": mod_keys}


def _snapshot_mtimes(root: Path) -> dict[str, tuple[int, int]]:
    return {str(p): (p.stat().st_size, p.stat().st_mtime_ns) for p in root.rglob("*") if p.is_file()} if root.exists() else {}


def run_stage2_e2e(auto_setup: Path | None, shots_dir: Path, build_test: bool = False) -> dict[str, Any]:
    host_localappdata = os.environ.get("LOCALAPPDATA", "")
    host_rclone_before = _snapshot_mtimes(Path(host_localappdata) / "rclone") if host_localappdata else {}
    host_prog_before = _snapshot_mtimes(Path(r"D:\Programs\Housekeeping"))
    cfg = resolve_sandbox_config(argparse.Namespace(env_file=None, c_drive=None, d_drive=None, removable_drive=None, disc_drive=None, sandbox_root=None, profile_dir=None))
    init_sandbox(cfg, skip_subst=False, force_hive=False)
    take_host_snapshot(cfg, name="hk-v020-baseline")

    version = (DEV_ROOT / "version.txt").read_text(encoding="utf-8").strip()
    run_root = Path(cfg["sandboxRoot"]) / "runs" / f"hk_v020_{uuid.uuid4().hex[:8]}"
    logs_dir, setup_data, hk_data = run_root / "logs", run_root / "setup_data", run_root / "hk_data"
    base_inst = Path(cfg["sandboxProfileDir"]) / "AppData" / "Local" / "Programs" / "StreamItAll" / "Mother" / "Housekeeping"
    wiz_inst, silent_inst, suite_dir = Path(f"{base_inst}Wiz"), run_root / "silent_inst", base_inst.parent
    for d in (shots_dir, logs_dir, setup_data, hk_data, suite_dir):
        d.mkdir(parents=True, exist_ok=True)
    for d in (base_inst, wiz_inst, silent_inst):
        if d.exists():
            shutil.rmtree(d, ignore_errors=True)
    env = _build_sandbox_env(cfg)
    if build_test or auto_setup is None or not auto_setup.is_file():
        auto_setup = _build_ephemeral_test_setup(version, run_root / "test_build")

    rel_launcher, rel_setup = _resolve_release_probe_binaries(version, run_root / "release_probe")
    assert rel_launcher.is_file() and rel_setup.is_file()
    rc_app_rel, _, _ = _run_bounded([str(rel_launcher), "--automation-port", "51999", "--automation-token", "rel"], env, logs_dir, "rel_app_fail")
    rc_setup_rel, _, _ = _run_bounded([str(rel_setup), "--automation-port", "51999", "--automation-token", "rel"], env, logs_dir, "rel_setup_fail")
    assert rc_app_rel == 2 and rc_setup_rel == 2

    screenshots: list[dict[str, Any]] = []
    p1, t1 = _free_port(), uuid.uuid4().hex
    wo, we = (logs_dir / "wiz.stdout.log").open("w", encoding="utf-8"), (logs_dir / "wiz.stderr.log").open("w", encoding="utf-8")
    wp = subprocess.Popen([str(auto_setup), "--install-dir", str(base_inst), "--data-dir", str(setup_data), "--log-dir", str(logs_dir), "--automation-port", str(p1), "--automation-token", t1], env=env, stdout=wo, stderr=we, text=True)
    st_wiz = None
    try:
        st_wiz = start_exebox_live_stream(automation_port=p1, automation_token=t1, target_pid=wp.pid, snapshot_dir=run_root / "stream_wiz", udp_port=5004, fps=20)
        _wait_http_ok(f"http://127.0.0.1:{p1}/v1/health", token=t1)
        tree = _http_json(f"http://127.0.0.1:{p1}/v1/tree", token=t1)[1]["data"]
        tb, chk_l, chk_d, btn_i = _find_node(tree, "TextBox", str(base_inst), True, True), _find_node(tree, "CheckBox", "Launch"), _find_node(tree, "CheckBox", "Desktop"), _find_node(tree, "Button", "Install Now")
        assert tb and chk_l and chk_d and btn_i
        assert _http_json(f"http://127.0.0.1:{p1}/v1/pointer", "POST", {"action": "click", "targetId": chk_l["id"], "x": 2000, "y": 2000}, t1)[0] == 200
        assert _http_json(f"http://127.0.0.1:{p1}/v1/pointer", "POST", {"action": "click", "targetId": chk_d["id"], "x": 2000, "y": 2000}, t1)[0] == 200
        assert _http_json(f"http://127.0.0.1:{p1}/v1/keyboard", "POST", {"action": "type", "targetId": tb["id"], "text": "Wiz"}, t1)[0] == 200
        screenshots.append({"stage": "Setup Wizard GUI", **_capture_png_endpoint(p1, t1, shots_dir / "stage2_01_setup_wizard.png", 0.8)})
        assert _http_json(f"http://127.0.0.1:{p1}/v1/pointer", "POST", {"action": "click", "targetId": btn_i["id"]}, t1)[0] == 200
        dl, btn_fin = time.monotonic() + 300.0, None
        while time.monotonic() < dl:
            btn_fin = _find_node(_http_json(f"http://127.0.0.1:{p1}/v1/tree", token=t1)[1].get("data"), "Button", "Finish")
            if btn_fin and btn_fin.get("isEnabled"):
                break
            time.sleep(1.5)
        assert btn_fin and btn_fin.get("isEnabled")
        assert _http_json(f"http://127.0.0.1:{p1}/v1/pointer", "POST", {"action": "click", "targetId": btn_fin["id"]}, t1)[0] == 200
        assert wp.wait(timeout=15) == 0 and (wiz_inst / "Housekeeping.exe").is_file() and (wiz_inst / "app" / "Housekeeping.exe").is_file()
    finally:
        _terminate_tree(wp); stop_exebox_live_stream(st_wiz); wo.close(); we.close()

    rc_si, _, err_si = _run_bounded([str(auto_setup), "--silent", "--no-shortcuts", "--no-launch", "--profile", "full", "--suite-dir", str(suite_dir), "--install-dir", str(silent_inst), "--data-dir", str(setup_data), "--log-dir", str(logs_dir)], env, logs_dir, "silent_install", 240)
    assert rc_si == 0 and (silent_inst / "Housekeeping.exe").is_file(), err_si
    installed_exe = wiz_inst / "Housekeeping.exe"
    rc_doc, doc_out, _ = _run_bounded([str(installed_exe), "--doctor", "--data-dir", str(hk_data), "--log-dir", str(logs_dir)], env, logs_dir, "doctor")
    assert rc_doc == 0 and json.loads(doc_out).get("ok") is True

    sp, ap, tok = _free_port(), _free_port(), uuid.uuid4().hex
    so, se = (logs_dir / "gui.stdout.log").open("w", encoding="utf-8"), (logs_dir / "gui.stderr.log").open("w", encoding="utf-8")
    gp = subprocess.Popen([str(installed_exe), "--port", str(sp), "--data-dir", str(hk_data), "--log-dir", str(logs_dir), "--automation-port", str(ap), "--automation-token", tok], env=env, stdout=so, stderr=se, text=True)
    stm = None
    try:
        stm = start_exebox_live_stream(automation_port=ap, automation_token=tok, target_pid=gp.pid, snapshot_dir=run_root / "stream_gui", udp_port=5004, fps=20)
        _wait_http_ok(f"http://127.0.0.1:{sp}/api/v1/health"); _wait_http_ok(f"http://127.0.0.1:{ap}/v1/health", token=tok)
        mod_summary = _exercise_all_8_modules(f"http://127.0.0.1:{sp}", Path(cfg["dBackingDir"]) / "Stage2Sandbox")
        assert _http_json(f"http://127.0.0.1:{ap}/v1/keyboard", "POST", {"action": "press", "key": "F9", "targetId": "HousekeepingMainForm"}, tok)[0] == 200
        screenshots.append({"stage": "Desktop 1080p Dashboard", **_capture_png_endpoint(ap, tok, shots_dir / "stage2_02_desktop_1080p.png", 1.8, width_range=(1916, 1924))})
        assert _http_json(f"http://127.0.0.1:{ap}/v1/keyboard", "POST", {"action": "press", "key": "6", "targetId": "HousekeepingMainForm"}, tok)[0] == 200
        screenshots.append({"stage": "Desktop Settings View", **_capture_png_endpoint(ap, tok, shots_dir / "stage2_03_desktop_settings.png", 1.2, width_range=(1916, 1924))})
        assert _http_json(f"http://127.0.0.1:{ap}/v1/keyboard", "POST", {"action": "press", "key": "F8", "targetId": "HousekeepingMainForm"}, tok)[0] == 200
        screenshots.append({"stage": "Mobile Responsive WebView2", **_capture_png_endpoint(ap, tok, shots_dir / "stage2_04_mobile_webview.png", 1.5, width_range=(408, 416))})
    finally:
        _terminate_tree(gp); stop_exebox_live_stream(stm); so.close(); se.close()

    mp = _free_port()
    mo, me = (logs_dir / "managed.stdout.log").open("w", encoding="utf-8"), (logs_dir / "managed.stderr.log").open("w", encoding="utf-8")
    mproc = subprocess.Popen([str(installed_exe), "--managed-by-suite", "--suite-id", "mother", "--bind", "127.0.0.1", "--port", str(mp), "--data-dir", str(hk_data), "--log-dir", str(logs_dir)], env=env, stdout=mo, stderr=me, text=True)
    try:
        _wait_http_ok(f"http://127.0.0.1:{mp}/api/v1/health")
        mc, _, mbody = _http_raw(f"http://127.0.0.1:{mp}/manifest.webmanifest", headers={"X-Forwarded-Prefix": "/app/housekeeping"})
        sc, shdrs, _ = _http_raw(f"http://127.0.0.1:{mp}/sw.js", headers={"X-Forwarded-Prefix": "/app/housekeeping"})
        qc, qpayload = _http_json(f"http://127.0.0.1:{mp}/api/v1/system/tailscale-qr", headers={"X-Forwarded-Prefix": "/app/housekeeping"})
        assert mc == 200 and "/app/housekeeping" in mbody.decode("utf-8", errors="replace")
        assert sc == 200 and shdrs.get("service-worker-allowed", "").startswith("/app/housekeeping")
        assert qc == 200 and "<svg" in json.dumps(qpayload).lower()
    finally:
        _terminate_tree(mproc); mo.close(); me.close()

    diff = diff_host_snapshots(cfg, name="hk-v020-baseline")
    host_leaks = [x for k in ("addedFiles", "removedFiles", "modifiedFiles", "addedRegistryEntries", "removedRegistryEntries", "modifiedRegistryEntries") for x in diff.get(k, []) if ".tmp.driveupload" not in str(x).lower()]
    assert len(host_leaks) == 0, f"Host mutation detected: {host_leaks}"
    assert (host_rclone_before == (_snapshot_mtimes(Path(host_localappdata) / "rclone") if host_localappdata else {})), "Host %LOCALAPPDATA%\\rclone leaked!"
    assert host_prog_before == _snapshot_mtimes(Path(r"D:\Programs\Housekeeping")), "D:\\Programs\\Housekeeping mutated!"

    receipt = {
        "schemaVersion": 1, "appId": "housekeeping", "version": version, "executedAtUtc": utc_now_iso(),
        "threadDesktop": _get_thread_desktop(), "vlcUdp5004Bound": _is_udp_5004_bound(),
        "releaseAppAutomationExitCode": rc_app_rel, "releaseSetupAutomationExitCode": rc_setup_rel,
        "silentSuiteInstallExitCode": rc_si, "doctorExitCode": rc_doc,
        "hostDiffCount": len(host_leaks), "modulesExercised": mod_summary, "screenshots": screenshots,
    }
    r_path = DEV_ROOT / "docs" / "reports" / f"{version}.stage2-sandbox-e2e-receipt.json"
    r_path.parent.mkdir(parents=True, exist_ok=True)
    r_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return receipt


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Housekeeping v0.2.0 Stage 2 4-Drive Sandbox E2E")
    ap.add_argument("--auto-setup", type=Path, default=None)
    ap.add_argument("--shots-dir", type=Path, default=DEV_ROOT / "logs" / "stage2_shots")
    ap.add_argument("--build-test", action="store_true")
    args = ap.parse_args(argv)
    print(json.dumps(run_stage2_e2e(args.auto_setup, args.shots_dir, args.build_test), indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
