#!/usr/bin/env bash
set -euo pipefail

# $1 = your .app bundle name (e.g. LeetkrewYtDownloader.app)
# $2 = desired .dmg output name (e.g. LeetkrewYtDownloader.dmg)
app_bundle="$1"
dmg_name="${2:-$(basename "$app_bundle" .app).dmg}"

if [[ ! -d "$app_bundle" ]]; then
  echo "App bundle not found: $app_bundle" >&2
  exit 1
fi

echo "🔨 Building DMG: $dmg_name"
hdiutil create \
  -volname "$(basename "$app_bundle" .app)" \
  -srcfolder "$app_bundle" \
  -format UDZO \
  -ov \
  "$dmg_name"

echo "✅ Created DMG: $dmg_name"
