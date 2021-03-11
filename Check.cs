using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Box.V2;
using Box.V2.JWTAuth;
using Box.V2.Models;
using ComposableAsync;
using RateLimiter;

namespace script
{
    public static class Check
    {
        public static async Task CheckAppUsersAndFolders()
        {
            Console.WriteLine("CheckAppUsersAndFolders　Start");

            // サービスアカウントのClientを準備
            var config = Program.ConfigureBoxApi();
            var boxJwt = new BoxJWTAuth(config);
            var adminToken = boxJwt.AdminToken();
            var saClient = boxJwt.AdminClient(adminToken);

            // サービスアカウントが取れているかチェック
            var sa = await saClient.UsersManager.GetCurrentUserInformationAsync();
            Console.WriteLine("SA user ID:{0}, Login:{1}, Name:{2}", sa.Id, sa.Login, sa.Name);
            
            // AppUser毎の処理
            var totalAppUsers = Config.AppUsers.Length;

            for (var i = 0; i < totalAppUsers; i++)
            {
                var appUserCounter = i + 1;
                var appUserId = Config.AppUsers[i];

                Console.WriteLine($"AppUser:{appUserId} {appUserCounter}/{totalAppUsers} Start");

                // 検索用ユーザーだった場合は何もしない
                if (appUserId == Config.SearchUserId)
                {
                    Console.WriteLine($"Skip. AppUser:{appUserId} is Search User");
                    continue;
                }

                // RateLimit対応スロットリング
                var throttle = TimeLimiter.GetFromMaxCountByInterval(Config.ApiRateLimit, TimeSpan.FromSeconds(1));


                // AppUser専用のClientを用意
                var auClient = new BoxClient(saClient.Config, saClient.Auth, asUser: appUserId);


                // ユーザーが所有するトップレベルのフォルダのリストを取得
                var topFolders = await GetFolders(auClient, "0", 100, throttle);

                // フォルダが1つだけでない、または、フォルダ名が指定と異なる場合
                if (topFolders.Count != 1 || topFolders.First().Name != Config.TopFolderName)
                {
                    Console.WriteLine(
                        $"ERROR トップフォルダが間違っている。 appUserId = {appUserId}, founder count = {topFolders.Count}, folder name = {topFolders.First().Name}");
                    return;
                }
                

                // トップフォルダのフォルダチェック
                BoxFolder topFolder = (BoxFolder) topFolders.First();

                // トップフォルダに検索ユーザーが招待されているか
                // フォルダのコラボレーションを一覧
                await throttle;
                var topCollabs = await auClient.FoldersManager
                    .GetCollaborationsAsync(topFolder.Id);

                var topCollab = topCollabs.Entries.SingleOrDefault(c => c.AccessibleBy.Id == Config.SearchUserId);
                if (topCollab == null)
                {
                    Console.WriteLine($"ERROR トップフォルダに検索ユーザーが招待されていない。appUserId = {appUserId}");
                    return;
                }

                // トップフォルダ下のサブフォルダをチェック
                var subFolders = await GetFolders(auClient, topFolder.Id, 1000, throttle);
                Console.WriteLine($"AppUser:{appUserId} {appUserCounter}/{totalAppUsers} number of subfolder = {subFolders.Count}");
                
                // タスクの待ち合わせ用リスト
                var folderTasks = new List<Task>();

                var forLock = new object();
                var checkCount = 0;
                // サブフォルダに検索ユーザーがついていないこと
                foreach (var subItem in subFolders)
                {
                    var task = Task.Run(async () =>
                    {
                        BoxFolder subFolder = (BoxFolder) subItem;
                        // フォルダのコラボレーションを一覧
                        await throttle;
                        var subCollabs = await auClient.FoldersManager
                            .GetCollaborationsAsync(subFolder.Id);

                        var subCollabListForSearch =
                            subCollabs.Entries.Where(c => c.AccessibleBy.Id == Config.SearchUserId);

                        // サブフォルダについている、検索用ユーザーのコラボレーションが1つだけかチェック
                        if (subCollabListForSearch.Count() != 1)
                        {
                            Console.WriteLine(
                                $"ERROR サブフォルダにコラボレーションが残っている。appUserId = {appUserId} subFolderId = {subFolder.Id} subFolderName = {subFolder.Name}");
                            return;
                        }

                        // サブフォルダについている唯一のコラボレーションが、トップフォルダについているコラボレーションと同じものか
                        var subCollabForSearch = subCollabListForSearch.First();

                        if (subCollabForSearch.Id != topCollab.Id)
                        {
                            // トップフォルダとサブフォルダで異なるコラボレーションがついている
                            Console.WriteLine(
                                $"ERROR サブフォルダとトップフォルダのコラボレーションが異なる。appUserId = {appUserId} subFolderId = {subFolder.Id} subFolderName = {subFolder.Name}");
                        }

                        lock (forLock)
                        {
                            checkCount++;
                            Console.Write("folder checked {0}", checkCount);
                            Console.SetCursorPosition(0, Console.CursorTop);
                        }
                    }); // end Task.Run
                    folderTasks.Add(task);
                } // end foreach on sub folders

                await Task.WhenAll(folderTasks);
                Console.WriteLine($"AppUser:{appUserId} {appUserCounter}/{totalAppUsers} folder check done");
            } // end for on appUsers

            Console.WriteLine("CheckAppUsersAndFolders　Done");
        }

        private static async Task<List<BoxItem>> GetFolders(BoxClient auClient, string folderId, int limit,
            TimeLimiter throttle)
        {
            await throttle;
            var topFolderItems = await auClient.FoldersManager.GetFolderItemsAsync(folderId, limit);

            // フォルダだけをあつめる
            var folders = new List<BoxItem>();
            foreach (var item in topFolderItems.Entries)
            {
                if (item.Type == "folder")
                {
                    folders.Add(item);
                }
            }

            return folders;
        }
    }
}