#!/usr/bin/env bash
# Mac 版の PromptLoupe.app を作る(Linux・Mac のどちらでも動く)。
#   使い方: tools/make_macos_app.sh <dotnet publish の出力先> <.app を置くフォルダ> <バージョン>
# - 発行物を PromptLoupe.app/Contents/MacOS に入れ、Info.plist とアイコンを置く
# - rcodesign(apple-codesign)があれば、.app 全体にアドホック署名を付ける(Apple の証明書・公証はなし)。
#   Apple シリコンの Mac は、署名のないプログラムを起動しない。中の実行ファイルと dylib には、
#   .NET SDK と各パッケージがすでに署名を付けているが、.app としての署名はここで付ける
set -euo pipefail

publish="$1"
outdir="$2"
version="$3"
here="$(cd "$(dirname "$0")/.." && pwd)"

app="$outdir/PromptLoupe.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp -R "$publish"/. "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/PromptLoupe"
# Contents/MacOS にはプログラムだけを置く決まり(署名の都合)。文書があれば Resources へ移す
# (今はライセンス文も exe に埋め込んでいるので、ふつうは何もない)
find "$app/Contents/MacOS" -maxdepth 1 -type f \( -name '*.txt' -o -name 'LICENSE' \) -exec mv {} "$app/Contents/Resources/" \;
sed "s/@VERSION@/$version/g" "$here/PromptLoupe.Avalonia/macos/Info.plist" > "$app/Contents/Info.plist"
cp "$here/PromptLoupe.Avalonia/macos/PromptLoupe.icns" "$app/Contents/Resources/"

if command -v rcodesign >/dev/null 2>&1; then
  rcodesign sign "$app"
else
  echo "rcodesign がないので、.app としての署名は付けていません" >&2
fi
