#!/usr/bin/env bash
set -euo pipefail

case "${1:-}" in
    x64) machine=x86_64 ;;
    arm64) machine=arm64 ;;
    *) echo "Usage: $0 {x64|arm64}" >&2; exit 2 ;;
esac
if [[ $# != 1 ]]; then
    echo "Usage: $0 {x64|arm64}" >&2
    exit 2
fi
if [[ $(uname -s) != Darwin || $(uname -m) != "$machine" ]]; then
    echo "Build the macOS $1 installer on a native macOS $machine host." >&2
    exit 1
fi

cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
project=src/ThoriumInbox/ThoriumInbox.csproj
version=$(DOTNET_NOLOGO=1 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0 \
    dotnet msbuild "$project" -nologo -p:Configuration=Release -getProperty:Version)
if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Expected a three-part app version, got '$version'." >&2
    exit 1
fi

work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
stage="$work/package"
app="$stage/Thorium Inbox.app/Contents"
info="$app/Info.plist"
resources="$app/Resources"
iconset="$work/ThoriumInbox.iconset"
output="artifacts/test-installer/macos-$1"
filename="ThoriumInbox-macos-$1-Unsigned-Test.pkg"

mkdir -p "$app/MacOS" "$resources" "$iconset"
dotnet publish "$project" -c Release -r "osx-$1" --self-contained true \
    -p:RestoreLockedMode=true -p:DebugType=None -p:DebugSymbols=false -p:ThoriumTestBuild=true -o "$app/MacOS"
test -x "$app/MacOS/ThoriumInbox"
cp LICENSE NOTICE THIRD-PARTY-NOTICES.md "$resources"
cp -R licenses "$resources"

cp installer/macos/Info.plist "$info"
plutil -replace CFBundleShortVersionString -string "$version" "$info"
plutil -replace CFBundleVersion -string "$version" "$info"
identifier=$(plutil -extract CFBundleIdentifier raw -o - "$info")

for size in 16 32 128 256 512; do
    sips -z "$size" "$size" installer/linux/thorium-inbox.png \
        --out "$iconset/icon_${size}x${size}.png" > /dev/null
    sips -z "$((size * 2))" "$((size * 2))" installer/linux/thorium-inbox.png \
        --out "$iconset/icon_${size}x${size}@2x.png" > /dev/null
done
iconutil -c icns -o "$resources/ThoriumInbox.icns" "$iconset"

pkgbuild --root "$stage" --component-plist installer/macos/components.plist \
    --identifier "$identifier" --version "$version" --install-location /Applications "$work/$filename"

mkdir -p "$output"
mv "$work/$filename" "$output/"
cd "$output"
shasum -a 256 "$filename" > SHA256SUMS.txt
printf '%s/%s\n' "$PWD" "$filename"
