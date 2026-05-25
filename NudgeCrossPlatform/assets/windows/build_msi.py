#!/usr/bin/env python3
"""Build Nudge MSI using Python's built-in msilib module."""

import traceback, msilib, os, sys, uuid
from pathlib import Path

try:
    VERSION = os.environ.get("NUDGE_VERSION", "1.5.3")
    SCRIPT_DIR = Path(__file__).resolve().parent
    REPO_DIR = SCRIPT_DIR.parent.parent.parent
    DIST_DIR = REPO_DIR / "dist" / "win-x64"

    if not DIST_DIR.exists():
        print(f"ERROR: {DIST_DIR} not found", flush=True)
        sys.exit(1)

    msi_path = str(REPO_DIR / "dist" / f"Nudge-Setup-{VERSION}.msi")
    print(f"Building {msi_path}...", flush=True)

    PRODUCT_CODE = "{" + str(uuid.uuid4()).upper() + "}"
    UPGRADE_CODE = "{C9E4B8D1-5A3F-4E7B-9C6D-2F8A1B4E3C7D}"

    db = msilib.init_database(msi_path, msilib.MSI_VERSION, msilib.UUID(1033))
    msilib.add_tables(db, msilib.StandardTables())

    # Set properties manually
    db.Execute(f"INSERT INTO `Property` (`Property`, `Value`) VALUES ('ProductCode', '{PRODUCT_CODE}')")
    db.Execute(f"INSERT INTO `Property` (`Property`, `Value`) VALUES ('ProductVersion', '{VERSION}')")
    db.Execute(f"INSERT INTO `Property` (`Property`, `Value`) VALUES ('ProductName', 'Nudge')")
    db.Execute(f"INSERT INTO `Property` (`Property`, `Value`) VALUES ('ProductManufacturer', 'Nudge')")
    db.Execute(f"INSERT INTO `Property` (`Property`, `Value`) VALUES ('ProductLanguage', '1033')")
    db.Execute(f"INSERT INTO `Property` (`Property`, `Value`) VALUES ('UpgradeCode', '{UPGRADE_CODE}')")

    # Directory structure
    db.Directory("TARGETDIR", "SourceDir")
    db.Directory("ProgramFiles64Folder", "ProgramFiles64Folder", "Program Files")
    db.Directory("INSTALLFOLDER", "INSTALLFOLDER", "Nudge")
    db.Directory("ProgramMenuFolder", "ProgramMenuFolder")
    db.Directory("DesktopFolder", "DesktopFolder")

    # Files
    files = [
        ("nudge-tray.exe",     DIST_DIR / "nudge-tray.exe"),
        ("nudge.exe",          DIST_DIR / "nudge.exe"),
        ("nudge-notify.exe",   DIST_DIR / "nudge-notify.exe"),
        ("model_inference.py", REPO_DIR / "model_inference.py"),
        ("train_model.py",     REPO_DIR / "train_model.py"),
        ("background_trainer.py", REPO_DIR / "background_trainer.py"),
        ("requirements-cpu.txt", REPO_DIR / "requirements-cpu.txt"),
        ("requirements.txt",    REPO_DIR / "requirements.txt"),
    ]

    comp = msilib.Component(db, "ProductComponent", "INSTALLFOLDER", "A1B2C3D4-E5F6-7890-ABCD-EF1234567890")
    for name, src in files:
        if not src.exists():
            print(f"  WARN: {src} not found", flush=True)
            continue
        comp.File(None, name, str(src), msilib.msidbFileAttributesVital)

    # Feature
    feature = msilib.Feature(db, "ProductFeature", "Nudge", "Nudge", 1)
    feature.add_component(comp.Id)

    # Shortcuts via SQL
    tgt = "[#nudge-tray.exe]"
    db.Execute(f"INSERT INTO `Shortcut` (`Shortcut`, `Directory_`, `Name`, `Component_`, `Target`) VALUES ('StartMenuNudge', 'ProgramMenuFolder', 'Nudge', '{comp.Id}', '{tgt}')")
    db.Execute(f"INSERT INTO `Shortcut` (`Shortcut`, `Directory_`, `Name`, `Component_`, `Target`) VALUES ('DesktopNudge', 'DesktopFolder', 'Nudge', '{comp.Id}', '{tgt}')")

    # Registry
    db.Execute(f"INSERT INTO `Registry` (`Registry`, `Root`, `Key`, `Name`, `Value`, `Component_`) VALUES ('NudgeInstalled', -1, 'Software\\Nudge', 'installed', '1', '{comp.Id}')")

    # Major upgrade
    db.Execute(f"INSERT INTO `Upgrade` (`UpgradeCode`, `VersionMin`, `VersionMax`, `Attributes`, `ActionProperty`) VALUES ('{UPGRADE_CODE}', '0.0.0', '{VERSION}', 256+512+768, 'OLDVERSIONFOUND')")

    # Launch condition
    db.Execute("INSERT INTO `Condition` (`Condition`, `Description`, `Level`) VALUES ('VersionNT64', 'Nudge requires 64-bit Windows.', 3)")

    # Install sequence
    db.Execute("INSERT INTO `InstallExecuteSequence` (`Action`, `Sequence`, `Condition`) VALUES ('FindRelatedProducts', 25)")
    db.Execute("INSERT INTO `InstallExecuteSequence` (`Action`, `Sequence`) VALUES ('RemoveExistingProducts', 6600)")

    db.Commit()
    print(f"MSI built: {msi_path}", flush=True)
    size = os.path.getsize(msi_path) // 1024
    print(f"Size: {size} KB", flush=True)
except Exception as e:
    print(f"FATAL: {e}", flush=True)
    traceback.print_exc()
    sys.exit(1)
