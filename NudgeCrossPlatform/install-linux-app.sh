#!/usr/bin/env bash
set -euo pipefail

APP_NAME="Nudge"
DESKTOP_ID="nudge.desktop"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAUNCHER_DIR="${HOME}/.local/share/applications"
ICON_DIR="${HOME}/.local/share/icons/hicolor/scalable/apps"
DESKTOP_FILE="${LAUNCHER_DIR}/${DESKTOP_ID}"
ICON_FILE="${ICON_DIR}/nudge.svg"

mkdir -p "${LAUNCHER_DIR}" "${ICON_DIR}"

cat > "${DESKTOP_FILE}" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=${APP_NAME}
Comment=Tray-based productivity tracker
Exec=${SCRIPT_DIR}/launch-nudge.sh
Icon=nudge
Terminal=false
Categories=Utility;Office;
StartupNotify=false
X-GNOME-UsesNotifications=true
Keywords=productivity;tracker;tray;analytics;
EOF

install -m 0644 "${SCRIPT_DIR}/assets/linux/nudge.svg" "${ICON_FILE}"

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "${LAUNCHER_DIR}" >/dev/null 2>&1 || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache "${HOME}/.local/share/icons/hicolor" >/dev/null 2>&1 || true
fi

echo "Installed ${APP_NAME} launcher:"
echo "  ${DESKTOP_FILE}"
echo "Installed icon:"
echo "  ${ICON_FILE}"
echo
echo "You can now search for '${APP_NAME}' in KDE and pin it to the taskbar."
