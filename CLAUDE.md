# PromptLoupe(旧名 M Viewer。Claude Code 用メモ)

AI生成画像のメタデータ(プロンプト・パラメーター)を元に、並び替え・絞り込みして閲覧する Windows 用画像ビューア。
C# / WPF / .NET 8、ModernWpfUI 0.9.6。バージョンは `PromptLoupe.csproj` の `<Version>`(現在 1.5.0)。1.5.0 で M Viewer から改名した(商標「MVIEW」(登録6320862、第9類・第42類)と紛らわしいため)。

## 進め方

- 回答とコメントは日本語。丁寧で簡潔に。UI の文言も日本語。
- ビルドは `dotnet build`、実行は `dotnet run`。変更したら**必ずビルドして、エラーがないことを確かめる**。
- 画面の見た目やドラッグ操作などは、実行しても確認しきれない。ユーザーに確認してもらう項目として、作業の最後に挙げる。
- 1.4.3(1.4.2 で JPEG の EXIF 回転、1.4.3 で HEIC の表示)まで、ユーザーが「動作は問題なし」と確認済み(2026-11-01)。ただし、左右反転の Orientation(2・4・5・7)は実物の画像がなく未確認。
- ユーザーが「動いた」と言うまで、「動く」と断言しない。
- 機能を足したら、`HelpWindow.xaml`(使い方)と `README.md` も更新する。バージョンを上げるときは `PromptLoupe.csproj` の `<Version>` を変える。

## ファイル構成

| ファイル | 役割 |
|---|---|
| `Metadata/MetadataReader.cs` | PNG(tEXt / iTXt / zTXt)、JPEG・WebP(EXIF UserComment)の読み取りと、生成ツールごとの解釈 |
| `Metadata/ImageMetadata.cs` | 解釈済みメタデータ |
| `FileItem.cs` | 一覧の項目、検索用の正規化済み文字列、並び替え(自然順は `StrCmpLogicalW`) |
| `ThumbnailService.cs` | サムネイルのメモリキャッシュ(LRU 600 枚、ディスクには保存しない) |
| `ExifOrientation.cs` | JPEG の EXIF 回転(Orientation)の読み取りと補正。表示(`MainWindow.Decode`)とサムネイルの両方で使う |
| `AppSettings.cs` | 設定(`%AppData%\PromptLoupe\settings.json`)。`DataDir` が初回に旧名の `%AppData%\MViewer` の中身をコピーして引き継ぐ(旧フォルダは消さない) |
| `MainWindow.xaml` / `.cs` | 画面、閲覧、絞り込み、メタデータ表示、ズーム |
| `MainWindow.FileOps.cs` | 左パネルのファイル操作と、一覧からのドラッグ持ち出し |
| `MainWindow.Fullscreen.cs` | 全画面表示(F11 / Esc) |
| `MainWindow.Tags.cs` / `TagDictionary.cs` | タグの色分けと、Danbooru 辞書(danbooru.csv)の読み込み |
| `RenameWindow.xaml(.cs)` | 名前の変更ダイアログ |
| `TagStatsWindow.xaml(.cs)` / `MainWindow.TagStats.cs` | タグの集計(使っている画像の枚数)。一覧や絞り込みが変わると `NotifyTagStats` で数え直す(300ms 間引き) |
| `HelpWindow.xaml(.cs)` | 使い方マニュアル(`FlowDocument`) |

## 守ること・落とし穴

- `ImageMetadata` は WPF の `System.Windows.Media.ImageMetadata` と名前が衝突する。`MainWindow.xaml.cs` では `using ImageMetadata = PromptLoupe.Metadata.ImageMetadata;` で解決している。`System.Windows.Media` を使う partial ファイルでは、`ImageMetadata` を直接書かない。
- `ListCollectionView.DeferRefresh()` の最中は、元のコレクションを変更できない。フォルダを開くたびに新しい `ObservableCollection` と `ListCollectionView` を作る(`SetItems`)。
- 自作の `TextBox` スタイル(`PlainText`)は、文字色をテーマのブラシで指定しないと、ダークでも黒になる。
- **一覧は複数選択**(`SelectionMode="Extended"`)。表示中の1枚は `currentItem` で管理し、`SelectedItem` は使わない。項目をまとめて取り除くときは `suppressSelection` と `RemoveItems` を使う。削除や名前変更のあとは、`FocusSelectedListItem` でフォーカスを新しい項目に移す(↑↓キーが先頭から始まるのを防ぐ)。
- **削除は既定で無効**(`AllowDelete`)。ごみ箱に移すだけで、完全削除はしない。確認ダイアログは `ConfirmDelete` で切り替え。この安全装置は外さない。
- 外からのドロップは「そのファイル/フォルダを開く」(コピーはしない)。一覧から持ち出すドラッグ中は `draggingOut` で、自分のウィンドウへのドロップを無視する。
- 全画面は `WindowStyle=None` + `Maximized`。ModernWpfUI のタイトルバーが完全に消えるかは未確認。
- タグ辞書は、アプリに**同梱しない**(ライセンス未確認)。利用者が読み込む。
- WebP の表示は、Windows の「WebP 画像拡張機能」に依存する(メタデータの読み取りには不要)。HEIC(1.4.3〜)は「HEIF 画像拡張機能」と「HEVC ビデオ拡張機能」に依存する。案内文は `CodecHint`。
- 編集の前には、ファイルを読み直す。

## 既知の制限・未対応

- PixAI は対応しない。ダウンロードした PNG・WebP にメタデータが一切入っていない(PNG は IHDR・IDAT・IEND のみ。2026-11-01 に実物で確認、ユーザーも Web で同じ情報を確認)。GIF は先頭フレームのみ。EXIF 回転は JPEG のみ対応(1.4.2、WebP・TIFF・HEIC は未対応。HEIC は自前で補正していないが、縦向きの写真が縦で出ることをユーザーが確認済み(2026-11-01))。
- 見送り: 虫眼鏡(部分拡大)、サムネイルのグリッド表示(仮想化パネルが必要)。

## 次にやること(候補)

1. Mac 版(Avalonia UI)。メタデータ読み取りと設定は共通化できる。
2. 配布の準備。コード署名は**しない方針**(2026-11-01 決定。費用に見合わず、OV・EV 証明書でも SmartScreen の警告はすぐには消えないため)。README に SmartScreen の回避方法を書いた。警告が問題になってきたら、Microsoft Store(MSIX、Store 側で署名)か SignPath Foundation(OSS 向けの無料署名)を検討する。名前は PromptLoupe に決定(Web・GitHub・J-PlatPat で同名なし、2026-11-01 確認。法的な確認ではない)。`LICENSE`(MIT、著作権者 yuto)と `THIRD-PARTY-NOTICES.txt`(ModernWpfUI)は追加済みで、バージョン情報と使い方にも表示している。アイコン(`app.ico`)は Claude で生成したもの。
