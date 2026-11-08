#!/usr/bin/env bash
# rcodesign(Mac のプログラムに署名するツール。Linux で動く)を、版と SHA-256 を固定して /usr/local/bin に入れる。CI 用
# https://github.com/indygreg/apple-platform-rs
set -euo pipefail

version=0.29.0
sha256=dbe85cedd8ee4217b64e9a0e4c2aef92ab8bcaaa41f20bde99781ff02e600002
name="apple-codesign-$version-x86_64-unknown-linux-musl"

tmp="$(mktemp -d)"
curl -sSfL -o "$tmp/rcodesign.tar.gz" \
  "https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign%2F$version/$name.tar.gz"
echo "$sha256  $tmp/rcodesign.tar.gz" | sha256sum -c -
tar -xzf "$tmp/rcodesign.tar.gz" -C "$tmp"
sudo install -m 755 "$tmp/$name/rcodesign" /usr/local/bin/rcodesign
rm -rf "$tmp"
rcodesign --version
