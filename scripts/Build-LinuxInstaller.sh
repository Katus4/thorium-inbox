#!/usr/bin/env bash
set -euo pipefail

if [[ $# != 0 ]]; then
    echo "Usage: $0" >&2
    exit 2
fi
if [[ $(uname -s) != Linux || $(uname -m) != x86_64 ]]; then
    echo 'Build the Debian installer on Linux x64.' >&2
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
payload="$stage/opt/thorium-inbox"
docs="$stage/usr/share/doc/thorium-inbox"
output=artifacts/test-installer/linux-x64
filename=ThoriumInbox-linux-x64-Unsigned-Test.deb
desktop=io.github.katus4.thoriuminbox.desktop

mkdir -p "$stage/DEBIAN" "$stage/usr/bin" "$payload" "$docs"
dotnet publish "$project" -c Release -r linux-x64 --self-contained true \
    -p:RestoreLockedMode=true -p:DebugType=None -p:DebugSymbols=false -p:ThoriumTestBuild=true -o "$payload"
test -x "$payload/ThoriumInbox"

ln -s /opt/thorium-inbox/ThoriumInbox "$stage/usr/bin/thorium-inbox"
install -Dm644 "installer/linux/$desktop" "$stage/usr/share/applications/$desktop"
install -Dm644 installer/linux/thorium-inbox.png \
    "$stage/usr/share/icons/hicolor/128x128/apps/thorium-inbox.png"
{
    cat NOTICE
    printf '\n'
    cat LICENSE
} > "$docs/copyright"
cp THIRD-PARTY-NOTICES.md "$docs"
cp -R licenses "$docs"

size=$(du -sk "$stage" | cut -f1)
sed -e "s/@VERSION@/$version/" -e "s/@INSTALLED_SIZE@/$size/" \
    installer/linux/control.in > "$stage/DEBIAN/control"
dpkg-deb --root-owner-group --build "$stage" "$work/$filename"

mkdir -p "$output"
mv "$work/$filename" "$output/"
cd "$output"
sha256sum "$filename" > SHA256SUMS.txt
printf '%s/%s\n' "$PWD" "$filename"
