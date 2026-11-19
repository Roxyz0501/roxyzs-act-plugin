# Roxyzs ACT Plugin

Roxyz0501 製の ACT プラグインを一つのタブで管理します。Windows 版 Advanced Combat Tracker と .NET Framework 4.8 が必要です。

## 導入と操作

1. [最新の安定版](https://github.com/Roxyz0501/roxyzs-act-plugin/releases/latest)から ZIP を展開し、ACT の Plugins → Plugin Listing で `RoxyzsActPlugin.dll` を登録します。
2. **Roxyzs ACT Plugin → Config** の一覧で機能説明を確認し、使うプラグインを選んで **オン** を押します。管理済みの DLL、または ACT に登録済みで無効な同名 DLL を再利用します。見つからない場合は作者の公開 Release から取得します。単体版を使用中なら先に ACT 側で無効にしてください。
3. 有効にすると個別のタブが開き、元のプラグインの設定画面を使用できます。FFXIV Parsing Plugin や SPESPE など各機能が必要とする外部依存は、各行の **GitHub** から確認できます。
4. **オフ** は個別プラグインの終了処理を呼び、タブを閉じます。次回起動時はオンの項目だけ初期化します。.NET Framework は同一 AppDomain にロードしたアセンブリを ACT 終了までメモリに保持します。
5. **更新** で現在版・最新版・リリースノートを確認し、**更新を適用** を押します。有効な子プラグインはその場で旧版を停止し、新版をロードします。初期化が失敗すると旧版の再開を試みます。オフのものはファイルだけ準備します。ポータル自身の更新は、検証済み DLL のパスを表示します。ACT の登録画面でポータルを無効にして新しい DLL に切り替えてください。

## 掲載プラグイン

| プラグイン | 機能 |
| --- | --- |
| [Aether Tracker](https://github.com/Roxyz0501/aether-tracker-act) | 周囲のキャラクターの追跡 |
| [Bahamut Guide](https://github.com/Roxyz0501/bahamut-guide-act) | 絶バハの SPESPE 画像通知の調整 |
| [Chat Relay](https://github.com/Roxyz0501/chat-relay-act) | チャット・エモートの Discord 転送 |
| [Food Buff Overlay](https://github.com/Roxyz0501/food-buff-overlay) | 食事効果の監視と通知 |
| [PvP Chronicle](https://github.com/Roxyz0501/pvp-chronicle-act) | PvP のキル・アシスト記録 |
| [SocialDistance](https://github.com/Roxyz0501/socialdistance-act-plugin) | プレイヤーの距離と接近警告 |
| [SSRename](https://github.com/Roxyz0501/ssrename) | スクリーンショットのリネーム |
| [Target Marker Overlay](https://github.com/Roxyz0501/target-marker-overlay-act) | ターゲットマーカーの付与先表示 |
| [Timeline Notice Editor](https://github.com/Roxyz0501/timeline-notice-editor-act) | SPESPE タイムライン画像の配置編集 |

初回は同梱のカタログを表示します。「一覧を更新」または起動時の更新確認で、このリポジトリの catalog.json を照合します。追加・削除と4言語の説明はポータルの再インストールなしで反映されます。最新の取得に成功した一覧は設定に保存します。各プラグインのコード・更新版は各作者リポジトリの Release から取得します。

## 保存とダウンロード

設定と管理 DLL は ACT のアプリデータの Config/RoxyzsActPlugin 内に保存します。設定の変更を60秒周期および終了時に保存します。ポータルは初回起動時に全項目をオフにします。ローカル一覧の表示やタブ移動は配布 API にアクセスしません。起動時のポータル更新確認は更新タブで変更できます。手動確認ではセッション内の ETag を再利用します。

取得元を作者の登録済み GitHub リポジトリに限定します。正式版タグ、配布 ZIP 名、SHA-256、DLL のアセンブリ名とバージョンを照合し、指定の DLL のみをバージョン別フォルダーに保存します。既存版のファイルは更新時にも保持します。

## ビルドと公開

`dotnet build tests/PortalTests.csproj -c Release --configfile NuGet.Config` でビルドし、`tests/bin/Release/net48/PortalTests.exe` を実行します。引数 `--online` で公開中の掲載プラグイン全件の配布物も検証します。`ActPath` MSBuild プロパティで ACT の参照先を指定できます。

プロジェクトの Version を更新し、対応する vX.Y.Z タグを push すると GitHub Actions がビルド・テストし、RoxyzsActPlugin-vX.Y.Z.zip と .zip.sha256 を安定版として公開します。

開発支援は任意です。すべての機能をどなたでも利用できます。[Ko-fi で Roxyz0501 の開発を支援](https://ko-fi.com/roxyz0501)

