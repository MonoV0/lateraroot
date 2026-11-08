# lateraroot

**Windows file widgets.**

ファイルをジャンルごとの正方形ウィジェットで扱う、Windows向けのデスクトップカスタマイズアプリです。画面端へ配置し、ホバー・クリックでファイルを展開します。通常のアプリを使う間は、同じライブラリをコンパクトなレールから呼び出せます。

![正方形ウィジェットの展開・収納アニメーション](docs/motion.gif)

プレビューはアプリ自身のWPF描画を白い背景へ合成したものです。実際のデスクトップ画像ではありません。カテゴリもファイルも同じ正方形で、余った領域は透過します。

[静止画](docs/desktop-expanded.png) · [設定画面](docs/settings.png) · [設計と参考資料](docs/DESIGN.md)

![作業中のコンパクト表示](docs/compact.png)

## ダウンロード・起動

1. [Releases](https://github.com/MonoV0/lateraroot/releases) から `lateraroot-*-win-x64.zip` をダウンロードします。
2. ZIP を任意のフォルダーへ**すべて展開**します。
3. `Lateraroot.exe` を起動します。デスクトップでは正方形のファイルウィジェット、アプリの作業中はコンパクトなレールが表示されます。

旧版から更新する場合は、まず通知領域から旧版を終了し、新しい ZIP を別フォルダーへ展開して起動してください。分類設定と収納履歴は同じ保存先から読み込みます。サインイン時起動を利用中で実行ファイルの場所を変えた場合は、設定で一度オフ→オンにして起動先を更新してください。

Windows 10 / 11（64 bit）向けです。配布 ZIP は .NET 同梱で、管理者権限や別途ランタイムのインストールは不要です。初期版はコード署名がありません。

## 使い方

- **見る:** カテゴリの正方形へホバー、またはクリックすると、同じサイズのファイルが端から順に現れます。`OPEN` で複数行、`CLOSE` で1行へ戻します。ファイルはクリックで開きます。ホイールや Page Up / Page Down で一覧を送れます。
- **探す・整理する:** ウィジェットを右クリック →「検索・整理」、または一覧にフォーカスして Ctrl+F。共通インスペクターで検索・名前／更新日／種類／サイズの並び替え・収納・復元を行います。更新日とサイズは降順です。
- **片付ける:** 「デスクトップを整理」を押し、内容を確認すると、ユーザーのデスクトップ直下にある通常のファイルとフォルダーを保管領域へ移動します。対象は全スタックです。新しいファイルは自動移動しません。
- **戻す:** 「元に戻す」は選択中スタックの収納済み項目を復元します。1件ずつ戻す場合はファイルを右クリックします。同名項目がある場合は ` (restored 1)` などを付け、既存ファイルを残します。
- **設定:** `lr.` の正方形、右クリックメニュー、または通知領域から開きます。正方形のサイズ（標準88 DIP、72〜112 DIP）、画面端、ディスプレイ、端に沿った位置、ジャンル名・色（#RRGGBB）・分類ルール・優先順位を編集できます。ジャンルは最大16個です。画面が狭い場合は正方形を保って複数列へ収めます。
- **移動:** `lr.` 上部のハンドルやコンパクト表示のハンドルをドラッグすると、近い画面端へ吸着します。ほかのディスプレイへも移動できます。設定の位置スライダーはキーボードでも調整できます。位置は保存されます。
- **最小化:** `−` で小さなタブにします。タブの `↗` で戻せます。タブもドラッグできます。
- **非表示:** コンパクト表示の `×` や右クリックの「非表示」で、ウィジェットと一覧を隠します。終了はせず、通知領域の「表示する」または `Ctrl+Alt+Space` で戻せます。最小化・非表示の選択も次回起動時に引き継ぎます。
- **固定:** インスペクター上部の「固定」で開いたままにします。Esc で閉じます。
- **動き:** 展開は端からの短いスライドと段階的な表示、収納は短いフェードです。素早い再ホバーで収納を中断できます。Windowsのアニメーション設定に従い、設定の「動きを減らす」でも即時表示へ切り替えられます。
- **呼び出す:** `Ctrl+Alt+Space` で一覧を開閉し、非表示・最小化中は表示を戻します。競合時は通知領域のアイコンから開いてください。
- **終了:** 通知領域の Lateraroot を右クリック →「終了」。サインイン時の起動は設定から任意で有効にできます。

初期ジャンルは画像、ドキュメント、メディア、アーカイブ、ショートカット、フォルダー、その他です。既存の色設定は更新時に保持します。新しい標準色にそろえる場合は、設定の「標準ルールに戻す」を使ってください。

デスクトップ用はシェルの上・通常アプリの下の表示順です。最前面固定の大きなパネルにはしません。正方形は辺の長さを固定し、画面の高さへ引き伸ばしません。表示はタスクバーを除く作業領域に沿います。通常の Windows デスクトップが前面になったことをイベントで判定します。エクスプローラーのフォルダー画面はデスクトップとして扱いません。自分の設定画面を操作中は元の表示を維持します。

### 分類ルールの例

| スタック | ルール |
| --- | --- |
| 仕事の資料 | `.pdf, .docx, .xlsx, .pptx` |
| スクリーンショット | `.png, .jpg, .webp` |
| フォルダー | `<folders>` |
| その他 | `*` |

拡張子の大文字・小文字は区別せず、`.pdf`、`pdf`、`*.pdf` を使えます。拡張子が重なるときは上のルールが優先されます。`*` はほかのルールに一致しない項目専用です。設定を変更すると収納済みファイルも再分類されます。

## ファイルとデータ

起動しただけではファイルを移動しません。通常のデスクトップを一覧表示し、明示的に「整理」を実行したときだけ収納します。共有デスクトップは表示のみです。隠し項目、システム項目、ジャンクション／シンボリックリンクは対象外です。`.lnk` ショートカット自体は収納できます。

標準の保存先は `%LOCALAPPDATA%\Lateraroot\Data` です。

| ファイル・フォルダー | 内容 |
| --- | --- |
| `settings.json` | 配置・スタック・並び順 |
| `library.json` | 元のパスと収納先の復元記録 |
| `Vault\<id>\<元の名前>` | 実ファイル |

移動前に復元記録をディスクへ保存します。アプリが終了しても実ファイルは `Vault` に残り、エクスプローラーからアクセスできます。バックアップは **Data フォルダー全体**を保存してください。アプリを削除する前に、各スタックの「元に戻す」で必要な項目を復元してください。収納中のファイルは元のパスが変わるため、ほかのアプリの参照先やショートカットのリンク先に影響する場合があります。

アプリ内のネットワーク通信・アカウント・テレメトリーはありません。シェルのアイコン取得やファイルを開く際は Windows の関連付けが使われます。

## 開発

[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) と Windows が必要です。WPF / Windows Forms の標準ライブラリを使い、外部のアプリケーション用 NuGet パッケージには依存しません。

メインフォントは同梱の **Zen Kaku Gothic New Medium** です。Windows へのフォントインストールは不要です。Light / Regular は未使用のため同梱していません。

```powershell
dotnet build src/Lateraroot/Lateraroot.csproj -c Release
dotnet run --project tests/Lateraroot.Core.Tests -c Release
dotnet run --project src/Lateraroot -- --demo
```

`--demo` では実際のデスクトップと切り離したサンプルを作成します。実装テスト用の保存先を指定することもできます。

```powershell
dotnet run --project src/Lateraroot -- --demo --data-dir C:\temp\lateraroot-demo
dotnet run --project src/Lateraroot -- --data-dir C:\temp\lateraroot-data --desktop-dir C:\temp\test-desktop
```

`--desktop-dir` を指定すると共有デスクトップ表示・サインイン時起動は無効になります。保存先を対象デスクトップの中に置くことはできません。

同梱フォント、正方形の実寸・透過、四辺配置、デスクトップ層、1,500件での仮想化、アニメーションの途中／完了／中断・動きを減らす設定、検索・収納・復元を確認するUI検証も実行できます。毎回新しい保存先を指定してください。結果JSON・描画PNG・動作GIFが保存先の `verification` に作成されます。実際のマウス入力を行うテストではありません。

```powershell
dotnet run --project src/Lateraroot -- --demo --verify-ui --data-dir C:\temp\lateraroot-ui-test
```

配布用ビルド:

```powershell
dotnet publish src/Lateraroot/Lateraroot.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

GitHub Actions でもテストと Windows 向けビルドを実行します。`v*` タグを push すると ZIP と SHA-256 をリリースに添付します。

## 現在の範囲

レールは選択した1画面の上下左右のいずれかに配置します。ファイル名パターンや日付による分類、ファイルのドラッグ＆ドロップでの任意ジャンルへの登録は未対応です。並び順は全ジャンル共通です。

フォルダーの移動は同一ボリュームでの移動に対応します。別ボリュームや使用中の項目など移動できないものはそのまま残し、結果を表示します。OneDrive 等の同期・オンライン専用ファイルは利用環境に依存します。複数画面・異なる DPI 向けの処理は実装していますが、全ハードウェア構成で検証済みではありません。

## 実装と軽量化

- 分類・検索・収納・復元とインスペクターは共通です。表示用ウィンドウは正方形グリッドと共通のファイル表示用の2個です。
- 前面アプリの変化は Windows のイベント通知で受け取り、定期的な前面ウィンドウのポーリングは行いません。タイトルや操作履歴は保存しません。
- ファイル変更通知はまとめて処理します。非表示中は一覧の再走査を保留し、表示を戻すと更新します。
- ファイル一覧はネイティブのリサイクル行で仮想化し、可視セルにバインドされたアイコンだけを取得します。閉じる・隠す操作で一覧要素を解放し、アイコンキャッシュには上限を設けています。
- 画面中央を覆う透明な全画面ウィンドウ、WebView、常時アニメーションは使いません。アニメーションは状態変化時の変形と透明度に限定し、ウィンドウの幅・高さは補間しません。
- 構造用アイコンは共通のベクター線、メインフォントはZen Kaku Gothic New Mediumです。短い日本語カテゴリ名は2行を均等にし、暗いカスタム色では文字色を自動調整します。

UI/UX Pro Maxの検索ガイドと参考サイトの実際のCSS・モーション調査を使って設計しました。採用・除外した指針は [docs/DESIGN.md](docs/DESIGN.md) に記録しています。

## Contributing / License

[CONTRIBUTING.md](CONTRIBUTING.md) を参照してください。ソースは MIT License、同梱フォントは SIL Open Font License 1.1 です。[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) と `Assets/Fonts/OFL.txt` を参照してください。

---

**English:** Lateraroot is a Windows desktop organizer with square file widgets on the desktop and a compact dock while using other apps. Widgets use transparent unused space, a desktop-level Z-order, finite interruptible motion, and native list virtualization. Drag the compact dock to any screen edge, minimize it to a tab, or hide and restore it from the tray. The main font is bundled Zen Kaku Gothic New Medium (SIL OFL). Configure extension-based groups, search and sort files, explicitly move desktop items into a local vault, and restore them without overwriting existing files. Data stays local. Build with .NET 10 SDK; portable x64 releases include the runtime. See the Japanese guide above for commands, storage details, and current limitations.
