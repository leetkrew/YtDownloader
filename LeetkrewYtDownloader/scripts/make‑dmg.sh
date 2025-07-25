#!/usr/bin/env bash
set -e

APP_NAME="LeetkrewYtDownloader"
VOL_NAME="Leetkrew YouTube Downloader"
TMP_DMG="${APP_NAME}.tmp.dmg"
FINAL_DMG="${APP_NAME}.dmg"
SRC_APP="${APP_NAME}.app"
MOUNT_DIR="/Volumes/${VOL_NAME}"

# 1) Create a read/write image
hdiutil create \
  -volname "$VOL_NAME" \
  -srcfolder "$SRC_APP" \
  -ov \
  -format UDRW \
  "$TMP_DMG"

# 2) Attach it at exactly our mountpoint
hdiutil attach "$TMP_DMG" \
  -mountpoint "$MOUNT_DIR" \
  -nobrowse \
  -readwrite

# 3) (Optional) Add Applications shortcut
ln -s /Applications "$MOUNT_DIR/Applications"

# 4) Detach and convert to a compressed, read‑only image
hdiutil detach "$MOUNT_DIR"
hdiutil convert "$TMP_DMG" \
  -format UDZO \
  -imagekey zlib-level=9 \
  -o "$FINAL_DMG"

# 5) Clean up
rm "$TMP_DMG"

echo "✅ Created $FINAL_DMG"
