#!/usr/bin/env python3
"""Build Nudge MSI installer using Python's built-in msilib module."""

import msilib, os, sys, uuid
from pathlib import Path

VERSION = os.environ.get("NUDGE_VERSION", "1.5.3")
SCRIPT_DIR = Path(__file__).resolve().parent  # assets/windows/
PROJECT_DIR = SCRIPT_DIR.parent.parent        # NudgeCrossPlatform/
REPO_DIR = PROJECT_DIR.parent                 # repo root
DIST_DIR = REPO_DIR / "dist" / "win-x64"
UPGRADE_CODE = "{C9E4B8D1-5A3F-4E7B-9C6D-2F8A1B4E3C7D}"

if not DIST_DIR.exists():
    print(f"ERROR: {DIST_DIR} not found", flush=True)
    sys.exit(1)

msi_path = str(REPO_DIR / "dist" / f"Nudge-Setup-{VERSION}.msi")
print(f"Building {msi_path}...", flush=True)

# ── Initialize MSI database ───────────────────────────────────────────────
db = msilib.init_database(
    msi_path,
    msilib.MSI_VERSION,
    msilib.UUID(1033),
    "{" + str(uuid.uuid4()).upper() + "}",
    VERSION,
    "Nudge",
    "Nudge",
    "Nudge",
    "Nudge",
    "Nudge",
    UPGRADE_CODE
)
msilib.add_tables(db, msilib.StandardTables())

# ── Directory structure ──────────────────────────────────────────────────
db.Directory("TARGETDIR", "SourceDir")
db.Directory("ProgramFiles64Folder", "ProgramFiles64Folder", "Program Files")
db.Directory("INSTALLFOLDER", "INSTALLFOLDER", "Nudge")
db.Directory("ProgramMenuFolder", "ProgramMenuFolder")
db.Directory("DesktopFolder", "DesktopFolder")

# ── Files ─────────────────────────────────────────────────────────────────
files = [
    ("nudge-tray.exe",     DIST_DIR / "nudge-tray.exe"),
    ("nudge.exe",          DIST_DIR / "nudge.exe"),
    ("nudge-notify.exe",   DIST_DIR / "nudge-notify.exe"),
    ("model_inference.py", PROJECT_DIR / "model_inference.py"),
    ("train_model.py",     PROJECT_DIR / "train_model.py"),
    ("background_trainer.py", PROJECT_DIR / "background_trainer.py"),
    ("requirements-cpu.txt", PROJECT_DIR / "requirements-cpu.txt"),
    ("requirements.txt",    PROJECT_DIR / "requirements.txt"),
]

component = msilib.Component(db, "ProductComponent", "INSTALLFOLDER", "A1B2C3D4-E5F6-7890-ABCD-EF1234567890")
for name, src in files:
    if not src.exists():
        print(f"  WARNING: {src} not found, skipping", flush=True)
        continue
    component.File(None, name, str(src), msilib.msidbFileAttributesVital)

# ── Feature ───────────────────────────────────────────────────────────────
feature = msilib.Feature(db, "ProductFeature", "Nudge", "Nudge", 1)
feature.add_component(component.Id)

# ── Shortcuts ─────────────────────────────────────────────────────────────
# Use raw SQL for shortcuts  
tgt = "[#nudge-tray.exe]"
db.Execute(f"INSERT INTO `Shortcut` (`Shortcut`, `Directory_`, `Name`, `Component_`, `Target`) VALUES ('StartMenuNudge', 'ProgramMenuFolder', 'Nudge', '{component.Id}', '{tgt}')")
db.Execute(f"INSERT INTO `Shortcut` (`Shortcut`, `Directory_`, `Name`, `Component_`, `Target`) VALUES ('DesktopNudge', 'DesktopFolder', 'Nudge', '{component.Id}', '{tgt}')")

# ── Uninstall registry cleanup ───────────────────────────────────────────
db.Execute("INSERT INTO `Registry` (`Registry`, `Root`, `Key`, `Name`, `Value`, `Component_`) VALUES ('NudgeInstalled', -1, 'Software\\Nudge', 'installed', '1', '{component.Id}')")

# ── Major upgrade ─────────────────────────────────────────────────────────
db.Execute(f"INSERT INTO `Upgrade` (`UpgradeCode`, `VersionMin`, `VersionMax`, `Attributes`, `ActionProperty`) VALUES ('{UPGRADE_CODE}', '0.0.0', '{VERSION}', 256+512+768, 'OLDVERSIONFOUND')")

# ── Launch condition: 64-bit Windows required ────────────────────────────
db.Execute("INSERT INTO `Condition` (`Condition`, `Description`, `Level`) VALUES ('VersionNT64', 'Nudge requires 64-bit Windows.', 3)")

# ── Install execute sequence ──────────────────────────────────────────────
db.Execute("INSERT INTO `InstallExecuteSequence` (`Action`, `Sequence`, `Condition`) VALUES ('FindRelatedProducts', 25)")
db.Execute("INSERT INTO `InstallExecuteSequence` (`Action`, `Sequence`) VALUES ('RemoveExistingProducts', 6600)")

db.Commit()
print(f"MSI built: {msi_path}", flush=True)
print(f"Size: {os.path.getsize(msi_path) // 1024} KB", flush=True)
