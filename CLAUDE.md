# PromptLoupe(旧名 M Viewer。Claude Code 用メモ)

AI生成画像のメタデータ(プロンプト・パラメーター)を元に、並び替え・絞り込みして閲覧する Windows 用画像ビューア。
C# / WPF / .NET 8、ModernWpfUI 0.9.6。バージョンは `PromptLoupe.csproj` の `<Version>`(現在 1.6.0。1.6.0 で撮影情報(Exif)の表示・撮影日時の並び替え・WebP の EXIF 回転を追加)。1.5.0 で M Viewer から改名した(商標「MVIEW」(登録6320862、第9類・第42類)と紛らわしいため)。

## 進め方

- 回答とコメントは日本語。丁寧で簡潔に。UI の文言も日本語。
- ビルドは `dotnet build`、実行は `dotnet run`。変更したら**必ずビルドして、エラーがないことを確かめる**。
- テストは `dotnet test PromptLoupe.Tests`(共通部分 `PromptLoupe.Core` だけが対象。画面のテストはない)。テスト用の画像は、テストの中でバイト列から組み立てる(`TestImages.cs` / `ExifBuilder`)。メタデータの読み取りを直したら、テストも足す。
- CI は GitHub Actions(`.github/workflows/build.yml`)。main と `claude/**` へのプッシュと PR で、Windows で `dotnet build -c Release`、Linux で `dotnet test`、Avalonia 版を Windows・Mac・Linux でビルドする(画面の動作までは確かめない)。
- クラウド(Linux)のセッションでは、WPF のプロジェクトはビルドできない(Ubuntu の .NET SDK には WindowsDesktop の SDK が入っていない。Microsoft 版の SDK の配布元はネットワークの設定で使えない)。`PromptLoupe.Core` と `PromptLoupe.Tests` はビルド・テストできる。WPF 側を変えたら、push して CI の Windows ビルドで確かめる。SDK は `apt-get install -y dotnet-sdk-8.0` で入れる。
- 共通部分を `PromptLoupe.Core/` に切り出した(2026-11-04)。クロスプラットフォーム版(Avalonia)の準備。WPF 版の動きは変えていない。切り出し後の WPF 版で、起動・PNG と JPEG のメタデータ表示・タグの絞り込みと色分けと集計・設定の引き継ぎ・撮影情報・EXIF 回転を、ユーザーが「問題なし」と確認済み(2026-11-04)。ユーザーは JPEG の実物を持っていないので、確認用の JPEG は Pillow で作って渡した(A1111 の UserComment、カメラの Exif と GPS、Orientation 6。生成スクリプトはリポジトリに入れていない)。
- Avalonia 版(`PromptLoupe.Avalonia/`)を作り始めた(2026-11-04)。方針(ユーザーと決めたこと): いずれ Avalonia 版**一本にする**。WPF 版と並行して育てるのではなく、Avalonia 版が WPF 版と同じことをできるようになるまでの保険として WPF 版を残し、同じになったら WPF 版はやめる。Mac で使う予定がなければ、Mac での動作確認は放置してよい。ただし**確認していない OS 向けには配布しない**(Releases に出さない、README に「対応」と書かない)。Linux はクラウドで確かめる(Avalonia.Headless でスクリーンショット)。Windows での確認はユーザーに頼む。Avalonia は 11.3 系を使う(12 系も出ているが、書き方を確実に知っている 11 系にした)。
- Avalonia 版はクラウド(Linux)でもビルドできる(`dotnet build PromptLoupe.Avalonia`)。画面の確認は、スクラッチに `Avalonia.Headless` と `Avalonia.Skia` を参照する小さなプログラムを作り、`MainWindow` を表示して `CaptureRenderedFrame()` で PNG に保存する(`UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })` + `UseSkia()`)。日本語のフォントはコンテナに入っている(IPAGothic)。このプログラムはリポジトリに入れていない。
- リリースは `.github/workflows/release.yml`。`v*` のタグを push すると、自己完結型・単一 exe(圧縮あり、約 73MB)を zip にして Releases に**下書き**で作る。タグと `<Version>` が違うと失敗する。公開は人が GitHub の画面で行う。
- 画面の見た目やドラッグ操作などは、実行しても確認しきれない。ユーザーに確認してもらう項目として、作業の最後に挙げる。
- 1.4.3(1.4.2 で JPEG の EXIF 回転、1.4.3 で HEIC の表示)まで、ユーザーが「動作は問題なし」と確認済み(2026-11-01)。
- 1.6.0(撮影情報(Exif)カード、GPS の表示設定、撮影日時の並び替え、HEIC の Exif 読み取り)も、ユーザーが「問題なし」と確認済み(2026-11-02)。
- EXIF 回転は、自作のテスト画像(Orientation 1〜8 の JPEG・WebP、計 16 枚)で、表示・サムネイルとも正しい向きになることを確認済み(2026-11-03、ユーザーもアプリの画面で確認)。左右反転(2・4・5・7)と WebP を含む。カメラで撮った実物の WebP では試していないが、手に入る見込みがないため、テスト画像での確認で十分とする(2026-11-04)。生成ツールはリポジトリに入れていない(回転の処理を触るときに作り直す)。
- ユーザーが「動いた」と言うまで、「動く」と断言しない。
- 機能を足したら、`HelpWindow.xaml`(使い方)と `README.md` も更新する。バージョンを上げるときは `PromptLoupe.csproj` の `<Version>` を変える。

## ファイル構成

| ファイル | 役割 |
|---|---|
| `PromptLoupe.Core/` | OS に依存しない共通部分(下の `Metadata/`・`PromptText.cs`・`AppSettings.cs`・`TagDictionary.cs`)。WPF を参照しない。名前空間は移す前と同じ(`PromptLoupe` / `PromptLoupe.Metadata`) |
| `PromptLoupe.Tests/` | 共通部分のテスト(xUnit) |
| `Metadata/MetadataReader.cs`(Core) | PNG(tEXt / iTXt / zTXt)、JPEG・WebP・HEIC(EXIF)の読み取りと、生成ツールごとの解釈。HEIC は ISOBMFF の meta → iinf / iloc から Exif アイテムを探す |
| `Metadata/ImageMetadata.cs`(Core) | 解釈済みメタデータ |
| `Metadata/ExifInfo.cs`(Core) | EXIF の撮影情報(カメラ・露出・撮影日時・GPS)と、右パネル用の整形。GPS は `ShowGps`(既定オフ)のときだけ座標を出す |
| `PromptText.cs`(Core) | 検索用の正規化(`Normalize`)、プロンプトのタグへの分割(`SplitTags`)、重みの除去(`CleanTag`)、集計用のタグ(`TagKeys`) |
| `FileItem.cs` | 一覧の項目、検索用の正規化済み文字列、並び替え(自然順は `StrCmpLogicalW`。Windows 専用なので、Core に移すときは自前の実装が要る) |
| `ThumbnailService.cs` | サムネイルのメモリキャッシュ(LRU 600 枚、ディスクには保存しない) |
| `ExifOrientation.cs` | JPEG・WebP の EXIF 回転(Orientation)の読み取りと補正。表示(`MainWindow.Decode`)とサムネイルの両方で使う。WebP は WIC ではなく `MetadataReader.ReadExif` で読む。HEIC は WIC が irot で直すので**回さない**(二重に回る) |
| `AppSettings.cs`(Core) | 設定(`%AppData%\PromptLoupe\settings.json`)。保存先は `DataDir`。旧名 M Viewer の保存先(`%AppData%\MViewer`)からの引き継ぎは、利用者がいないため削除した |
| `MainWindow.xaml` / `.cs` | 画面、閲覧、絞り込み、メタデータ表示、ズーム |
| `MainWindow.FileOps.cs` | 左パネルのファイル操作と、一覧からのドラッグ持ち出し |
| `MainWindow.Fullscreen.cs` | 全画面表示(F11 / Esc) |
| `MainWindow.Tags.cs` / `TagDictionary.cs`(Core) | タグの色分けと、Danbooru 辞書(danbooru.csv)の読み込み |
| `RenameWindow.xaml(.cs)` | 名前の変更ダイアログ |
| `TagStatsWindow.xaml(.cs)` / `MainWindow.TagStats.cs` | タグの集計(使っている画像の枚数)。一覧や絞り込みが変わると `NotifyTagStats` で数え直す(300ms 間引き) |
| `HelpWindow.xaml(.cs)` | 使い方マニュアル(`FlowDocument`) |
| `PromptLoupe.Core/NaturalComparer.cs` | 自然順の比較(`StrCmpLogicalW` の代わり。Avalonia 版で使う。WPF 版は `StrCmpLogicalW` のまま) |
| `PromptLoupe.Avalonia/` | Avalonia 版(開発中)。WPF 版と同じ名前のファイルは同じ役割で、中身は WPF 版から移したもの |
| `PromptLoupe.Avalonia/ImageLoader.cs` | 画像の読み込み(SkiaSharp。WIC の代わり)と、JPEG・WebP の EXIF 回転。読めるのは PNG・JPEG・WebP・BMP・GIF。HEIC・TIFF は表示できない |
| `PromptLoupe.Avalonia/PlatformShell.cs` | 既定のアプリで開く・ファイルマネージャーで表示・ごみ箱(Windows は VisualBasic、Mac は osascript で Finder、Linux は `gio trash`、なければ freedesktop の決まりで自前) |
| `PromptLoupe.Avalonia/MessageDialog.cs` | MessageBox の代わり(Avalonia には標準のものがない) |

## 守ること・落とし穴

- `ImageMetadata` は WPF の `System.Windows.Media.ImageMetadata` と名前が衝突する。`MainWindow.xaml.cs` では `using ImageMetadata = PromptLoupe.Metadata.ImageMetadata;` で解決している。`System.Windows.Media` を使う partial ファイルでは、`ImageMetadata` を直接書かない。
- ルートの `PromptLoupe.csproj` は、`DefaultItemExcludes` で `PromptLoupe.Core/**`・`PromptLoupe.Tests/**`・`PromptLoupe.Avalonia/**` を除いている(除かないと、Core のファイルが二重にコンパイルされる)。ルートに `.sln` を置くと、`dotnet build` がプロジェクトを選べずに失敗するので置かない。
- `ListCollectionView.DeferRefresh()` の最中は、元のコレクションを変更できない。フォルダを開くたびに新しい `ObservableCollection` と `ListCollectionView` を作る(`SetItems`)。
- 自作の `TextBox` スタイル(`PlainText`)は、文字色をテーマのブラシで指定しないと、ダークでも黒になる。
- **一覧は複数選択**(`SelectionMode="Extended"`)。表示中の1枚は `currentItem` で管理し、`SelectedItem` は使わない。項目をまとめて取り除くときは `suppressSelection` と `RemoveItems` を使う。削除や名前変更のあとは、`FocusSelectedListItem` でフォーカスを新しい項目に移す(↑↓キーが先頭から始まるのを防ぐ)。
- **削除は既定で無効**(`AllowDelete`)。ごみ箱に移すだけで、完全削除はしない。確認ダイアログは `ConfirmDelete` で切り替え。この安全装置は外さない。
- 外からのドロップは「そのファイル/フォルダを開く」(コピーはしない)。一覧から持ち出すドラッグ中は `draggingOut` で、自分のウィンドウへのドロップを無視する。
- 全画面は `WindowStyle=None` + `Maximized`。ModernWpfUI のタイトルバー(左上のアプリ名、右上の閉じるボタン)は**残る**(2026-11-04、ユーザーが確認)。最大化から入って戻すと、いったん元のサイズを挟む。試して見送った案(2026-11-04): 全画面の間だけ `WindowHelper.SetUseModernWindowStyle(false)` にすると、タイトルバーは消えるが、切り替えがカクカクする。さらに切り替え中に DWM の cloak(`DWMWA_CLOAK`)で隠すと、滑らかにはなるが一瞬消え、全画面でもタスクバーが出るようになった。どちらも元に戻した。
- タグ辞書は、アプリに**同梱しない**(ライセンス未確認)。利用者が読み込む。
- WebP の表示は、Windows の「WebP 画像拡張機能」に依存する(メタデータの読み取りには不要)。HEIC(1.4.3〜)は「HEIF 画像拡張機能」と「HEVC ビデオ拡張機能」に依存する。案内文は `CodecHint`。
- 編集の前には、ファイルを読み直す。
- Avalonia 版の落とし穴: `ColumnDefinition` や `ScaleTransform` に `x:Name` を付けても、コードから使うフィールドはできない(コントロールではないため)。列は `BodyGrid.ColumnDefinitions[i]`、変換はコードで作って付ける。`ListBox` の `ItemsSource` を差し替えても前の選択が残ることがあるので、`Selection.Clear()` してから選び直す(`RefreshView`)。WPF の `ListCollectionView` はないので、絞り込みと並び替えは `RefreshView` で一覧を作り直す。`Image` の `RenderTransformOrigin` の既定は中央なので `0,0` にする。`DragEventArgs.Data` は古い書き方で、`DataTransfer` を使う。チェック付きのメニュー(`ToggleType`)は、クリックの処理で設定から `IsChecked` を付け直す(自動で切り替わるかどうかに頼らない)。

## 既知の制限・未対応

- PixAI は対応しない。ダウンロードした PNG・WebP にメタデータが一切入っていない(PNG は IHDR・IDAT・IEND のみ。2026-11-01 に実物で確認、ユーザーも Web で同じ情報を確認)。ステルス PNGInfo(画素の最下位ビット)にも何も入っていない(RGBA だがアルファはすべて 255。2026-11-03 に実物 4 枚で確認)。NovelAI のステルス PNGInfo(`stealth_pngcomp`)への対応は見送り。NovelAI の画像は tEXt にも同じ情報があり、今のままで読めるため。GIF は先頭フレームのみ。EXIF 回転は JPEG(1.4.2)と WebP(1.6.0、テスト画像で確認済み)。TIFF は未対応。HEIC は自前で補正していないが、縦向きの写真が縦で出ることをユーザーが確認済み(2026-11-01)。TIFF の撮影情報(Exif)も未対応。
- 見送り: 虫眼鏡(部分拡大)、サムネイルのグリッド表示(仮想化パネルが必要)。

## 次にやること(候補)

1. Avalonia 版を WPF 版と同じにする(2026-11-04 に第1段階: 一覧・サムネイル・絞り込み・並び替え・画像表示とズーム・メタデータのカード・タグの色分けと辞書・タグの集計・名前の変更・ごみ箱への削除・全画面・テーマ・設定)。Linux ではスクリーンショットで確認済み。**Windows での確認はまだ**(ユーザーに頼む)。残り: 一覧からのドラッグ持ち出し、ファイルのコピー・切り取り・貼り付け(クリップボードのファイル)、画像のコピー、プロパティ(Windows のみ)、使い方の画面(`FlowDocument` の代わり)、HEIC・TIFF の表示(別のライブラリが要る。ライセンスと配布サイズを調べる)、Avalonia 版のライセンス表記(Avalonia・SkiaSharp・HarfBuzzSharp・Tmds.DBus.Protocol・MicroCom。SkiaSharp の THIRD-PARTY-NOTICES は約 2,700 行)、配布(release.yml)。Mac はユーザーの MacBook Pro 2016(Intel、macOS 12 まで)で確認できるが、Mac で使う予定がなければ放置してよい。Mac の配布は署名・公証がないと Gatekeeper に強く止められる。
2. 配布の準備。コード署名は**しない方針**(2026-11-01 決定。費用に見合わず、OV・EV 証明書でも SmartScreen の警告はすぐには消えないため)。README に SmartScreen の回避方法を書いた。警告が問題になってきたら、Microsoft Store(MSIX、Store 側で署名)か SignPath Foundation(OSS 向けの無料署名)を検討する。名前は PromptLoupe に決定(Web・GitHub・J-PlatPat で同名なし、2026-11-01 確認。法的な確認ではない)。`LICENSE`(MIT、著作権者 yuto)と `THIRD-PARTY-NOTICES.txt`(ModernWpfUI)は追加済みで、バージョン情報と使い方にも表示している。アイコン(`app.ico`)は、写真と、文字を映す虫眼鏡の絵(1.6.0 の公開前にタグの絵から変更)。`tools/make_icon.ps1`(WPF で描画し、16〜256px の PNG を詰めた .ico を作る)で生成する。
