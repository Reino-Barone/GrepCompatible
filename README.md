# GrepCompatible

.NET 8.0で書かれた高性能なPOSIX準拠のgrep実装です。

📖 **Languages**: [English](README-en.md) | [日本語](README.md)

**Current Version**: v0.1.0 (July 22, 2025)

---

## 機能

### 基本機能

- **POSIX準拠**: 標準的なgrepコマンドラインオプションと動作を実装
- **高性能**: SIMD最適化、並列処理、メモリ効率最適化により高速動作
- **クロスプラットフォーム**: Windows、Linux、macOSで.NET 8.0を使用して動作
- **パターンマッチング**: 正規表現、固定文字列、拡張正規表現パターンをサポート

### 検索オプション

- **大文字小文字を区別しない検索** (`-i`, `--ignore-case`)
- **マッチ反転** (`-v`, `--invert-match`) - マッチしない行を選択
- **行番号** (`-n`, `--line-number`) - マッチした行に行番号を表示
- **カウントのみ** (`-c`, `--count`) - ファイルごとのマッチ行数のみを表示
- **ファイル名のみ** (`-l`, `--files-with-matches`) - マッチを含むファイル名のみを表示
- **ファイル名を抑制** (`-h`, `--no-filename`) - 出力でファイル名プレフィックスを隠す
- **サイレントモード** (`-q`, `--quiet`) - 通常出力をすべて抑制

### パターンタイプ

- **拡張正規表現** (`-E`, `--extended-regexp`)
- **固定文字列** (`-F`, `--fixed-strings`) - パターンを文字列リテラルとして扱う
- **単語全体マッチング** (`-w`, `--word-regexp`) - 単語全体のみをマッチ

### ファイル操作

- **再帰検索** (`-r`, `--recursive`) - ディレクトリを再帰的に検索
- **インクルードパターン** (`--include`) - パターンにマッチするファイルのみを検索
- **除外パターン** (`--exclude`) - パターンにマッチするファイルをスキップ
- **標準入力サポート** - ファイルが指定されていない場合は標準入力から読み込み

### 出力制御

- **コンテキスト行** (`-C`, `--context`) - マッチ周辺のN行のコンテキストを表示
- **前方コンテキスト** (`-B`, `--before-context`) - マッチ前のN行を表示
- **後方コンテキスト** (`-A`, `--after-context`) - マッチ後のN行を表示
- **最大カウント** (`-m`, `--max-count`) - N回のマッチ後に停止

## インストール

### オプション1: 自己完結型実行ファイル（推奨）

Windows用の自己完結型実行ファイルをダウンロードして簡単インストール：

1. [リリースページ](https://github.com/Reino-Barone/GrepCompatible/releases)から最新版をダウンロード
2. ZIPファイルを展開
3. **PowerShell（推奨）**: `install-windows.ps1`を実行
   ```powershell
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
   .\install-windows.ps1
   ```
4. **コマンドプロンプト**: `install-windows.bat`をダブルクリックまたは実行

#### 利用可能なアーキテクチャ
- `win-x64`: 64ビットWindows（最も一般的）
- `win-x86`: 32ビットWindows
- `win-arm64`: ARM64 Windows（Surface Pro Xなど）

#### インストールオプション
- **ユーザーインストール（デフォルト）**: `%LOCALAPPDATA%\GrepCompatible`にインストール
- **システム全体インストール**: `%ProgramFiles%\GrepCompatible`にインストール（管理者権限必要）
  ```powershell
  .\install-windows.ps1 -ForAllUsers
  ```

#### アンインストール
```powershell
.\install-windows.ps1 -Uninstall
```

### オプション2: GitHubリリースから手動インストール

1. [リリースページ](https://github.com/Reino-Barone/GrepCompatible/releases)から`grep.exe`をダウンロード
2. 任意のディレクトリに配置（例：`C:\Tools\`）
3. そのディレクトリをPATH環境変数に追加

### オプション3: ソースからビルド

#### 前提条件

- .NET 8.0 SDK以降

#### ビルド手順

```bash
git clone https://github.com/Reino-Barone/GrepCompatible.git
cd GrepCompatible
dotnet build -c Release
```

#### 実行

```bash
# プロジェクトから直接実行
dotnet run --project src -- [OPTIONS] PATTERN [FILE...]

# または実行可能ファイルをビルドして使用
dotnet publish src -c Release -o ./publish
./publish/GrepCompatible [OPTIONS] PATTERN [FILE...]
```

#### 自己完結型実行ファイルの作成

Windows用の配布可能な実行ファイルを作成：

```bash
# Windows x64向け（単一ファイル）
dotnet publish src -c Release -r win-x64 --self-contained true -o ./dist/win-x64 -p:PublishSingleFile=true

# 全アーキテクチャのリリースパッケージを作成
pwsh scripts/build-release.ps1

# 特定のアーキテクチャのパッケージを作成
pwsh scripts/create-package.ps1 -Runtime win-x64
```

作成された`grep.exe`は.NETランタイムを内包した自己完結型実行ファイルです。

## 使用方法

### 基本的な使用方法

```bash
# file.txtで"hello"を検索
GrepCompatible hello file.txt

# すべての.txtファイルで"hello"を検索
GrepCompatible hello *.txt

# 大文字小文字を区別しない検索
GrepCompatible -i hello file.txt

# 行番号を表示
GrepCompatible -n hello file.txt

# ディレクトリ内を再帰的に検索
GrepCompatible -r hello /path/to/directory

# コンテキスト行付きで検索
GrepCompatible -C 3 hello file.txt
```

### 高度な使用方法

```bash
# 拡張正規表現パターン
GrepCompatible -E "hello|world" file.txt

# 固定文字列検索（正規表現なし）
GrepCompatible -F "hello.world" file.txt

# 単語全体マッチング
GrepCompatible -w hello file.txt

# マッチ数のみ表示
GrepCompatible -c hello file.txt

# マッチを含むファイル名のみ表示
GrepCompatible -l hello *.txt

# マッチ反転（マッチしない行）
GrepCompatible -v hello file.txt

# インクルード/除外ファイルパターン
GrepCompatible --include="*.cs" --exclude="*.Test.cs" hello /path/to/source
```

### 標準入力からの読み込み

```bash
# 標準入力から読み込み
echo "hello world" | GrepCompatible hello

# パイプを使用
cat file.txt | GrepCompatible -i hello
```

## アーキテクチャ

### コアコンポーネント

- **GrepApplication**: メインアプリケーションエントリーポイントとオーケストレーション
- **GrepEngine**: ファイル検索用の並列処理エンジン
- **MatchStrategy**: プラガブルパターンマッチング戦略
- **OutputFormatter**: POSIX準拠の出力フォーマッティング
- **CommandLine**: 堅牢なコマンドライン引数解析

### パフォーマンス最適化

- **SIMD最適化**: 高速文字列検索のためのSIMD命令活用
- **並列処理**: マルチスレッドファイル処理とWork-Stealing戦略
- **メモリ管理**: 効率的なメモリ使用のため`ArrayPool<T>`とメモリプールを使用
- **非同期I/O**: ブロックしないファイル操作
- **最適化された文字列操作**: 効率的な文字列検索とマッチング

## 開発

### プロジェクト構造

```text
GrepCompatible/
├── src/
│   ├── GrepCompatible.csproj
│   ├── Program.cs
│   ├── Abstractions/        # インターフェースと契約
│   ├── CommandLine/         # コマンドライン解析
│   ├── Constants/           # 共有定数
│   ├── Core/               # コアアプリケーションロジック
│   ├── Models/             # データモデル
│   └── Strategies/         # パターンマッチング戦略
├── scripts/                # ビルドと配布スクリプト
│   ├── build-release.ps1   # 完全リリースビルド
│   ├── build-windows.ps1   # Windows実行ファイルビルド
│   ├── build-windows.bat   # Windows実行ファイルビルド（バッチ）
│   ├── create-package.ps1  # インストールパッケージ作成
│   ├── install-windows.ps1 # Windows インストーラー
│   └── install-windows.bat # Windows インストーラー（バッチ）
├── tests/                  # 単体テスト
└── GrepCompatible.sln
```

### テストの実行

```bash
# 全テスト実行
dotnet test

# 特定のテストカテゴリ実行
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
dotnet test --filter Category=Performance
```

### ビルド

```bash
# デバッグビルド
dotnet build

# リリースビルド
dotnet build -c Release

# 配布用パブリッシュ（フレームワーク依存）
dotnet publish -c Release -o ./publish

# 自己完結型実行ファイル（Windows x64）
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./dist/win-x64
```

### 配布パッケージの作成

```bash
# 全アーキテクチャのリリースパッケージを作成（PowerShell）
pwsh scripts/build-release.ps1

# 特定のアーキテクチャのパッケージを作成
pwsh scripts/create-package.ps1 -Runtime win-x64

# Windowsでバッチファイルを使用
scripts\build-windows.bat
```

作成されたパッケージには以下が含まれます：
- 自己完結型実行ファイル（`grep.exe`）
- インストールスクリプト（PowerShellとバッチ両対応）
- インストール手順書（`README.txt`）

## パフォーマンス

GrepCompatibleは以下の要素で高性能を実現：

- **SIMD最適化**: AVX2/SSE4.2命令による高速文字列検索
- 複数のCPUコアでの並列ファイル処理
- `Span<T>`を使用したメモリ効率的な文字列操作
- `ArrayPool<T>`とカスタムメモリプールによる最適化されたバッファ管理
- ブロックを防ぐ非同期I/O操作
- キャンセレーション対応による応答性向上

### ベンチマーク結果

現在の実装は多くのケースで GNU grep と同等またはそれ以上のパフォーマンスを示しています。詳細なベンチマークは `tests/Performance/` で確認できます。

## 互換性

### POSIX準拠

- POSIXのgrep仕様に準拠
- 互換性のある終了コード（マッチ時0、マッチなし時1、エラー時2）
- 標準オプション形式（`-h`, `--help`など）

### プラットフォーム

- Windows 10/11
- Linux（Ubuntu、CentOSなど）
- macOS 10.15+

### .NETバージョン

- .NET 8.0以降が必要
- モダンなC#機能を使用（プライマリコンストラクタ、レコード型、`required`修飾子など）

## コントリビューション

プロジェクトへの貢献を歓迎します！

### 貢献の手順

1. リポジトリをフォーク
2. 機能ブランチを作成 (`git checkout -b feature/amazing-feature`)
3. 変更を実装とコミット (`git commit -m 'Add amazing feature'`)
4. 新機能にテストを追加
5. すべてのテストがパスすることを確認 (`dotnet test`)
6. ブランチにプッシュ (`git push origin feature/amazing-feature`)
7. プルリクエストを送信

### 開発ガイドライン

- 新機能には必ずテストを追加
- コードは既存のスタイルに従う
- パフォーマンスに影響する変更はベンチマークも含める
- ドキュメントを適切に更新する

## ライセンス

このプロジェクトはMITライセンスの下でライセンスされています - 詳細は[LICENSE](LICENSE.md)ファイルを参照してください。

## 謝辞

- GNU grepやその他のPOSIX準拠実装からインスピレーション
- モダンなC#と.NETパフォーマンスベストプラクティスで構築
- 教育目的と本番環境の両方での使用を想定して設計
