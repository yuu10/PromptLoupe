# PromptLoupe

AI生成画像のメタデータを読み取り、それを元に並び替え・絞り込みしながら閲覧できる画像ビューア(C# / Avalonia / .NET 10。Windows・Mac・Linux)。

![PromptLoupe のメイン画面。左にファイル一覧、中央に画像、右にプロンプトのタグやキャラクター別プロンプトのカード](docs/images/main.png)

## ダウンロード

[Releases](https://github.com/yuu10/PromptLoupe/releases) から、OS に合ったものを入手してください。どれも .NET のインストールは不要で、ファイルは実行ファイル 1 つ(Mac は `PromptLoupe.app`)だけです。

| OS | ファイル | 動作の確認 |
|---|---|---|
| Windows 10・11(64 ビット) | `PromptLoupe-*-win-x64.zip` | 確認済み |
| Mac(Apple シリコン) | `PromptLoupe-*-osx-arm64.zip` | **未確認(試験版)** |
| Mac(Intel) | `PromptLoupe-*-osx-x64.zip` | **未確認(試験版)** |
| Linux(x64) | `PromptLoupe-*-linux-x64.tar.gz` | AI による自動の確認のみ(実機では未確認) |

- **Mac 版**は、Mac の実機で動作を確かめていない試験版です。Apple の公証を受けていないため、ダウンロードしたものはそのままでは開けません。ターミナルで `xattr -cr PromptLoupe.app` を実行してから開いてください。macOS 12 以降が対象です。
- **Linux 版**は、開発用のクラウド環境(Ubuntu)で、AI(Claude)が起動・画像の表示・HEIC と TIFF の表示・ファイル操作を自動で確かめていますが、実機のデスクトップでは確かめていません。
- Mac・Linux で動いた・動かなかったなど、[Issues](https://github.com/yuu10/PromptLoupe/issues) で教えていただけると助かります。
- 1.x は Windows 専用の WPF 版でした。2.0.0 から、Windows・Mac・Linux で動く版(Avalonia)に置き換えています。設定は 1.x と同じ場所に保存するので、Windows では 1.x の設定がそのまま引き継がれます。

### 初めて起動するとき(Windows の SmartScreen の警告)

PromptLoupe の exe にはコード署名をしていません。そのため、ダウンロードした exe を初めて開くと、「Windows によって PC が保護されました」という画面が出ることがあります。

起動するには、「詳細情報」をクリックしてから「実行」を押してください。2回目からは表示されません。

ウイルス対策ソフトが誤って警告することもあります。気になる場合は、下の手順でソースコードからビルドしてください。

### 設定の保存先

- Windows: `%AppData%\PromptLoupe`
- Mac・Linux: `~/.config/PromptLoupe`

## 機能

- 画像表示(PNG / JPEG / WebP / BMP / GIF / TIFF / HEIC)、ホイールでズーム、ドラッグでパン、ダブルクリックでフィット
  - JPEG・WebP・TIFF は回転情報(Orientation)に従って、正しい向きで表示する(サムネイルも同じ)。HEIC は、OS の機能が HEIF の回転情報で直して返すので、EXIF の値では回さない
  - PNG・JPEG・WebP・BMP・GIF は SkiaSharp、TIFF は LibTiff.Net で読む(どの OS でも。WebP も拡張機能なしで表示できる)
  - HEIC は OS の機能で読む: Windows は WIC(「HEIF 画像拡張機能」と「HEVC ビデオ拡張機能」が必要)、Mac は ImageIO、Linux はその PC の libheif と HEVC のプラグイン。HEVC のデコーダーは LGPL などで exe に入れられないため、同梱していない
  - 前後の画像を先読みしておき、送ったときにすぐ表示する
- 撮影情報(Exif): JPEG・WebP・HEIC の EXIF から、カメラ・レンズ・撮影日時・絞り・シャッター速度・ISO 感度・焦点距離・露出補正・撮影モード・フラッシュ・ホワイトバランスを右パネルに表示
  - GPS の位置情報は、初期設定では「記録あり」とだけ表示。メニュー「設定 → 撮影情報の位置情報(GPS)を表示する」で緯度・経度・標高を表示
- フォルダ内閲覧(←→ / PageUp・PageDown / Home・End)、ドラッグ&ドロップ、コマンドライン引数、「このアプリで開く」に対応
- メタデータ読み取り: Stable Diffusion WebUI(A1111/Forge)、ComfyUI、NovelAI、InvokeAI
  - PNG の tEXt / iTXt / zTXt、JPEG・WebP の EXIF UserComment
  - 上記以外のツールでも、`prompt` / `negative_prompt` / `seed` などの一般的な名前の項目を持つ JSON なら、汎用的に読み取る(「不明なツール(JSON: …)」と表示)
  - PixAI は非対応(ダウンロードした画像にプロンプトなどの情報が入っていないため)
- サブフォルダ読み込み: フォルダツリーの上の「サブフォルダ」か、メニュー「設定 → サブフォルダの読み込み」で深さを選択(含めない / 1〜3階層まで / 無制限。既定は含めない)。一覧ではファイル名の前のフォルダ名を別の色で表示。右クリックの「画像のあるフォルダへ移動」で、その画像のフォルダを開く
- フォルダのパス(絞り込み欄の上): フォルダ名のクリックで移動、空いたところのクリック(Ctrl+L / Alt+D)で手入力。戻る・進む・親フォルダへ(Alt+← / Alt+→ / Alt+↑、マウスの戻る・進むボタン)。メニュー「設定 → フォルダのパスを表示」で隠せる
- フォルダツリー(左パネルの上): よく使う場所とドライブから、フォルダを選んで開く。開いているフォルダは、ツリーでも開いて選んだ状態にする。最小化と高さの変更ができる(保存)。画像の枚数は数えない
- サムネイル: ファイル一覧に表示(メニュー「設定 → サムネイルを表示」でON/OFF)。画面に見えている分だけバックグラウンドで生成し、メモリ上にのみ保持(最大600枚、ディスクには保存しません)
- バージョン情報: メニュー「ヘルプ → バージョン情報」(バージョンは `PromptLoupe/PromptLoupe.csproj` の `<Version>` で管理)
- ライセンス: メニュー「ヘルプ → ライセンス」(ライセンス文は exe に入っている)
- 並び替え: ファイル名(自然順)・更新日時・撮影日時(EXIF。無ければ更新日時で代用)・サイズ・モデル・サンプラー・ステップ数・CFG
- 絞り込み: ファイル名・プロンプト・ネガティブプロンプト・キャラクター別プロンプト・キャラクター別ネガティブ・モデル名(カンマ区切りでAND。例: `blue hair, 1girl`。検索対象は「検索対象」ボタンで選択)
  - スペースと `_` は同じ扱い(`hatsune miku` = `hatsune_miku`)、`\` のエスケープは無視、大文字小文字は区別しない
- 使い方マニュアル: メニュー「ヘルプ → 使い方」または F1(内容は `PromptLoupe/Help.txt` を埋め込み)
- 表示方式の維持: 「フィット」「100%」のどちらを選んだかを覚え、画像を切り替えても同じ表示方式で開く(次回起動時も保持)
- 見た目: Avalonia の Fluent テーマ。ダーク/ライト/OS の設定に合わせる(メニュー「設定 → テーマ」)
- 絞り込み欄は画面上部でウィンドウの右端まで伸ばし、「検索対象」ボタンはその左に配置
- プロンプト・ネガティブ・キャラクター別・パラメーター・撮影情報の各カードは、見出しのクリックで最小化/展開(状態は保存)
- アプリのアイコンは、写真と、プロンプト(文字)を映す虫眼鏡を組み合わせたデザイン(`app.ico`。`tools/make_icon.ps1` で生成)
- 右パネルはカード表示。プロンプトはタグのチップで表示し、クリックで絞り込み欄に追加/解除(重みやかっこは外した形)
- サムネイルの大きさ(小48 / 中64 / 大96)を選択、画像の右下に表示倍率とフィット/100%ボタン
- プロンプトのコピー、生メタデータの表示
- ファイル操作(左の一覧の右クリックメニュー / キー): コピー・切り取り・貼り付け(エクスプローラーなどのファイルマネージャーと連携)、名前の変更、削除(ごみ箱へ)、ファイルマネージャーで表示、既定のアプリで開く、パス・画像・プロンプトのコピー、プロパティ(Windows のみ)。複数選択に対応
  - 切り取り(Ctrl+X)は、Windows のエクスプローラーと、Linux の GNOME・KDE のファイルマネージャーに「移動」として伝わる。Mac の Finder には伝わらず、Finder に貼り付けるとコピーになる
  - 削除は初期設定では無効。「設定 → ファイル操作 → 削除を許可する」でオンにする。「削除の前に確認する」も設定で切り替え(既定はオン)
  - 一覧の画像を、エクスプローラーや他のアプリへドラッグ&ドロップで持ち出せる(既定はコピー、Shift を押しながらで移動。Windows・Mac のみ。Linux は Avalonia 11.3 が対応していない)。外からのドロップは、そのファイル/フォルダを開く
  - 開いているフォルダの画像が、ほかのアプリで移動・削除・名前変更されたら、一覧にも反映する(切り取ってエクスプローラーに貼り付けたときなど)。新しく増えた画像は、開き直すと出る
- 全画面表示(F11 / メニュー「表示」/ 画像右下のボタン。Esc で戻る)
- 右のパネル(プロンプトなど)を隠す / 出す(Ctrl+I / メニュー「表示」/ 画像右下のボタン。状態は保存)
- 画像右下の表示倍率とボタンは、左端の ▸ で最小化できる(状態は保存)
- キャラクター別プロンプトにも「全文を表示」(プロンプトとネガティブを1つの欄に、見出しで区切って表示)
- タグの色分け: Danbooru のタグ辞書(`danbooru.csv`、利用者が用意)で、一般 / アーティスト / 作品名 / キャラクター名 / メタ を色分け。`<lora:…>` などは辞書なしで色分け。メニュー「設定 → タグの色分け」で辞書を読み込む(設定と同じフォルダに `danbooru.csv` として保存。アプリには同梱しない)。辞書は現時点では [a1111-sd-webui-tagcomplete の danbooru.csv](https://github.com/DominikDoom/a1111-sd-webui-tagcomplete/blob/main/tags/danbooru.csv) を推奨(同じ場所の e621.csv などは分類の番号が違うため不可)
- タグの集計(メニュー「表示 → タグの集計」/ Ctrl+T): タグごとに、使っている画像の枚数と割合を一覧にする
  - 集計するプロンプト(プロンプト / ネガティブ / キャラクター別)、範囲(一覧に表示中の画像 / フォルダのすべて)を選択。列の見出しのクリックで並び替え(もう一度で昇順/降順を切り替え)。条件は保存
  - ダブルクリックか Enter で絞り込みに追加/解除。範囲が「表示中」なら、絞り込みに合わせて自動で集計し直す
  - 選んだタグをカンマ区切りで、一覧をタブ区切りでコピーできる

## キー操作

| キー(Mac は Ctrl の代わりに ⌘) | 動作 |
|---|---|
| ← / PageUp / Backspace | 前の画像 |
| → / PageDown / Space | 次の画像 |
| Home / End | 最初 / 最後 |
| F | フィット表示 |
| 1 | 等倍(100%) |
| F11 / Esc | 全画面表示の切り替え / 全画面を終了 |
| Ctrl+C / Ctrl+X / Ctrl+V | 選んだファイルのコピー / 切り取り / 貼り付け |
| F2 | 名前の変更 |
| Delete(Mac は ⌘+Delete も) | ごみ箱へ移動(設定で許可しているとき) |
| Ctrl+Shift+C | ファイルのパスをコピー |
| Ctrl+T | タグの集計 |
| Ctrl+I | 右のパネルを隠す / 出す |
| Alt+Enter | プロパティ(Windows のみ) |
| Alt+← / Alt+→ / Alt+↑(Mac は ⌘[ / ⌘] / ⌘↑) | 前のフォルダに戻る / 進む / 親フォルダへ |
| Ctrl+L / Alt+D | フォルダのパスを入力 |
| F1 | 使い方 |

## ビルドと実行

.NET 10 SDK が必要です。Windows・Mac・Linux のどれでもビルドできます。

```
dotnet run --project PromptLoupe
```

テスト(共通部分 `PromptLoupe.Core` が対象):

```
dotnet test
```

配布と同じ形(自己完結型・単一ファイル)にする場合(`-r` は `win-x64` / `linux-x64` / `osx-arm64` / `osx-x64`):

```
dotnet publish PromptLoupe/PromptLoupe.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Mac 向けは、発行したものを `tools/make_macos_app.sh` で `PromptLoupe.app` にまとめます(Linux の上でも作れます)。

main と `claude/` のブランチへのプッシュ、PR のたびに、GitHub Actions でビルドとテストが通るかを確かめ、配布と同じ形のものを Artifacts に置いています(`.github/workflows/build.yml`。14 日で消えます)。

## リリースの手順

1. `PromptLoupe/PromptLoupe.csproj` の `<Version>` を上げて、main にマージする
2. 同じバージョンのタグを付けて push する(例: `git tag v2.0.0` → `git push origin v2.0.0`)
3. GitHub Actions(`.github/workflows/release.yml`)が、Windows・Mac・Linux 向けの配布物を作り、Releases に**下書き**で載せる。タグと `<Version>` が違うと止まる
4. Releases で下書きを開き、「変更点」を書いて「Publish release」を押す

## 構成

- `PromptLoupe.slnx` — ソリューション(ルートで `dotnet build` / `dotnet test` できる)

**PromptLoupe/**(アプリ本体。Avalonia)

- `MainWindow.axaml(.cs)` — 画面、閲覧、絞り込み、メタデータ表示、ズーム
- `MainWindow.FileOps.cs` — ファイル操作とドラッグ&ドロップ(持ち出し)
- `MainWindow.Watcher.cs` — 開いているフォルダの見張り(ほかのアプリでの移動・削除・名前変更を一覧に反映)
- `MainWindow.Fullscreen.cs` — 全画面表示
- `MainWindow.PathBar.cs` — フォルダのパスの欄(パンくず・手入力)と、戻る・進む・親フォルダへ
- `MainWindow.FolderTree.cs` — フォルダツリー
- `MainWindow.Tags.cs` — タグの色分け
- `MainWindow.TagStats.cs` / `TagStatsWindow.axaml(.cs)` — タグの集計
- `FileItem.cs` — 一覧の項目と並び替え
- `ThumbnailService.cs` — サムネイルの生成とメモリキャッシュ
- `ImageLoader.cs` — 画像の読み込み(SkiaSharp・LibTiff.Net)と、回転の補正
- `HeifLoader.cs` — HEIC の読み込み(Windows は WIC、Mac は ImageIO、Linux は libheif)
- `ImagePreloader.cs` — 前後の画像の先読み
- `PlatformShell.cs` — 既定のアプリで開く・ファイルマネージャーで表示・ごみ箱へ移す・プロパティ(OS ごとの処理)
- `FileClipboard.cs` — ファイルと画像のクリップボード(切り取りの伝え方は OS ごとに違う)
- `RenameWindow.axaml(.cs)` — 名前の変更ダイアログ
- `HelpWindow.axaml(.cs)` / `Help.txt` — 使い方マニュアル(本文は `Help.txt`)
- `LicenseWindow.axaml(.cs)` / `THIRD-PARTY-NOTICES.txt` — ライセンスの表示
- `MessageDialog.cs` — メッセージのダイアログ
- `macos/` — Mac 版の Info.plist とアイコン

**PromptLoupe.Core/**(OS に依存しない共通部分)

- `Metadata/MetadataReader.cs` — 各形式のメタデータ抽出と、生成ツールごとの解釈
- `Metadata/ImageMetadata.cs` — 解釈済みメタデータのモデル
- `Metadata/ExifInfo.cs` — EXIF の撮影情報と、表示用の整形
- `PromptText.cs` — 検索用の正規化、プロンプトのタグへの分割
- `NaturalComparer.cs` — 自然順(img2 が img10 より前)の比較
- `FileChanges.cs` — ほかのアプリでの移動・削除・名前変更の通知を、一覧の変更に読み替える
- `FolderPath.cs` — パスの分割(パンくず)・比較・手入力の解釈
- `AppSettings.cs` — 設定の保存
- `TagDictionary.cs` — タグ辞書の読み込み

**PromptLoupe.Tests/** — 共通部分のテスト

**tools/** — `make_macos_app.sh`(Mac 版の .app を作る)、`install_rcodesign.sh`(CI 用。Mac の署名ツールを入れる)、`make_icon.ps1`(アイコンを作る。Windows の PowerShell で動く)

## ライセンス

PromptLoupe は [MIT License](LICENSE) で配布しています(Copyright (c) 2026 yuto)。

### 使用ライブラリ

Avalonia(MIT)・SkiaSharp・HarfBuzzSharp(MIT)・MicroCom.Runtime(MIT)・Tmds.DBus.Protocol(MIT)・ANGLE(BSD 3-Clause)・LibTiff.Net(BSD 3-Clause)と、SkiaSharp に含まれる Skia などのライブラリを使っています。ライセンス文は [PromptLoupe/THIRD-PARTY-NOTICES.txt](PromptLoupe/THIRD-PARTY-NOTICES.txt) にあり、`LICENSE` とともに exe に埋め込んで、アプリの「ヘルプ → ライセンス」で表示します。

## 既知の制限

- HEIC の表示には、OS の機能が必要です(Windows は「HEIF 画像拡張機能」と「HEVC ビデオ拡張機能」、Linux は libheif と HEVC のプラグイン)。Linux では、libheif の版によって、特殊な作りの HEIC を読めないことがあります
- TIFF の撮影情報(Exif)の読み取りは未対応。TIFF は先頭のページだけ表示します
- GIF アニメーションは先頭フレームのみ表示
- Linux では、一覧からほかのアプリへのドラッグでの持ち出しはできません
- サムネイルのグリッド表示は未対応
