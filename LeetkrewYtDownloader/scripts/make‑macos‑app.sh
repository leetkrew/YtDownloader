#!/usr/bin/env bash
set -euo pipefail

# $1 = path to your publish folder
publish_dir="$1"
# e.g. /…/bin/Release/net8.0/osx-x64/publish
if [[ ! -d "$publish_dir" ]]; then
  echo "Publish directory not found: $publish_dir" >&2
  exit 1
fi

# derive your app name from the base folder (or override manually)
app_name="$(basename "$(dirname "$publish_dir")")"  
# e.g. "LeetkrewYtDownloader"

bundle_name="${app_name}.app"
rm -rf "$bundle_name"

# Create .app structure
mkdir -p "$bundle_name/Contents/MacOS"
cp -R "$publish_dir/"* "$bundle_name/Contents/MacOS/"

# Write a minimal Info.plist
cat > "$bundle_name/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" \
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$app_name</string>
  <key>CFBundleExecutable</key>
  <string>$app_name</string>
  <key>CFBundleIdentifier</key>
  <string>com.yourcompany.$app_name</string>
  <key>CFBundleVersion</key>
  <string>1.0</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
</dict>
</plist>
EOF

echo "✅ Created macOS bundle: $bundle_name"
