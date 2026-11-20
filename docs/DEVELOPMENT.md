# 開発・ビルド

コマンドはリポジトリ直下のPowerShellで実行します。

## アプリをビルドする

Windowsの.NET Framework C#コンパイラを使います。追加パッケージは不要です。

```powershell
./scripts/build.ps1
```

出力先は `HOI4VoiceStudio/HOI4VoiceStudio.exe` です。更新するexeを起動している場合は先に閉じてください。

## インストーラをビルドする

[NSISの準備](../installer/BUILD.md)を済ませて実行します。

```powershell
./scripts/build-installer.ps1
```

NSISが別の場所にある場合は `-CompilerPath "C:\path\to\makensis.exe"` を指定します。
出力先は `dist/UnitVoiceStudio-Setup-1.0.0.exe` です。
同梱するのはアプリ・操作説明・配布案内・第三者ライセンス表示です。

## 検証する

```powershell
$appPath = (Resolve-Path './HOI4VoiceStudio/HOI4VoiceStudio.exe').Path
$gamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Hearts of Iron IV'
$testOutput = Join-Path $PWD ('tests/run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$run = Start-Process -FilePath $appPath -ArgumentList '--self-test', ('"' + $gamePath + '"'), ('"' + $testOutput + '"') -WindowStyle Hidden -Wait -PassThru
$run.ExitCode
Get-Content (Join-Path $testOutput 'test-results.txt')
```

自動検証は音声定義の解析、WAV変換、国・用途を分離した置換、保存・復元、登録先の検出・修復、引き継ぎを確認します。マイク録音は開始しません。

| オプション | 用途 |
|---|---|
| `--self-test GAME OUTPUT` | 音を出さずに自動検証 |
| `--preview-test GAME OUTPUT` | 4か国の音声を再生し、終了・停止を検証 |
| `--scan GAME OUTPUT` | 音声一覧と移動音声の参照データを取得 |

OUTPUTには専用フォルダーを指定してください。CLIのエラーはexe隣の `command-error.txt` に出力します。

インストーラの導入・更新・削除・録音保持は、`./scripts/test-installer.ps1` で検証します。
テストは `tests/run-installer-*` 内で行い、通常のショートカットとアンインストール一覧への登録は省略します。

## 実装の概要

- 本体・統合DLC・DLCの `.asset` を解析し、国タグ・用途別の音声を取得します。
- 録音は `sound/uvr/` に保存し、対象イベントの参照先だけを専用IDへ変更します。該当する元の定義ファイルと同じ仮想パスへ出力します。
- 試聴時はWAVを16bit PCMへ変換し、Windowsの再生完了通知で終了状態を更新します。
- 登録先はWindowsの実際のドキュメント等から検出し、HOI4の既存ファイルを確認してから登録します。

同じ音声定義ファイルを変更する別のModとは競合する場合があります。
`data` は録音・割り当て・元定義を保持し、`UnitVoiceMod` はそこから再生成します。

## 公開する

1. ソースをコミットし、生成物・録音・元音声・個人設定を含めないことを確認します（`.gitignore` で除外済み）。
2. インストーラとSHA-256値をGitHub Releasesへ添付します。配布ファイルが変わる場合はバージョンも更新します。
3. 不具合はIssuesで受け付けます。報告フォームは `.github/ISSUE_TEMPLATE/` にあります。

独自コードの再利用ライセンスは設定していません。ゲーム素材にはHOI4側の規約、第三者ソフトウェアには各ライセンスが適用されます。

検証記録：開発版のゲーム内動作は2026-11-21に利用者から確認報告あり。OneDriveの移動先はテスト用フォルダーで検証し、実際のクラウド同期やすべての別PC・他Modとの組み合わせは未確認です。
