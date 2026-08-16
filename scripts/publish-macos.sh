#!/usr/bin/env bash
set -euo pipefail

rid="${1:-osx-arm64}"
case "$rid" in
  osx-arm64|osx-x64) ;;
  *)
    echo "usage: $0 [osx-arm64|osx-x64]" >&2
    exit 2
    ;;
esac

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
artifact_root="$repo_root/artifacts/$rid"
publish_dir="$artifact_root/publish"
app_dir="$artifact_root/InfiniteLizards.app"
executable="InfiniteLizards.Desktop"
dotnet_executable="${DOTNET_EXECUTABLE:-dotnet}"
entitlements="$repo_root/scripts/macos-entitlements.plist"
adhoc_entitlements="$repo_root/scripts/macos-entitlements-adhoc.plist"

publish_args=(
  "$repo_root/src/InfiniteLizards.Desktop/InfiniteLizards.Desktop.csproj"
  -c Release
  -r "$rid"
  --self-contained true
  -p:DebugType=None
  -p:DebugSymbols=false
  -o "$publish_dir"
)
if [[ "${PUBLISH_NO_RESTORE:-0}" == "1" ]]; then
  publish_args+=(
    --no-restore
    --disable-build-servers
    -m:1
    -p:NuGetAudit=false
    -p:RestoreIgnoreFailedSources=true
    -p:UsedAvaloniaProducts=
  )
fi

"$dotnet_executable" publish "${publish_args[@]}"

rm -rf "$app_dir"
mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources"
cp -R "$publish_dir/." "$app_dir/Contents/MacOS/"

cat > "$app_dir/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>zh_CN</string>
  <key>CFBundleDisplayName</key>
  <string>Infinite Lizards</string>
  <key>CFBundleExecutable</key>
  <string>InfiniteLizards.Desktop</string>
  <key>CFBundleIdentifier</key>
  <string>com.infinitelizards.desktop</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>Infinite Lizards</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>2.0.0</string>
  <key>CFBundleVersion</key>
  <string>2.0.0</string>
  <key>LSMinimumSystemVersion</key>
  <!-- .NET 8's self-contained macOS runtime is linked with a macOS 11.0
       deployment target. Do not advertise an older version than the native
       host/runtime can actually launch on. -->
  <string>11.0</string>
  <key>LSUIElement</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST

chmod +x "$app_dir/Contents/MacOS/$executable"
identity="${CODESIGN_IDENTITY:--}"
if [[ "$identity" == "-" ]]; then
  entitlements="$adhoc_entitlements"
fi

native_sign_args=(
  --force
  --sign "$identity"
  --options runtime
)
if [[ "$identity" != "-" ]]; then
  native_sign_args+=(--timestamp)
fi

# `codesign --deep` can validate a bundle while leaving self-contained .NET
# dylibs with signatures that hardened runtime library validation rejects at
# launch. .NET keeps its managed assemblies and runtime metadata beside the
# apphost in Contents/MacOS; codesign treats every regular file there as a
# nested code object. Sign each one explicitly, then seal the app bundle last.
while IFS= read -r -d '' candidate; do
  if [[ "$candidate" == "$app_dir/Contents/MacOS/$executable" ]]; then
    # Signing CFBundleExecutable directly from inside its bundle makes
    # codesign validate the not-yet-sealed outer bundle. The final bundle
    # signature below signs this executable with the required entitlements.
    continue
  fi
  codesign "${native_sign_args[@]}" "$candidate"
done < <(find "$app_dir/Contents/MacOS" -type f -print0)

app_sign_args=(
  "${native_sign_args[@]}"
  --entitlements "$entitlements"
)
codesign "${app_sign_args[@]}" "$app_dir"
codesign --verify --deep --strict "$app_dir"

echo "$app_dir"
