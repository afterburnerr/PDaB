#!/usr/bin/env python3
"""Package a built Linux player and complete Unity sources; never publishes."""
from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import json
import os
from pathlib import Path
import re
import stat
import tarfile
import zipfile

SOURCE_ROOTS = ("Assets", "Packages", "ProjectSettings", "Tools", "Docs")
ROOT_FILES = ("README.md", ".gitignore", ".gitattributes")
EXCLUDED_PARTS = {"Library", "Temp", "Logs", "obj", "bin", "UserSettings", "Backups", "Builds",
                  "__pycache__", "venv", "node_modules"}
EXCLUDED_PREFIXES = ("Tools/Art/PodRideResearch/",)
TEXT_SUFFIXES = {".cs", ".py", ".json", ".md", ".txt", ".sh", ".yaml", ".yml", ".asset", ".prefab", ".unity"}
SECRET_PATTERNS = (
    re.compile(rb"gh[pousr]_[A-Za-z0-9]{20,}"),
    re.compile(rb"github_pat_[A-Za-z0-9_]{30,}"),
    re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"),
    re.compile(rb"\b[0-9]{8,12}:[A-Za-z0-9_-]{30,}\b"),
)


def digest(stream) -> str:
    result = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
        result.update(chunk)
    return result.hexdigest()


def file_hash(path: Path) -> str:
    with path.open("rb") as stream:
        return digest(stream)


def excluded(relative: Path) -> bool:
    parts = relative.parts
    name = relative.name
    posix = relative.as_posix()
    return (any(part in EXCLUDED_PARTS or part.startswith(".") for part in parts)
            or any(posix.startswith(prefix) for prefix in EXCLUDED_PREFIXES)
            or bool(re.search(r"\.blend(?:[0-9]+|@)$", name))
            or name.endswith((".log", ".pyc", ".pyo", ".session", ".sqlite", ".sqlite3", ".db"))
            or name.lower().startswith(("credentials", "secrets", "private_key")))


def source_files(project: Path) -> list[Path]:
    selected = []
    for name in ROOT_FILES:
        path = project / name
        if path.is_symlink():
            raise ValueError(f"Root source file is a symlink: {name}")
        if path.is_file():
            selected.append(path)
    for root in SOURCE_ROOTS:
        directory = project / root
        if directory.is_symlink():
            raise ValueError(f"Source root is a symlink: {root}")
        if not directory.exists():
            if root != "Docs":
                raise ValueError(f"Missing required project directory: {root}")
            continue
        for current, directories, files in os.walk(directory, followlinks=False):
            current_path = Path(current)
            kept = []
            for name in sorted(directories):
                path = current_path / name
                if excluded(path.relative_to(project)):
                    continue
                if path.is_symlink():
                    raise ValueError(f"Source directory is a symlink: {path.relative_to(project)}")
                kept.append(name)
            directories[:] = kept
            for name in sorted(files):
                path = current_path / name
                if not excluded(path.relative_to(project)):
                    if path.is_symlink():
                        raise ValueError(f"Source file is a symlink: {path.relative_to(project)}")
                    selected.append(path)
    return sorted(selected, key=lambda path: path.relative_to(project).as_posix())


def audit_sources(project: Path, paths: list[Path]) -> dict:
    required = ("Assets/Scenes/SampleScene.unity", "Packages/manifest.json", "Packages/packages-lock.json",
                "ProjectSettings/ProjectVersion.txt", "Tools/Art/EntranceLamp/EntranceLamp.blend",
                "Tools/Art/Stalker/Stalker.blend")
    selected = {path.relative_to(project).as_posix() for path in paths}
    missing = [name for name in required if name not in selected]
    if missing:
        raise ValueError("Missing required source files: " + ", ".join(missing))
    suspicious = []
    sizes = []
    for path in paths:
        size = path.stat().st_size
        relative = path.relative_to(project).as_posix()
        sizes.append((size, relative))
        if path.suffix.lower() in TEXT_SUFFIXES and size <= 2 * 1024 * 1024:
            content = path.read_bytes()
            if any(pattern.search(content) for pattern in SECRET_PATTERNS):
                suspicious.append(relative)
    if suspicious:
        raise ValueError("Credential-like content found; inspect these files: " + ", ".join(suspicious))
    return {"source_files": len(paths), "source_bytes": sum(size for size, _ in sizes),
            "largest_files": [{"path": name, "bytes": size} for size, name in sorted(sizes, reverse=True)[:12]],
            "github_lfs_required": [name for size, name in sizes if size > 100 * 1024 * 1024],
            "blend_sources": sorted(name for name in selected if name.endswith(".blend")),
            "exclusions": [*EXCLUDED_PREFIXES, "hidden tooling/cache directories", "*.log", "*.blend1, *.blend2, ..."]}


def entries_for(base: Path, paths: list[Path]) -> list[dict]:
    return [{"path": path.relative_to(base).as_posix(), "bytes": path.stat().st_size,
             "sha256": file_hash(path), "mode": 0o755 if path.stat().st_mode & 0o111 else 0o644}
            for path in paths]


def manifest_bytes(kind: str, entries: list[dict]) -> bytes:
    return (json.dumps({"product": "PDaB", "kind": kind, "sha256": True,
                       "files": entries}, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def zip_source(project: Path, paths: list[Path], destination: Path) -> dict:
    entries = entries_for(project, paths)
    payload = manifest_bytes("Unity source", entries)
    with zipfile.ZipFile(destination, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path, entry in zip(paths, entries):
            info = zipfile.ZipInfo("PDaB/" + entry["path"], date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = (stat.S_IFREG | entry["mode"]) << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            with path.open("rb") as source, archive.open(info, "w") as target:
                for chunk in iter(lambda: source.read(1024 * 1024), b""):
                    target.write(chunk)
        info = zipfile.ZipInfo("PDaB/RELEASE-MANIFEST.json", date_time=(1980, 1, 1, 0, 0, 0))
        info.create_system = 3
        info.external_attr = (stat.S_IFREG | 0o644) << 16
        info.compress_type = zipfile.ZIP_DEFLATED
        archive.writestr(info, payload)
    verify_zip(destination, entries)
    return {"path": str(destination), "files": len(entries) + 1,
            "bytes": destination.stat().st_size, "sha256": file_hash(destination)}


def verify_zip(path: Path, entries: list[dict]) -> None:
    with zipfile.ZipFile(path) as archive:
        if len(archive.infolist()) != len(entries) + 1 or archive.testzip() is not None:
            raise ValueError("Source ZIP integrity/file count failed")
        for entry in entries:
            name = "PDaB/" + entry["path"]
            with archive.open(name) as stream:
                if digest(stream) != entry["sha256"]:
                    raise ValueError("Source ZIP hash mismatch: " + name)


def tar_build(build: Path, destination: Path, templates: Path) -> dict:
    for required in ("PDaB.x86_64", "UnityPlayer.so", "PDaB_Data"):
        if not (build / required).exists():
            raise ValueError("Incomplete Linux player: missing " + required)
    for name in ("run.sh", "README_RU.txt"):
        target = build / name
        content = (templates / name).read_bytes()
        if target.exists() and target.read_bytes() != content:
            raise ValueError("Refusing to overwrite different build support file: " + str(target))
        target.write_bytes(content)
    (build / "run.sh").chmod(0o755)
    (build / "PDaB.x86_64").chmod(0o755)
    paths = []
    for path in sorted(build.rglob("*")):
        if any(part.endswith("_BackUpThisFolder_ButDontShipItWithYourGame") for part in path.relative_to(build).parts):
            continue
        if path.is_symlink():
            raise ValueError("Unexpected symlink in Linux build: " + str(path))
        if path.is_file():
            paths.append(path)
    entries = entries_for(build, paths)
    payload = manifest_bytes("Linux x86_64 player", entries)
    with destination.open("xb") as output, gzip.GzipFile(fileobj=output, mode="wb", filename="", mtime=0, compresslevel=6) as compressed:
        with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive:
            for path, entry in zip(paths, entries):
                info = tarfile.TarInfo("PDaB-Linux/" + entry["path"])
                info.size, info.mode, info.mtime = entry["bytes"], entry["mode"], 0
                with path.open("rb") as stream:
                    archive.addfile(info, stream)
            info = tarfile.TarInfo("PDaB-Linux/RELEASE-MANIFEST.json")
            info.size, info.mode, info.mtime = len(payload), 0o644, 0
            archive.addfile(info, io.BytesIO(payload))
    with tarfile.open(destination, "r:gz") as archive:
        if len(archive.getmembers()) != len(entries) + 1:
            raise ValueError("Linux archive file count failed")
        for entry in entries:
            member = archive.getmember("PDaB-Linux/" + entry["path"])
            stream = archive.extractfile(member)
            if stream is None or digest(stream) != entry["sha256"] or member.mode != entry["mode"]:
                raise ValueError("Linux archive hash/mode mismatch: " + entry["path"])
    return {"path": str(destination), "files": len(entries) + 1,
            "bytes": destination.stat().st_size, "sha256": file_hash(destination)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--audit", action="store_true", help="Read-only source audit; create no archives")
    parser.add_argument("--build-dir", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    project = args.project.resolve()
    paths = source_files(project)
    audit = audit_sources(project, paths)
    if args.audit:
        print(json.dumps(audit, ensure_ascii=False, indent=2))
        return
    if args.build_dir is None or args.output is None:
        parser.error("Packaging requires --build-dir and --output (or use --audit)")
    build, output = args.build_dir.resolve(), args.output.resolve()
    if not build.is_dir():
        parser.error("--build-dir does not exist")
    if output == project or project in output.parents:
        parser.error("Write release output outside the Unity project")
    output.mkdir(parents=True, exist_ok=True)
    source_archive = output / "PDaB-Unity-Source.zip"
    linux_archive = output / "PDaB-Linux-x86_64.tar.gz"
    if source_archive.exists() or linux_archive.exists():
        raise ValueError("Archive exists; use a new output directory instead of overwriting a release")
    linux = tar_build(build, linux_archive, project / "Tools/Release")
    source = zip_source(project, paths, source_archive)
    checksums = output / "SHA256SUMS.txt"
    checksums.write_text("".join(item["sha256"] + "  " + Path(item["path"]).name + "\n" for item in (linux, source)), encoding="utf-8")
    result = {"audit": audit, "linux": linux, "source": source, "checksums": str(checksums), "verified": True}
    (output / "ArchiveReport.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
