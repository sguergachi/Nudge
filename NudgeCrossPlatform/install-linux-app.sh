#!/usr/bin/env bash
# Install Nudge for the current user — run from the extracted release directory.
# Creates a .desktop entry and optionally symlinks binaries to ~/.local/bin/.
set -euo pipefail

APP_NAME="Nudge"
DESKTOP_ID="nudge.desktop"
BIN_SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Ensure the self-contained tray binary exists
if [[ ! -f "${BIN_SRC}/nudge-tray" ]]; then
    echo "Error: nudge-tray not found in ${BIN_SRC}"
    echo "Run this script from the extracted release directory."
    exit 1
fi

echo "=========================================="
echo "  Installing ${APP_NAME}"
echo "=========================================="

# ── Optionally symlink binaries to ~/.local/bin/ ──────────────────────────
LOCAL_BIN="${HOME}/.local/bin"
if [[ -d "${LOCAL_BIN}" ]] || mkdir -p "${LOCAL_BIN}" 2>/dev/null; then
    if [[ ":$PATH:" != *":${LOCAL_BIN}:"* ]]; then
        echo "  Adding ${LOCAL_BIN} to PATH (add to your shell rc file for persistence)"
        export PATH="${LOCAL_BIN}:${PATH}"
    fi
    ln -sf "${BIN_SRC}/nudge-tray"    "${LOCAL_BIN}/nudge-tray"
    ln -sf "${BIN_SRC}/nudge"         "${LOCAL_BIN}/nudge"
    ln -sf "${BIN_SRC}/nudge-notify"  "${LOCAL_BIN}/nudge-notify"
    echo "  ✓ Symlinked binaries to ${LOCAL_BIN}"
fi

# ── Desktop entry ──────────────────────────────────────────────────────────
LAUNCHER_DIR="${HOME}/.local/share/applications"
ICON_DIR="${HOME}/.local/share/icons/hicolor/scalable/apps"
DESKTOP_FILE="${LAUNCHER_DIR}/${DESKTOP_ID}"

mkdir -p "${LAUNCHER_DIR}" "${ICON_DIR}"

cat > "${DESKTOP_FILE}" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=${APP_NAME}
Comment=Tray-based productivity tracker with AI-powered nudge detection
Exec=${BIN_SRC}/nudge-tray
Icon=nudge
Terminal=false
Categories=Utility;Office;
StartupNotify=false
X-GNOME-UsesNotifications=true
Keywords=productivity;tracker;tray;analytics;
EOF

# Install icon
if [[ -f "${BIN_SRC}/assets/linux/nudge.svg" ]]; then
    install -m 0644 "${BIN_SRC}/assets/linux/nudge.svg" "${ICON_DIR}/nudge.svg"
    echo "  ✓ Icon installed"
elif [[ -f "${BIN_SRC}/nudge.svg" ]]; then
    install -m 0644 "${BIN_SRC}/nudge.svg" "${ICON_DIR}/nudge.svg"
    echo "  ✓ Icon installed"
else
    echo "  ⚠ No icon found at assets/linux/nudge.svg — skipping"
fi

# Refresh desktop database
if command -v update-desktop-database &>/dev/null; then
    update-desktop-database "${LAUNCHER_DIR}" &>/dev/null || true
fi
if command -v gtk-update-icon-cache &>/dev/null; then
    gtk-update-icon-cache "${HOME}/.local/share/icons/hicolor" &>/dev/null || true
fi

echo ""
echo "=========================================="
echo "  ✓ ${APP_NAME} installed"
echo "=========================================="
echo ""
echo "  Launch from app menu: ${APP_NAME}"
echo "  Or run:               ${BIN_SRC}/nudge-tray"
echo ""
echo "  Uninstall:"
echo "    rm ${DESKTOP_FILE}"
echo "    rm -rf ${BIN_SRC}"
echo "    rm -f ${LOCAL_BIN}/nudge-tray ${LOCAL_BIN}/nudge ${LOCAL_BIN}/nudge-notify"
echo ""
