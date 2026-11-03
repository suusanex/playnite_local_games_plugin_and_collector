# ローカルゲームカタログ

DLsite の作品 ID が入った CSV から共通の JSONL カタログとカバー画像を作り、Playnite のライブラリに表示します。タグ、お気に入り、クリア状態、評価は Playnite 側に残り、カタログの再生成やライブラリ更新では上書きしません。

## 前提

- .NET SDK 10 以降。collector は File-based App です。
- Playnite 10。この環境でのビルド確認は Playnite 10.63 に同梱の SDK 6.18 です。
- 入力 CSV は UTF-8 で、先頭行に `key` 列があります。`key` は `RJ01234567` のような DLsite 作品 ID です。分類や評価の列は取り込みません。

## カタログを作る

```text
dotnet run --file src/DlsiteCollector/DlsiteCollector.cs -- --input <csv> --output <フォルダ>\games.jsonl
```

`--input` と `--output` は必須です。個人の CSV パスはコードに入っていません。

生成されるもの:

- `games.jsonl`。1 行が 1 作品です。
- `media/dlsite/<作品ID>/cover.jpg` など。パスは JSONL があるフォルダからの相対パスです。

終了コード:

- 0: すべての行が成功した。
- 1: 一部の行または取得が失敗した。成功した作品は書き出してある。標準エラーに作品 ID、または行番号が出る。
- 2: CSV が読めない、`key` 列がない、正規化後の作品 ID が重複している、既存カタログが壊れている、または書き込みに失敗した。この場合は既存の `games.jsonl` を置き換えない。

作品 ID ではない `key` はその行だけ失敗し、他の成功分は残します。取得に失敗した作品は、同じ ID の前回成功レコードとカバーを残します。CSV から消した ID は新しい JSONL には出ません。すでに Playnite へ入っているゲームは、ここでは削除しません。

再実行しても ID は `dlsite:` に作品 ID を付けたままです。作品 ID の英字だけを大文字にそろえ、数字は変えません。

## Playnite へ入れる

1. Playnite を終了します。
2. プラグインをビルドします。

```text
dotnet build src/LocalGameCatalog.PlaynitePlugin/LocalGameCatalog.PlaynitePlugin.csproj
```

3. `src/LocalGameCatalog.PlaynitePlugin/bin/Debug` から次のファイルを `%APPDATA%\Playnite\Extensions\LocalGameCatalog` へコピーします。

- `extension.yaml`
- `icon.png`
- `LocalGameCatalog.PlaynitePlugin.dll`
- `LocalGameCatalog.Core.dll`

4. Playnite を起動します。
5. アドオンの拡張機能設定で Local Game Catalog を開き、「カタログ JSONL のパス」に `games.jsonl` のフルパスを入力して閉じます。
6. ライブラリを更新します。

失敗したときは `%APPDATA%\Playnite\extensions.log` を見ます。カタログパスが空、ファイルが読めない、カバーが無い、カバーの保存に失敗したときは、ここに英語のメッセージが出ます。

## 更新で変わるもの

ライブラリ更新で書き換えるのは、作品名、製品ページのリンク、カバー、開発元、発売日、ソース名（DLsite）です。

タグ、カテゴリ、お気に入り、非表示、クリア状態、ユーザースコア、プレイ時間、インストール状態、インストール先、メモはカタログから代入しません。

開発元が空のレコードは、すでに付いている開発元を消しません。カバーが空、またはファイルが無い場合は、既存のカバーを残します。Playnite のインポート除外に入っている作品は取り込みません。

同じ作品は `dlsite:` で始まる ID で照合するので、更新のたびに重複登録しません。

## 制限

- DLsite へのログインや購入履歴の同期はしません。公開されている `product.json` だけを使います。
- ゲームのダウンロード、インストール、起動はしません。
- カタログから消えた作品を Playnite から自動では削除しません。不要なゲームは Playnite 上で削除してください。
- CSV の分類、評価、メモは取り込みません。
- 2 つ目以降のストアには対応しません。

## 確認結果

ビルドと単体テスト、実データの収集、Playnite 上の表示は別々に記録します。

### ビルドと単体テスト

- `dotnet test tests/LocalGameCatalog.Tests/LocalGameCatalog.Tests.csproj`: 17 成功、0 失敗。
- プラグインプロジェクトは、インストール済み Playnite SDK 6.18 に対して警告 0 でビルドできた。
- SDK は .NET SDK 11.0.100-rc.1 です。NETSDK1057 のプレビュー利用メッセージが出ます。

### 実データ

- 公開 ID `RJ01041722`、存在しない ID、作品 ID ではない `key` の 3 行で collector を実行した。成功 1、失敗 2、終了コード 1。成功した作品の JSONL とカバーファイルを確認した。
- 指定の 176 行 CSV では成功 173、失敗 3、終了コード 1。失敗は作品 ID ではない `key` で、行番号は 7、33、50。カバーは 173 件、欠けは 0 件。
- 同じ CSV をもう一度実行した。終了コードは 1、成功 173、失敗 3。カタログの ID 集合は再実行の前後で同じ 173 件だった。

### Playnite

普段使っているライブラリ `%APPDATA%\Playnite\library` は変更していません。

Playnite 10.63 がすでに起動していたため、別プロセスでの確認はできませんでした。`--userdatadir` を付けた起動も、実行ファイル名を変えた起動も、`Application already running, shutting down.` と記録して終了しました。単一インスタンスの制限です。タイトル、カバー、製品リンク、タグの維持、重複の有無は、Playnite を終了してから上の手順で確認してください。プラグインのビルド自体は、同じ SDK に対して成功しています。
