using Box.V2.Models;

namespace script
{
    public static class Config
    {
        // BOXアプリ設定からダウンロードしたJWT認証用のConfigファイルを指定してください。
        public const string PathToConfigFile = "COMMON_324763110_u41fwt2k_config.json";

        // 並列で実行するスレッド数 (Box API Rate Limit 16.66/sec/user )
        public const int ApiRateLimit = 16;

        // トップレベルのフォルダ名
        public const string TopFolderName = "HOUSE_LIST";

        // 検索用 AppUserID
        public const string SearchUserId = "15483772961";

        // 検索用 AppUserを招待する時の権限
        public const string SearchUserRole = BoxCollaborationRoles.Editor;
        // public const string SearchUserRole = BoxCollaborationRoles.Viewer;

        // 邸情報を所有するAppUserのIDリスト
        public static readonly string[] AppUsers = new[]
        {
            "15483972741","15483743798","15483854330","15483584669","15483859476"
        };


    }
}