#!/usr/bin/env bash
set -euo pipefail

release_tag="${1:?Usage: build-release-assets.sh <release-tag> <rid>}"
rid="${2:?Usage: build-release-assets.sh <release-tag> <rid>}"
project="src/Transiever.ThunderbirdResiever.Cli/Transiever.ThunderbirdResiever.Cli.csproj"
binary="tbrx"

[[ "$release_tag" == v* ]] || { echo "Release tag must start with v: $release_tag" >&2; exit 2; }
case "$rid" in
  win-x64|linux-x64) ;;
  *) echo "Unsupported runtime identifier: $rid" >&2; exit 2 ;;
esac

version="${release_tag#v}"
output="artifacts/publish/$rid"
mkdir -p "$output"

dotnet publish "$project" --configuration Release --runtime "$rid" --self-contained true \
  -p:PublishAot=true -p:PublishTrimmed=true -p:PublishSingleFile=true \
  -p:InvariantGlobalization=false -p:TreatWarningsAsErrors=true -p:Version="$version" \
  --output "$output"

if [[ "$rid" == win-* ]]; then
  executable="$output/$binary.exe"
  archive="artifacts/$binary-$release_tag-$rid.zip"
else
  executable="$output/$binary"
  archive="artifacts/$binary-$release_tag-$rid.tar.gz"
fi

test -f "$executable"
format="$(file -b "$executable")"
case "$rid" in
  win-x64) [[ "$format" == PE32+\ executable* && "$format" == *x86-64* ]] ;;
  linux-x64) [[ "$format" == ELF\ 64-bit* && "$format" == *x86-64* ]] ;;
esac || { echo "Unexpected executable format for $rid: $format" >&2; exit 1; }

if [[ "$rid" == win-* ]]; then
  pwsh -NoLogo -NoProfile -Command "Compress-Archive -LiteralPath '$executable' -DestinationPath '$archive' -Force"
  entries="$(unzip -Z1 "$archive")"
else
  tar -czf "$archive" -C "$output" "$binary"
  entries="$(tar -tzf "$archive")"
fi

[[ "$entries" == "$(basename "$executable")" ]] || {
  echo "Archive must contain only $(basename "$executable"): $entries" >&2
  exit 1
}
