# 修正 script

## 事前準備

### ソフトウェア

- .NET 5.0 をインストール
    - https://docs.microsoft.com/ja-jp/dotnet/core/install/
    - インストール後、ターミナルソフトウェアから、`dotnet --version` で、`5.0` 以上であることを確認する
- gitをインストール

### 設定

- gitで、このリポジトリをローカルコンピュータにクローンする
    - `git clone <このリポジトリのgit url>`
- JWT認証用のconfig.jsonを、プロジェクトフォルダ直下に配置する
- `Config.cs` ファイルを開き、以下の個所を変更する
    - `PathToConfigFile` JWT認証用のconfig.jsonファイルへのパス
    - `TopFolderName` 新たに作成するトップレベルフォルダの名称
    - `SearchUserId` 検索用AppUserのId
    - `SearchUserRole` 検索用AppUserをトップレベルフォルダに招待するときの権限
    - `AppUsers` 邸情報を所有しているAppUserのIDのリスト（95ユーザー分）

## 使い方

ターミナルを開き、プロジェクトフォルダ直下で、以下のコマンドを実行

### `dotnet run move`

以下の内容で修正スクリプトを実行する。

- `Config.AppUsers`に指定されたすべてのユーザーを処理対象にする
- 新トップフォルダを`Config.TopFolderName`で指定された名前で作成する
- 検索用ユーザー `Config.SearchUserId` を、`Config.SearchUserRole`権限で、トップレベルフォルダに招待する
- トップレベルフォルダ（新トップフォルダ以外）を処理対象にする
- 新トップフォルダ以外のトップレベルフォルダから、`Config.SearchUserId`のコラボレーションを削除する
- トップレベルフォルダを、新トップフォルダの配下に移動する


<hr>

以下のコマンドは`move`が成功していることを確認するために準備しました。 利用する必要は有りません。

### `dotnet run check`

`dotnet run move` が正しく行われたか以下の観点でチェックする

- `Config.AppUsers`に指定されたすべてのユーザーを処理対象にする
- 指定の名前の新トップレベルフォルダが作られているか
- 新トップレベルフォルダに、検索用AppUser`Config.SearchUserId`が招待されているか
- 新トップレベルフォルダ配下のフォルダに、古い検索用AppUserのコラボレーションがのこっていないか

<hr>

以下のコマンドはテストするために利用しました。 利用する必要は有りません。

##### `dotnet run test:create`

テスト用AppUserとフォルダを生成します

- `Test.NumberOfAppUsersForTest`の数だけAppUserを作成します。
- `Test.NumberOfFoldersForTest`の数だけ各AppUser配下にフォルダを作成します。
- `Test.PathToAppUserIDsForTest`のファイルに、作成したAppUserのIDのリストを書き出します。
  （これをConfig.AppUsersにはりつけるとテストを行えます）

##### `dotnet run test:delete`

生成したテスト用AppUserとフォルダを削除します

- `Test.PathToAppUserIDsForTest`のファイルにリストされたIDのAppUserをフォルダごと削除します。
- 注意: この操作を行うと、一旦削除対象ユーザーのフォルダをサービスアカウント配下に移動させたあとに削除します。 
  コンテンツを移動させると、移動元ユーザーのLogin名がフォルダに付くので、AppUser名に"ST_APP_USER"
  がついているものはすべて`test:create`によって作られたものだという前提をもち、この名前を含むフォルダをサービスアカウント配下から削除します。

##### `dotnet run test:find`

`dotnet run test:create` が何らかの理由で途中で失敗した場合、ファイルにIDが書き出されないので、AppUserの名前が`ST_APP_USER_`
から始まるものが新たに作成されたAppUserであるという前提のもと、IDを探しファイルへの書き出しを行います。



