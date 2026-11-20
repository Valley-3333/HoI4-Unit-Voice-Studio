# NSISの準備

インストーラの作成には **NSIS 3.13** を使います。

1. [公式ZIP](https://sourceforge.net/projects/nsis/files/NSIS%203/3.13/nsis-3.13.zip/download)を取得し、以下のSHA-256値と照合します。
2. `tools/nsis-3.13/makensis.exe` ができるように展開します。

```text
ba63dffc4410ee89193e1cb5a41989991bd77c61068da17e3156d136b7b0b3d8
```

ハッシュ値の出典：[公式リリース情報](https://nsis-dev.github.io/release-data/versions.json)。
ZIPではなくHTMLが取得された場合は別のミラーを使い、同じハッシュ値で確認してください。

ビルド・検証・公開の手順は [開発ガイド](../docs/DEVELOPMENT.md) を参照してください。
