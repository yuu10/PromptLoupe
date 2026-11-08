# PromptLoupe(旧名 M Viewer。Claude Code 用メモ)

AI生成画像のメタデータ(プロンプト・パラメーター)を元に、並び替え・絞り込みして閲覧する画像ビューア。Windows・Mac・Linux。
C# / Avalonia 11.3 / .NET 10。バージョンは `PromptLoupe/PromptLoupe.csproj` の `<Version>`(現在 2.0.0。2026-11-08 に公開。2.0.0 で WPF 版(Windows 専用)から Avalonia 版に置き換え、Mac・Linux 向けも配布する)。1.x は WPF 版(ModernWpfUI。最後は 1.7.0。コードは v1.7.0 のタグに残っている)。1.5.0 で M Viewer から改名した(商標「MVIEW」(登録6320862、第9類・第42類)と紛らわしいため)。

## 進め方

- 回答とコメントは日本語。丁寧で簡潔に。UI の文言も日本語。
- ビルドはルートで `dotnet build`(`PromptLoupe.slnx`)、実行は `dotnet run --project PromptLoupe`。変更したら**必ずビルドして、エラーがないことを確かめる**。クラウド(Linux)のセッションでもビルドできる。SDK は `curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh && bash dotnet-install.sh --channel 10.0 --install-dir /root/.dotnet` で入れる(`/usr/local/bin/dotnet` が `/root/.dotnet` を指している)。
- テストは `dotnet test`(共通部分 `PromptLoupe.Core` だけが対象。画面のテストはない)。テスト用の画像は、テストの中でバイト列から組み立てる(`TestImages.cs` / `ExifBuilder`)。メタデータの読み取りを直したら、テストも足す。
- CI は GitHub Actions(`.github/workflows/build.yml`)。main と `claude/**` へのプッシュと PR で、Linux でテスト、Windows・Linux でビルドと発行、Linux の上で Mac 向け(osx-arm64・osx-x64)の .app 作成。配布と同じ形のものを Artifacts に置く(14 日)。画面の動作までは確かめない。Mac の実行環境(macos-latest)は、割り当てられずに 15 分で取り消されることがあった(2026-11-04)ので使わない。
- 方針(ユーザーと決めたこと): 2.0.0 で WPF 版をやめて Avalonia 版一本にした(2026-11-08。機能をそろえ、Windows での普段使いでも問題なしとユーザーが確認してから)。Windows の確認はユーザーに頼む。Linux はクラウドで確かめる(Avalonia.Headless や Xvfb)。Mac はユーザーの MacBook Pro 2016(Intel、macOS 12 まで)で確認できるが、Mac で使う予定がなければ放置してよい。**確認していない OS 向けには、確認していないことを明記して配布する**: Mac は「試験版・動作未確認」(2026-11-06、フィードバックを期待してユーザーが希望)、Linux は「AI による自動の確認のみ、実機では未確認」(2026-11-08、ユーザーの希望)。README とリリースノートに書いてある。Avalonia は 11.3 系を使う(12 系も出ているが、書き方を確実に知っている 11 系にした)。
- 画面の確認(クラウド)は、スクラッチに `Avalonia.Headless` と `Avalonia.Skia` を参照する小さなプログラムを作り、`MainWindow` を表示して `CaptureRenderedFrame()` で PNG に保存する(`UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })` + `UseSkia()`)。発行した単一ファイル版は、Xvfb の上で起動して `import -window root` で撮れる。日本語のフォントはコンテナに入っている(IPAGothic)。HEIC を試すには `apt-get install libheif-plugin-libde265` が要る。これらのプログラムはリポジトリに入れていない。
- リリースは `.github/workflows/release.yml`。`v*` のタグを push すると、Windows(win-x64 の zip。Windows の上で作る)・Mac(osx-arm64・osx-x64 の .app の zip)・Linux(linux-x64 の tar.gz。実行の権限を残すため)を、自己完結型・単一ファイル(約 47MB。ライセンス文も exe に入っていて、配布物は exe だけ)で作り、Releases に**下書き**で載せる。タグと `<Version>` が違うと失敗する。公開は人が GitHub の画面で行う。リリースノートの雛形(OS ごとの確認状況・起動の仕方)は release.yml の中。
- 画面の見た目やドラッグ操作などは、実行しても確認しきれない。ユーザーに確認してもらう項目として、作業の最後に挙げる。
- ユーザーが「動いた」と言うまで、「動く」と断言しない。
- 機能を足したら、`PromptLoupe/Help.txt`(使い方)と `README.md` も更新する。バージョンを上げるときは `PromptLoupe/PromptLoupe.csproj` の `<Version>` を変える。
- 確認済みのこと(1.x の WPF 版から引き継いだもの): EXIF 回転は、自作のテスト画像(Orientation 1〜8 の JPEG・WebP、計 16 枚)で、表示・サムネイルとも正しい向きになることを確認済み(2026-11-03)。生成ツールはリポジトリに入れていない(回転の処理を触るときに作り直す)。ユーザーは JPEG の実物を持っていないので、確認用の JPEG は Pillow で作って渡す(A1111 の UserComment、カメラの Exif と GPS、Orientation 6)。TIFF(回転 1〜8・CMYK・16 ビットなど)と HEIC の確認用画像も同じく作って渡した(2026-11-07)。

## ファイル構成

| ファイル | 役割 |
|---|---|
| `PromptLoupe.Core/` | OS に依存しない共通部分(下の `Metadata/`・`PromptText.cs`・`AppSettings.cs`・`TagDictionary.cs`)。名前空間は移す前と同じ(`PromptLoupe` / `PromptLoupe.Metadata`) |
| `PromptLoupe.Tests/` | 共通部分のテスト(xUnit) |
| `Metadata/MetadataReader.cs`(Core) | PNG(tEXt / iTXt / zTXt)、JPEG・WebP・HEIC(EXIF)の読み取りと、生成ツールごとの解釈。HEIC は ISOBMFF の meta → iinf / iloc から Exif アイテムを探す |
| `Metadata/ImageMetadata.cs`(Core) | 解釈済みメタデータ |
| `Metadata/ExifInfo.cs`(Core) | EXIF の撮影情報(カメラ・露出・撮影日時・GPS)と、右パネル用の整形。GPS は `ShowGps`(既定オフ)のときだけ座標を出す |
| `PromptText.cs`(Core) | 検索用の正規化(`Normalize`)、プロンプトのタグへの分割(`SplitTags`)、重みの除去(`CleanTag`)、集計用のタグ(`TagKeys`) |
| `AppSettings.cs`(Core) | 設定(Windows は `%AppData%\PromptLoupe\settings.json`、Mac・Linux は `~/.config/PromptLoupe/settings.json`。1.x の WPF 版と同じ場所なので、Windows では 1.x の設定を引き継ぐ)。保存先は `DataDir`。旧名 M Viewer の保存先(`%AppData%\MViewer`)からの引き継ぎは、利用者がいないため削除した |
| `PromptLoupe.Core/NaturalComparer.cs` | 自然順の比較(WPF 版で使っていた Windows の `StrCmpLogicalW` の代わり) |
| `PromptLoupe/` | アプリ本体(Avalonia)。2.0.0 で WPF 版(1.x、ルートにあった)を消して置き換えた(2026-11-08)。フォルダ名も `PromptLoupe.Avalonia/` から変えた。WPF 版のコードは 1.7.0 のタグに残っている |
| `PromptLoupe.slnx` | ソリューション。ルートで `dotnet build` / `dotnet test` できる(WPF 版の csproj がルートにあった間は、プロジェクトを選べなくなるので置けなかった) |
| `PromptLoupe/MainWindow.axaml` / `.cs` | 画面、閲覧、絞り込み、メタデータ表示、ズーム。絞り込みと並び替えは `RefreshView` で一覧を作り直す |
| `PromptLoupe/MainWindow.FileOps.cs` | 左パネルのファイル操作と、一覧からのドラッグ持ち出し |
| `PromptLoupe/MainWindow.Fullscreen.cs` | 全画面表示(F11 / Esc。`WindowState.FullScreen`) |
| `PromptLoupe/MainWindow.PathBar.cs` / `FolderPath.cs`(Core) | フォルダのパスの欄(絞り込み欄の上。2026-11-08)。パンくずのボタン(クリックで移動)と、空いたところのクリック(Ctrl+L / Alt+D、Mac は ⌘L)で入力欄に切り替え(Enter で移動、Esc・フォーカスが外れたら取り消し。見つからなければ `invalid` のクラスで枠を赤くし、`InnerRightContent` に理由。`DataValidationErrors` は下に行が増えて表示がずれるので使わない)。戻る・進むの履歴(50 件。`navigatingHistory` のあいだは積まない)、親フォルダへ(Alt+←→↑、マウスの戻る・進むボタン、Mac は ⌘[ ⌘] ⌘↑)。`OpenFolder` の最初で `OnFolderChanging` を呼び、履歴・パスの欄・ツリーを合わせる。設定 `ShowPathBar` で隠せる(全画面でも隠す)。長いパスは右端(いまのフォルダ)を見せる(`ScrollViewer.ScrollToEnd` は縦なので、`Offset` を自分で計算する) |
| `PromptLoupe/MainWindow.FolderTree.cs` | フォルダツリー(左パネルの上。2026-11-08)。根はよく使う場所(ピクチャ・デスクトップ・ドキュメント・ダウンロード、Mac・Linux はホームも)とドライブ(Windows のドライブ名はあとから付ける。Mac・Linux は「ルート (/)」、Mac は /Volumes も)。開いたときに中のフォルダを読む(`FolderNode.Loading`。仮の子で ▸ を出す)。隠し・システム・. で始まる名前は出さない。画像の枚数は数えない(ユーザーの希望)。フォルダを開くと `SyncFolderTree` で、いちばん近い根からそのフォルダまで開いて選ぶ(`syncingTree` のあいだは開かない)。右クリックは選択を変えない(トンネルの `PointerPressed` で `Handled`、対象は `menuTarget`)。ツリーにフォーカスがあるときは、画像の切り替えとファイル操作のキーを働かせない(Delete で一覧のファイルを消さないため)。見出しで最小化、境目で高さ変更(`FolderTreeCollapsed`・`FolderTreeHeight`)。上の「サブフォルダ」はメニューと同じ `SubfolderDepth`(`SetSubfolderDepth`)。Linux の Headless で、パンくず・ツリー・履歴・手入力・右クリック・深さ・最小化・パスの欄を隠す・全画面を確認(2026-11-08) |
| `PromptLoupe/MainWindow.Watcher.cs` / `FileChanges.cs`(Core) | 開いているフォルダの見張り(`FileSystemWatcher`)。ほかのアプリでの移動・削除・名前変更を一覧に反映する(2026-11-05。切り取ってエクスプローラーに貼り付けると、一覧に古い項目が残る不具合への対応)。ユーザーが WPF 版で、切り取り→エクスプローラーへ貼り付け、エクスプローラーでの削除・名前変更、Shift+ドラッグでの移動を確認し「問題なし」(2026-11-05)。通知は 300ms ためてから `FileChanges.Map` で読み替える。消えたはずのファイルが残っていれば何もしない(保存時に消して戻すアプリのため)。表示中でない画像が消えても、見ている画像は切り替えない。新しく増えた画像は加えない。WPF 版から移した(Linux で動作確認済み) |
| `PromptLoupe/MainWindow.Tags.cs` / `TagDictionary.cs`(Core) | タグの色分けと、Danbooru 辞書(danbooru.csv)の読み込み |
| `PromptLoupe/RenameWindow.axaml(.cs)` | 名前の変更ダイアログ |
| `PromptLoupe/TagStatsWindow.axaml(.cs)` / `MainWindow.TagStats.cs` | タグの集計(使っている画像の枚数)。一覧や絞り込みが変わると `NotifyTagStats` で数え直す(300ms 間引き) |
| `PromptLoupe/FileItem.cs` | 一覧の項目、検索用の正規化済み文字列、並び替え(自然順は `NaturalComparer`) |
| `PromptLoupe/ThumbnailService.cs` | サムネイルのメモリキャッシュ(LRU 600 枚、ディスクには保存しない) |
| `PromptLoupe/ImageLoader.cs` | 画像の読み込み(SkiaSharp。WIC の代わり)と、JPEG・WebP・TIFF の回転。表示用で回転が要らないものは `new Bitmap(Stream)` で Avalonia に直接読ませる(自前で展開して写すより 2〜3 割速く、読み込み中のメモリも半分。画素は一致を確認済み。2026-11-07)。PNG・JPEG・WebP・BMP・GIF は SkiaSharp、TIFF(先頭ページ)は LibTiff.NET(どの OS でも。exe に入る)、HEIC・HEIF は `HeifLoader`。TIFF は LibTiff の RGBA 読み出し(`ReadRGBAImageOriented`)で CMYK・16 ビット・JPEG 圧縮なども 8 ビット RGBA(アルファは乗算済み)にそろう。ただし LibTiff の RGBA 読み出しは Orientation 5〜8 の回転を扱わない(1〜4 の反転と同じ扱い)ので、ファイルと同じ系統の向きを要求して保存されたままの並びで読み、`ApplyOrientation` で 1〜8 を直す(1〜8 すべてテスト画像で確認済み)。表示できなかったときの案内は `UnsupportedHint(Exception)`(`HeifCodecMissingException` のときだけ入れ方を出す。画像の欄にも出す。ステータスバーはフォルダの読み込みの表示に上書きされるため) |
| `PromptLoupe/HeifLoader.cs` | HEIC を OS の機能で読む(2026-11-07)。Windows は WIC(COM を関数ポインターで直接呼ぶ。仮想関数表の位置はファイルの注釈。拡張機能がないと `WINCODEC_ERR_COMPONENTNOTFOUND`)、Mac は ImageIO(`CGImageSourceCreateThumbnailAtIndex` に `WithTransform` を付けると回転を当てた画像になる。大きさを指定しなければ元の大きさ)、Linux はその PC の libheif(`libheif.so.1`。`heif_init` でプラグインを読む。HEVC のプラグインがないときは error 4 / suberror 3000)。どれも irot を当てて返すので回さない。Magick.NET(2026-11-06 に入れた)をやめた理由: ユーザーが「DLL を exe に内蔵できないなら使いたくない」(LGPL の差し替えのため `Magick.Native` を別ファイルにしていた)。HEIC・TIFF で 500〜1000ms かかっていたのも Magick.NET(HEVC を CPU だけで展開)。NuGet の LibHeif.Native も古い(1.15.1)うえ GPL の x265 入りで MIT と表示されていたので不採用。Linux(Ubuntu の libheif 1.17.6 + libheif-plugin-libde265)で、nokiatech の適合性テストのうち、ふつうの画像とグリッド(iPhone の写真と同じ作り)は読めた。`iden` を使う特殊なもの(C008・C014・C023)と画像シーケンス(C026〜C032)は、この版の libheif では読めない。Windows(WIC)はユーザーが確認済み(2026-11-07)。Mac(ImageIO)は未確認 |
| `PromptLoupe/ImagePreloader.cs` | 表示用の画像の先読み(2026-11-07)。表示中と、一覧で前後を裏で読んでおく(送っている向きに 2 枚・逆向きに 1 枚。向きは `MainWindow` の `direction`。1 つずつ動いたときだけ更新)。ユーザーの「WPF 版より画像の切り替えが引っかかる(メモリ使用率 90% 超でも WPF 版はサクサク)」への対応。表示用の画像の持ち主はこのクラスで、`SetImage` では Dispose しない(手放すのは `Keep`・`Clear`)。更新日時が変わっていたら読み直す。Linux の Headless で、1792×2304 の PNG を送ったときの表示まで 103〜123ms → 1〜5ms。PNG は縮小しながら読めないので、大きい画像ほど展開に時間がかかる(画素数にほぼ比例)のは避けられず、先読みで隠している |
| `PromptLoupe/PlatformShell.cs` | 既定のアプリで開く・ファイルマネージャーで表示・ごみ箱(Windows は VisualBasic、Mac は osascript で Finder、Linux は `gio trash`、なければ freedesktop の決まりで自前)・プロパティ(Windows のみ。WPF 版と同じ `ShellExecuteEx`。ほかの OS ではメニューに出さない) |
| `PromptLoupe/FileClipboard.cs` | ファイルと画像のクリップボード(Avalonia 11.3 の `DataTransfer`)。切り取りの印は、Windows は「Preferred DropEffect」、Linux は `x-special/gnome-copied-files` と `application/x-kde-cutselection`、どの OS でもアプリ用の `PromptLoupe.cut`。Windows は入れたあとで `FlushAsync`(アプリを閉じても残す) |
| `PromptLoupe/HelpWindow.axaml(.cs)` / `Help.txt` | 使い方(`FlowDocument` の代わり)。本文は埋め込みリソースの `Help.txt`(見出し・箇条書き・**太字**・`コード` だけの簡単な書き方。書き方はファイルの先頭)。機能を足したり変えたりしたら、ここも直す |
| `PromptLoupe/MessageDialog.cs` | MessageBox の代わり(Avalonia には標準のものがない) |
| `PromptLoupe/THIRD-PARTY-NOTICES.txt` / `LicenseWindow.axaml(.cs)` | ライセンス表記(2026-11-05)。Avalonia・MicroCom・Tmds.DBus・LibTiff.Net は GitHub のライセンス文、ANGLE・SkiaSharp・HarfBuzzSharp は NuGet パッケージ内の LICENSE と THIRD-PARTY-NOTICES.txt(SkiaSharp と HarfBuzzSharp のものは同一なので1回だけ)をつないだもの。`LICENSE` とともに exe に埋め込み(`EmbeddedResource`)、「ヘルプ → ライセンス」(`LicenseWindow`)で表示する。配布物は exe だけ(Mac は .app だけ)。ユーザーの希望(2026-11-07)。パッケージのバージョンを変えたら作り直す(生成スクリプトはリポジトリに入れていない) |
| `PromptLoupe/macos/` / `tools/make_macos_app.sh` / `tools/install_rcodesign.sh` | Mac 版(2026-11-06)。Info.plist(`@VERSION@` を置き換える)と、`app.ico` から Pillow で作った `PromptLoupe.icns`。CI の Linux の上で Mac 向けに発行し(.NET SDK が本体にアドホック署名を付ける。dylib は各パッケージが署名済み)、`make_macos_app.sh` で .app にまとめ、rcodesign(版と SHA-256 を固定)で .app 全体にアドホック署名する。Contents/MacOS にはプログラムだけを置く(文書があれば Resources へ移す。今はライセンス文も exe に入っているので、ふつうは何もない)。macOS 12 以降。クラウドのセッションでは、ダウンロードした rcodesign の実行が許可されなかったので、署名は CI でだけ行う。`rcodesign verify` は .app に使えず、本体に使ってもアドホック署名(証明書なし)で「CMS error」になる(ツール自身が不具合ありと警告している)ので、`print-signature-info` に `code_directory` があることと、`_CodeSignature/CodeResources` があることで確かめる。CI(build.yml)で osx-arm64・osx-x64 の作成が通った(2026-11-06。Magick.NET 入りで約 55MB・45MB)。release.yml の Mac のジョブは、タグを push するまで試せない |
| `tools/make_icon.ps1` | `app.ico` を作る(Windows の PowerShell で WPF の描画機能を使う。アプリ本体とは無関係)。アイコンは、写真と、文字を映す虫眼鏡の絵(1.6.0 の公開前にタグの絵から変更)。16〜256px の PNG を詰めた .ico |

## 守ること・落とし穴

- **一覧は複数選択**(`SelectionMode="Extended"`)。表示中の1枚は `currentItem` で管理し、`SelectedItem` は使わない。項目をまとめて取り除くときは `suppressSelection` と `RemoveItems` を使う。削除や名前変更のあとは、フォーカスを新しい項目に移す(↑↓キーが先頭から始まるのを防ぐ)。
- **削除は既定で無効**(`AllowDelete`)。ごみ箱に移すだけで、完全削除はしない。確認ダイアログは `ConfirmDelete` で切り替え。この安全装置は外さない。
- 外からのドロップは「そのファイル/フォルダを開く」(コピーはしない)。一覧から持ち出すドラッグ中は `draggingOut` で、自分のウィンドウへのドロップを無視する。
- タグ辞書は、アプリに**同梱しない**(ライセンス未確認)。利用者が読み込む。
- **配布物に別ファイルを足さない**(ユーザーの希望。2026-11-07)。ライセンス文は exe に埋め込む。LGPL などで exe に入れられない部品(HEVC のデコーダーなど)は使わず、OS の機能を使う。
- 編集の前には、ファイルを読み直す。
- Avalonia の落とし穴: `ColumnDefinition` や `ScaleTransform` に `x:Name` を付けても、コードから使うフィールドはできない(コントロールではないため)。列は `BodyGrid.ColumnDefinitions[i]`、変換はコードで作って付ける。`ListBox` の `ItemsSource` を差し替えても前の選択が残ることがあるので、`Selection.Clear()` してから選び直す(`RefreshView`)。WPF の `ListCollectionView` はないので、絞り込みと並び替えは `RefreshView` で一覧を作り直す。`Image` の `RenderTransformOrigin` の既定は中央なので `0,0` にする。`DragEventArgs.Data` は古い書き方で、`DataTransfer` を使う。クリップボードも `DataTransfer` / `DataTransferItem` と `ClipboardExtensions`(`TryGetFilesAsync`・`SetBitmapAsync`・`DataFormat.CreateBytesPlatformFormat` など)を使う。`IStorageFile` は自分で実装できない(`NotClientImplementable`)ので、パスからは `StorageProvider.TryGetFileFromPathAsync` で作る(デスクトップではすぐ終わる)。チェック付きのメニュー(`ToggleType`)は、クリックの処理で設定から `IsChecked` を付け直す(自動で切り替わるかどうかに頼らない)。
- Avalonia 11.3.22 の、OS ごとのクリップボードとドラッグ(2026-11-05、パッケージの中身を逆コンパイルして確認):
  - クリップボードのファイルは、Windows は CF_HDROP、Linux(X11)は `text/uri-list`、Mac は `public.file-url`。画像(`DataFormat.Bitmap`)は、Windows は DIB・DIBV5・PNG、Linux は `image/png`、Mac は PNG。任意の形式は `CreateBytesPlatformFormat`(Windows は `RegisterClipboardFormat` の名前、Linux は MIME、Mac は UTI)。
  - Mac の Finder には、クリップボードで「移動」を伝える決まりがない。Finder に貼り付けるとコピーになる(このアプリの中で貼り付けたときだけ移動)。
  - **ほかのアプリへのドラッグは、Windows(OLE の `DoDragDrop`)と Mac だけ**。Linux(X11)にはドラッグ元の実装がなく、アプリの中だけのドラッグ(`InProcessDragSource`)になるので、Linux では一覧からの持ち出しを始めない(`CanDragOut`)。
  - 一覧の項目の選択は `ListBoxItem.OnPointerPressed`(押した時点)で変わる。複数選択のうち1枚をつかんでドラッグできるように、`FileList` にトンネルで `PointerPressed` を付け、その場合だけ `Handled` にして選択を変えない(離したときに1枚だけにする。WPF 版の `pendingSingle` と同じ)。
  - Windows の Avalonia はキーの表示名を持たず、`KeyGesture` の Enter が「Return」と表示される。そのため、プロパティのメニューには Alt+Enter を出していない(キーは効く)。
  - Headless のクリップボードはメモリ上の仮のもの(アプリの処理は確かめられるが、OS との連携は確かめられない)。Xvfb で X11 のクリップボードを2つのプロセスで受け渡す確認も試したが、文字だけでも受け渡せなかった(この環境の問題と判断。2026-11-05)。

## 既知の制限・未対応

- PixAI は対応しない。ダウンロードした PNG・WebP にメタデータが一切入っていない(PNG は IHDR・IDAT・IEND のみ。2026-11-01 に実物で確認、ユーザーも Web で同じ情報を確認)。ステルス PNGInfo(画素の最下位ビット)にも何も入っていない(RGBA だがアルファはすべて 255。2026-11-03 に実物 4 枚で確認)。NovelAI のステルス PNGInfo(`stealth_pngcomp`)への対応は見送り。NovelAI の画像は tEXt にも同じ情報があり、今のままで読めるため。GIF は先頭フレームのみ。TIFF は先頭ページのみ。TIFF の撮影情報(Exif)は未対応。HEIC は OS の機能(`HeifLoader`)次第(Windows は拡張機能、Linux は libheif のプラグインが要る)。Linux では、一覧からのドラッグ持ち出しができない(Avalonia 11.3 の制限)。
- 見送り: 虫眼鏡(部分拡大)、サムネイルのグリッド表示(仮想化パネルが必要)。

## 次にやること(候補)

- フォルダのパスの欄とフォルダツリー(2026-11-08。イメージ画像を見てユーザーが決めた形)。Windows・Mac での確認はまだ。

- 1.7.0(WPF 版の最後)を 2026-11-05 にリリースした(ユーザーが公開)。2.0.0(Avalonia 版。Windows・Mac・Linux)を 2026-11-08 にリリースした(ユーザーが公開)。release.yml の 5 つのジョブ(下書き・Windows・Mac 2 種・Linux)は初回で成功した。
- README の画面写真(`docs/images/main.png`)は WPF 版のまま(ユーザーの判断で差し替えない。2026-11-08)。
- Mac・Linux の利用者からのフィードバック(Issues)を待つ。

1. 経緯(Avalonia 版への移行。2026-11-04〜11-08): (2026-11-04 に第1段階: 一覧・サムネイル・絞り込み・並び替え・画像表示とズーム・メタデータのカード・タグの色分けと辞書・タグの集計・名前の変更・ごみ箱への削除・全画面・テーマ・設定)。Linux ではスクリーンショットで確認済み。Windows でもユーザーが確認し「全体的に問題ない」(2026-11-05。`dotnet run` のデバッグ版で)。確認用の JPEG・WebP(Orientation 1〜8 の JPEG・WebP、A1111 の UserComment の JPEG、カメラの Exif と GPS で Orientation 6 の JPEG、計 18 枚。Pillow で作成、リポジトリには入れていない)も問題なし。ただし「動作がちょっと重い気がした」(5,600 枚、830×1210 程度のフォルダ)。ユーザーが様子を見ている。読み込み自体は速い(1792×2304 の PNG で表示用 約 90ms、サムネイル 約 75ms。Linux・Release で計測)。重いと感じた操作が分かったら調べる。第2段階(2026-11-05): 一覧からのドラッグ持ち出し(Windows・Mac。Linux は Avalonia が未対応)、ファイルのコピー・切り取り・貼り付け、画像のコピー、プロパティ(Windows のみ)、使い方の画面(`Help.txt`)。Linux の Headless で、コピー・切り取り・貼り付け(同じ名前は「名前 (2)」、移動のあとはクリップボードを空にする)、画像のコピー、使い方の画面(スクリーンショット)、プロパティがメニューに出ないことを確認した。Windows でもユーザーが確認し「問題なし」(2026-11-05): 使い方(F1・Esc・テーマ)、右クリックメニュー、プロパティ(Alt+Enter)、画像のコピー(アプリを閉じたあとも貼り付けられる)、右パネルの文字の Ctrl+C、エクスプローラーとのコピー・切り取り・貼り付け(「名前 (2)」を含む)、ドラッグ持ち出し(Shift で移動、複数選択の1枚をつかむ、自分のウィンドウへのドロップを無視)。確認中に「上位と下位の同じ名前のファイルが片方しか出ない」ように見えたが、貼り付けで増えたファイルが一覧に加わっていなかっただけだった(新しく増えた画像は加えない仕様。不具合ではない)。Linux・Mac の OS のクリップボード・ファイルマネージャーとの連携は未確認。第3段階(2026-11-05): ライセンス表記(`PromptLoupe.Avalonia/THIRD-PARTY-NOTICES.txt`。使い方・バージョン情報・README も直した)、Avalonia 版の `<Version>` を WPF 版に合わせて 1.7.0 に、CI(build.yml)で配布と同じ形(自己完結型・単一ファイル、約 47MB)に発行して Actions の Artifacts に置く(win-x64・linux-x64、14 日。Releases には出さない)。Linux の単一ファイル版は、Xvfb の上で起動して画面が出ることを確認した。Windows の単一ファイル版(CI の Artifacts)も、ユーザーが起動・画像の表示・THIRD-PARTY-NOTICES.txt の同梱を確認した(2026-11-06)。第4段階(2026-11-06): HEIC・HEIF・TIFF の表示(Magick.NET)、Mac 版の .app と CI(build.yml の Artifacts、release.yml で下書きに追加)。ARM 版の Windows 向け(win-arm64)は作らない(2026-11-07、ユーザーが決定。Windows 11 の ARM 版は x64 版をエミュレーションで動かせる。win-arm64 の発行は WPF 版・Avalonia 版ともクラウドで通ることを確認済みで、Avalonia・SkiaSharp・Magick.NET とも win-arm64 の部品がある)。確認用の TIFF 10 枚と HEIC 3 枚(Pillow で作成。画像の中に正しい見え方を書いた)を zip でユーザーに渡した(リポジトリには入れていない)。Windows での HEIC(回転入りの C008 を含む)・TIFF(確認用 10 枚)の表示も、ユーザーが「問題なし」(2026-11-07)。先読みのあと、ユーザーは「だいぶ改善されたが、WPF 版よりまだ時間がかかる気がする」(2026-11-07)。原因の切り分けのため、一時的にステータスバーへ展開・待ち・描画の時間(`RequestAnimationFrame` を 2 回待って描画の時間を測った)を出し、F9 で縮小表示の品質(高・中・低)を切り替えられるようにした(確認後に消した)。Linux の Headless では 1792×2304 で展開 約 80ms・描画 約 25ms。Windows での計測(ユーザー): 品質を変えても描画の時間はあまり変わらず、引っかかるときは「待ち」が大きい画像で 150ms(先読みが追いつく前に送ったとき)。そこで、送る向きに 2 枚先読みし、表示用の読み込みのコピーを 1 回減らした(2026-11-07)。Linux の Headless では、大きい画像を 120ms ごとに送っても待ち 0ms。さらに、Magick.NET をやめて、TIFF は LibTiff.NET、HEIC は OS の機能(`HeifLoader`)にし、ライセンス文を exe に埋め込んだ(2026-11-07。配布物は exe だけ)。Windows でユーザーが確認(2026-11-07): HEIC(WIC。C008 の向きを含む)・TIFF の表示、配布物が exe だけになったこと、「だいぶよくなってる」「HEIC の展開も少し改善」。大きい TIFF(6000×4000 で約 0.5 秒)は、全体を展開するしかないので仕方ないとユーザーも納得。調査用の表示は消した。メニュー・キー操作・設定を WPF 版と突き合わせ、機能はそろったと判断(2026-11-07)。残り: release.yml の Mac のジョブ(次のタグで初めて動く)、Linux 版の配布をどうするか、WPF 版をやめる時期。Mac はユーザーの MacBook Pro 2016(Intel、macOS 12 まで)で確認できるが、Mac で使う予定がなければ放置してよい。Mac の配布は署名・公証がないと Gatekeeper に強く止められる。 2026-11-08: ユーザーが普段使いでも「問題なし」。WPF 版のコードを消し、フォルダを `PromptLoupe/` に改名、ソリューションを置き、2.0.0 に上げ、release.yml を Windows・Mac・Linux 向けに書き直し、README・使い方を Avalonia 版に合わせた。
2. 配布の準備。コード署名は**しない方針**(2026-11-01 決定。費用に見合わず、OV・EV 証明書でも SmartScreen の警告はすぐには消えないため)。README に SmartScreen の回避方法を書いた。警告が問題になってきたら、Microsoft Store(MSIX、Store 側で署名)か SignPath Foundation(OSS 向けの無料署名)を検討する。Mac は署名・公証がないと Gatekeeper に強く止められる(`xattr -cr` を案内)。名前は PromptLoupe に決定(Web・GitHub・J-PlatPat で同名なし、2026-11-01 確認。法的な確認ではない)。`LICENSE`(MIT、著作権者 yuto)。
