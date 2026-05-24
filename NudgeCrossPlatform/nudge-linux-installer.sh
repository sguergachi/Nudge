#!/usr/bin/env bash
# Nudge Linux Installer — self-contained, no extraction needed.
# Usage: chmod +x nudge-linux-installer.run && ./nudge-linux-installer.run
set -euo pipefail

APP_NAME="Nudge"
INSTALL_DIR="${HOME}/.local/share/nudge"
BIN_DIR="${HOME}/.local/bin"
DESKTOP_DIR="${HOME}/.local/share/applications"
ICON_DIR="${HOME}/.local/share/icons/hicolor/scalable/apps"

echo "=========================================="
echo "  Nudge Installer"
echo "=========================================="
echo ""

# ── Extract embedded payload ──────────────────────────────────────────────
echo "  Extracting..."
ARCHIVE_LINE=$(awk '/^__ARCHIVE_BELOW__/ {print NR+1; exit 0; }' "$0")
tail -n+${ARCHIVE_LINE} "$0" | tar -xzf - -C /tmp
PAYLOAD="/tmp/nudge-install"
echo "  ✓ Extracted to ${PAYLOAD}"

# ── Verify ─────────────────────────────────────────────────────────────────
if [[ ! -f "${PAYLOAD}/nudge-tray" ]]; then
    echo "  ✗ nudge-tray not found in payload"
    exit 1
fi

# ── Install ────────────────────────────────────────────────────────────────
rm -rf "${INSTALL_DIR}"
mkdir -p "${INSTALL_DIR}" "${BIN_DIR}" "${DESKTOP_DIR}" "${ICON_DIR}"

cp -r "${PAYLOAD}"/* "${INSTALL_DIR}/"
chmod +x "${INSTALL_DIR}/nudge-tray" "${INSTALL_DIR}/nudge" "${INSTALL_DIR}/nudge-notify"

# Symlink to ~/.local/bin/
ln -sf "${INSTALL_DIR}/nudge-tray"   "${BIN_DIR}/nudge-tray"
ln -sf "${INSTALL_DIR}/nudge"        "${BIN_DIR}/nudge"
ln -sf "${INSTALL_DIR}/nudge-notify" "${BIN_DIR}/nudge-notify"
echo "  ✓ Binaries installed to ${INSTALL_DIR}"

# ── Desktop entry ──────────────────────────────────────────────────────────
cat > "${DESKTOP_DIR}/nudge.desktop" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=${APP_NAME}
Comment=Tray-based productivity tracker with AI-powered nudge detection
Exec=${BIN_DIR}/nudge-tray
Icon=nudge
Terminal=false
Categories=Utility;Office;
StartupNotify=false
Keywords=productivity;tracker;tray;analytics;
EOF

# ── Icon ───────────────────────────────────────────────────────────────────
if [[ -f "${INSTALL_DIR}/nudge.svg" ]]; then
    cp "${INSTALL_DIR}/nudge.svg" "${ICON_DIR}/nudge.svg"
elif [[ -f "${INSTALL_DIR}/assets/linux/nudge.svg" ]]; then
    cp "${INSTALL_DIR}/assets/linux/nudge.svg" "${ICON_DIR}/nudge.svg"
else
    # Generate a minimal SVG icon
    cat > "${ICON_DIR}/nudge.svg" <<'SVGEOF'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
  <rect x="2" y="2" width="28" height="28" rx="4" fill="#0B0B0E" stroke="#333" stroke-width="1"/>
  <text x="16" y="22" font-family="sans-serif" font-size="16" fill="#58A6FF" text-anchor="middle" font-weight="700">N</text>
</svg>
SVGEOF
fi
echo "  ✓ Desktop entry created"

# Refresh caches
if command -v update-desktop-database &>/dev/null; then
    update-desktop-database "${DESKTOP_DIR}" &>/dev/null || true
fi
if command -v gtk-update-icon-cache &>/dev/null; then
    gtk-update-icon-cache "${HOME}/.local/share/icons/hicolor" &>/dev/null || true
fi

# ── PATH check ─────────────────────────────────────────────────────────────
if [[ ":$PATH:" != *":${BIN_DIR}:"* ]]; then
    echo ""
    echo "  ⚠ Add ~/.local/bin to your PATH (not needed for app menu):"
    echo "     echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.bashrc"
fi

# ── Cleanup ────────────────────────────────────────────────────────────────
rm -rf "${PAYLOAD}"

echo ""
echo "=========================================="
echo "  ✓ Nudge installed successfully"
echo "=========================================="
echo ""
echo "  Launch from app menu:  ${APP_NAME}"
echo "  Or run:                ${BIN_DIR}/nudge-tray"
echo ""
echo "  ML features auto-install Python deps on first launch."
echo ""
echo "  Uninstall:  rm -rf ${INSTALL_DIR} ${BIN_DIR}/nudge-tray ${BIN_DIR}/nudge ${BIN_DIR}/nudge-notify ${DESKTOP_DIR}/nudge.desktop ${ICON_DIR}/nudge.svg"
echo ""

exit 0
__ARCHIVE_BELOW__
