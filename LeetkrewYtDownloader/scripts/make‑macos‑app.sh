#!/usr/bin/env bash
set -euo pipefail

# 1. Where did we publish?
publishDir="${1:-$(pwd)}"

# 2. Internal exe name (must match CFBundleExecutable)
exeName="LeetkrewYtDownloader"

# 3. Friendly bundle name (what users see in Finder)
bundleFriendly="Leetkrew YouTube Downloader"

# 4. Compose bundle path
appBundle="$publishDir/${bundleFriendly}.app"
contents="$appBundle/Contents"
macosDir="$contents/MacOS"
resourcesDir="$contents/Resources"

# 5. Clean slate
rm -rf "$appBundle"

# 6. Build structure
mkdir -p "$macosDir" "$resourcesDir"

# 7. Copy in your published binary (keep its original name!)
cp "$publishDir/$exeName" "$macosDir/$exeName"
chmod +x "$macosDir/$exeName"

# 8. Info.plist
cat > "$contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>CFBundleName</key>
    <string>${bundleFriendly}</string>
    <key>CFBundleDisplayName</key>
    <string>${bundleFriendly}</string>
    <key>CFBundleIdentifier</key>
    <string>com.rjregalado.leetkrewytdownloader</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleExecutable</key>
    <string>${exeName}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.13.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>CFBundleIconFile</key>
    <string>LeetkrewIcon.icns</string>
    <key>CFBundleSupportedPlatforms</key>
    <array>
      <string>MacOSX</string>
    </array>
  </dict>
</plist>
EOF

# 9. (Optional) copy Resources like your ffmpeg folder or icons:
if [ -d "$publishDir/ffmpeg" ]; then
  cp -R "$publishDir/ffmpeg" "$resourcesDir/ffmpeg"
fi

# 10. Remove quarantine so Gatekeeper lets us run
xattr -cr "$appBundle"

echo "✔️  Built app bundle at: $appBundle"
