#!/usr/bin/env bash
set -euo pipefail

# 0) tweak these if you renamed your .app
APP_NAME="Leetkrew YouTube Downloader"
VOLUME_NAME="LeetkrewYTDL"                      # avoid spaces here
SRC_APP="${APP_NAME}.app"
TMP_DMG="${VOLUME_NAME}.tmp.dmg"
FINAL_DMG="${APP_NAME}.dmg"
MOUNT_POINT="/Volumes/${VOLUME_NAME}"

# 1) Create a read‑write DMG
hdiutil create \
  -volname "${VOLUME_NAME}" \
  -srcfolder "${SRC_APP}" \
  -ov \
  -format UDRW \
  "${TMP_DMG}"

# 2) Attach it, mount it read/write, capture the device node
DEVICE=$(hdiutil attach \
  "${TMP_DMG}" \
  -mountpoint "${MOUNT_POINT}" \
  -nobrowse \
  -readwrite \
  | awk -v mp="${MOUNT_POINT}" '$NF == mp { print $1 }'
)

if [[ -z "${DEVICE}" ]]; then
  echo "✘ Failed to find device for ${MOUNT_POINT}" >&2
  exit 1
fi

# 3) (Optional) Symlink to /Applications inside the volume
ln -sf /Applications "${MOUNT_POINT}/Applications"

# 4) Unmount first, then detach
hdiutil unmount "${MOUNT_POINT}" || true
hdiutil detach "${DEVICE}" -force

# 5) Convert to compressed, read‑only UDZO
hdiutil convert \
  "${TMP_DMG}" \
  -format UDZO \
  -imagekey zlib-level=9 \
  -o "${FINAL_DMG}"

# 6) Clean up the temp image
rm -f "${TMP_DMG}"

echo "✅ Created ${FINAL_DMG}"
