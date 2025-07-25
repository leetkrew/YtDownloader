#!/usr/bin/env bash
set -euo pipefail

# If you want to pass the publish directory as an argument, default to cwd
publishDir="${1:-$(pwd)}"
appName="LeetkrewYtDownloader"

# Compose the .app bundle path inside your publish folder
appBundle="$publishDir/${appName}.app"
contents="$appBundle/Contents"

# 1) create bundle layout
mkdir -p "$contents"/{MacOS,Resources}

# 2) copy your published executable
cp "$publishDir/$appName" "$contents/MacOS/$appName"
chmod +x "$contents/MacOS/$appName"

# 3) write Info.plist
cat > "$contents/Info.plist" <<EOF
<plist version="1.0">
  <dict>
    <!-- Basic identity -->
    <key>CFBundleName</key>
    <string>Leetkrew YT Downloader</string>
    <key>CFBundleDisplayName</key>
    <string>Leetkrew YouTube Downloader</string>
    <key>CFBundleIdentifier</key>
    <string>com.rjregalado.leetkrewytdownloader</string>

    <!-- Versioning -->
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>

    <!-- Executable info -->
    <key>CFBundleExecutable</key>
    <string>LeetkrewYtDownloader</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>

    <!-- Localization and minimum OS -->
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.13.0</string>

    <!-- High‑DPI support -->
    <key>NSHighResolutionCapable</key>
    <true/>

    <!-- App icon (if you bundle one) -->
    <!-- put LeetkrewIcon.icns into Resources/ -->
    <key>CFBundleIconFile</key>
    <string>LeetkrewIcon.icns</string>

    <!-- Supported platforms -->
    <key>CFBundleSupportedPlatforms</key>
    <array>
      <string>MacOSX</string>
    </array>
  </dict>
</plist>
EOF

echo "✔️  Built app bundle at: $appBundle"

