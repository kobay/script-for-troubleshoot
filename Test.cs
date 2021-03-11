using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Box.V2;
using Box.V2.JWTAuth;
using Box.V2.Models;
using ComposableAsync;
using RateLimiter;

namespace script
{
    public static class Test
    {
        // TEST用に作成するAppUserの数
        private const int NumberOfAppUsersForTest = 5;

        // TEST用に作成するフォルダの数
        private const int NumberOfFoldersForTest = 10;

        // TEST用に作成したAppUserのIDを保存しておくファイル
        private const string PathToAppUserIDsForTest = @"testappusers.json";
        
        public static async Task CreateAppUsersAndFoldersAsync()
        {
            Console.WriteLine("CreateAppUsersAndFoldersAsync　Start");

            // BOX API実行用のClientを準備
            var config = Program.ConfigureBoxApi();
            var boxJwt = new BoxJWTAuth(config);
            var adminToken = boxJwt.AdminToken();
            var saClient = boxJwt.AdminClient(adminToken);

            // サービスアカウントが取れているかチェック
            var sa = await saClient.UsersManager.GetCurrentUserInformationAsync();
            Console.WriteLine("SA user ID:{0}, Login:{1}, Name:{2}", sa.Id, sa.Login, sa.Name);

            // RateLimit対応スロットリング
            var throttle = TimeLimiter.GetFromMaxCountByInterval(Config.ApiRateLimit, TimeSpan.FromSeconds(1));

            // test用のappUserを作成する。
            var userTasks = new Task<BoxUser>[NumberOfAppUsersForTest];
            for (var i = 0; i < NumberOfAppUsersForTest; i++)
            {
                var num = i;
                userTasks[i] = Task.Run(() => CreateAppUserAsync(num, saClient, throttle));
            }

            // 待機
            await Task.WhenAll(userTasks);
            Console.WriteLine("finished app user creation");

            // 検索用AppUserを作成
            var searchUser = await CreateSearchUser(saClient, throttle);
            var searchUserId = searchUser.Id;
            // var searchUserId = "15473262228";
            Console.WriteLine("created searchUser ID:{0}", searchUserId);

            for (int i = 0; i < NumberOfAppUsersForTest; i++)
            {
                var appUser = userTasks[i].Result;
                var currAppUser = i + 1;

                var auClient = new BoxClient(saClient.Config, saClient.Auth, asUser: appUser.Id);
                BoxFile sampleFile = null;
                await using (var fileStream = new FileStream(@"Sample.docx", FileMode.Open))
                {
                    BoxFileRequest requestParams = new BoxFileRequest()
                    {
                        Name = $"Sample-for-{appUser.Id}.docx",
                        Parent = new BoxRequestEntity() {Id = "0"}
                    };

                    sampleFile = await auClient.FilesManager.UploadAsync(requestParams, fileStream);
                    Console.WriteLine($"uploaded {sampleFile.Name} {currAppUser}/{NumberOfAppUsersForTest}");
                }

                // ユーザー毎にフォルダを作成
                var folderTasks = new List<Task>();

                Console.WriteLine($"creating folders for {currAppUser}/{NumberOfAppUsersForTest}");
                for (var j = 0; j < NumberOfFoldersForTest; j++)
                {
                    var currFolder = j + 1;
                    folderTasks.Add(Task.Run(async () =>
                    {
                        // フォルダを作成する
                        var folder = await CreateFolderAsync(auClient, appUser, currFolder, throttle);

                        // 検索ユーザーを招待する
                        await CreateCollaboration(folder, searchUserId, auClient, throttle);

                        // サンプルファイルをコピーする
                        if (sampleFile != null)
                        {
                            var requestParams = new BoxFileRequest()
                            {
                                Id = sampleFile.Id,
                                Parent = new BoxRequestEntity()
                                {
                                    Id = folder.Id
                                }
                            };

                            await throttle;
                            await auClient.FilesManager.CopyAsync(requestParams);
                        }

                        Console.WriteLine(
                            $"appUser {currAppUser}/{NumberOfAppUsersForTest}, Folder {currFolder}/{NumberOfFoldersForTest}");
                    }));

                    // 少しずつ待機
                    // if ()
                }

                // フォルダ作成を待機
                await Task.WhenAll(folderTasks);
            }


            // 削除できるように、appUserのIDをファイルに残す
            var toBeDeletedIds = userTasks.Select(t => t.Result.Id).ToList();
            // 検索用ユーザーも削除対象に追加しておく
            toBeDeletedIds.Add(searchUserId);
            // ファイルに書き出す
            await File.WriteAllTextAsync(PathToAppUserIDsForTest, JsonSerializer.Serialize(toBeDeletedIds));

            Console.WriteLine("CreateAppUsersAndFoldersAsync　Done");
        }

        private static async Task CreateCollaboration(BoxEntity folder, string searchUserId, BoxClient auClient,
            TimeLimiter throttle)
        {
            var requestParams = new BoxCollaborationRequest()
            {
                Item = new BoxRequestEntity()
                {
                    Type = BoxType.folder,
                    Id = folder.Id
                },
                Role = Config.SearchUserRole,
                AccessibleBy = new BoxCollaborationUserRequest()
                {
                    Id = searchUserId
                }
            };
            await throttle;
            await auClient.CollaborationsManager.AddCollaborationAsync(requestParams);
        }

        private static async Task<BoxUser> CreateSearchUser(BoxClient saClient, TimeLimiter throttle)
        {
            // 検索ユーザーを作成
            var userParams = new BoxUserRequest()
            {
                Name = "ST_APP_USER_SEARCH",
                IsPlatformAccessOnly = true
            };

            await throttle;
            var searchUser = await saClient.UsersManager.CreateEnterpriseUserAsync(userParams);
            return searchUser;
        }

        private static async Task<BoxUser> CreateAppUserAsync(int number, BoxClient saClient, TimeLimiter throttle)
        {
            // Console.WriteLine("execute API to create appUser No.{0}", number);

            var userParams = new BoxUserRequest()
            {
                Name = "ST_APP_USER_" + number,
                IsPlatformAccessOnly = true
            };
            await throttle;
            var newUser = await saClient.UsersManager.CreateEnterpriseUserAsync(userParams);
            return newUser;
        }

        private static async Task<BoxFolder> CreateFolderAsync(BoxClient auClient, BoxUser appUser, int num,
            TimeLimiter throttle)
        {
            var folderParams = new BoxFolderRequest()
            {
                Name = "TestFolder" + num,
                Parent = new BoxRequestEntity()
                {
                    Id = "0"
                }
            };
            await throttle;
            var folder = await auClient.FoldersManager.CreateAsync(folderParams);
            return folder;
        }

        public static async Task DeleteAppUsersAsync()
        {
            Console.WriteLine("DeleteAppUsersAsync　Start");

            var config = Program.ConfigureBoxApi();
            var boxJwt = new BoxJWTAuth(config);
            var adminToken = boxJwt.AdminToken();
            var saClient = boxJwt.AdminClient(adminToken);

            // サービスアカウントが取れているかチェック
            var sa = await saClient.UsersManager.GetCurrentUserInformationAsync();
            Console.WriteLine("SA user ID:{0}, Login:{1}, Name:{2}", sa.Id, sa.Login, sa.Name);

            // RateLimit対応スロットリング
            var throttle = TimeLimiter.GetFromMaxCountByInterval(Config.ApiRateLimit, TimeSpan.FromSeconds(1));
            
            // ファイルを読み込む
            if (!File.Exists(PathToAppUserIDsForTest))
            {
                Console.WriteLine(PathToAppUserIDsForTest + " が存在しません。");
            }

            var fileData = await File.ReadAllTextAsync(PathToAppUserIDsForTest, Encoding.UTF8);
            var appUserIds = JsonSerializer.Deserialize<string[]>(fileData);


            // AppUserを削除
            if (appUserIds != null)
            {
                var tasks = new Task[appUserIds.Length];

                var totalAppUser = appUserIds.Length;

                for (int i = 0; i < totalAppUser; i++)
                {
                    var appUserId = appUserIds[i];
                    var num = i + 1;
                    tasks[i] = Task.Run(() =>
                        DeleteAppUserAsync(appUserId, saClient, sa.Id, num, totalAppUser, throttle));
                }

                // 待機
                await Task.WhenAll(tasks);
            }
            
            // サービスアカウントの退避フォルダをクリア
            {
                var offset = 0;
                var totalCount = 0;
                do
                {
                    var items = await saClient.FoldersManager.GetFolderItemsAsync("0", 1000);
                    totalCount = items.TotalCount;
                    offset += 1000;

                    List<Task> tasks = new List<Task>();
                    foreach (var entry in items.Entries)
                    {
                        if (entry.Type != "folder" || !entry.Name.Contains("ST_APP_USER")) continue;
                        var task = Task.Run(async () =>
                        {
                            await throttle;
                            Console.WriteLine($"deleting {entry.Name}");
                            await saClient.FoldersManager.DeleteAsync(entry.Id, recursive: true);
                        });

                        tasks.Add(task);
                    }

                    await Task.WhenAll(tasks);
                } while (totalCount > offset);
            }

            Console.WriteLine("DeleteAppUsersAsync　Done");
        }

        private static async Task DeleteAppUserAsync(string userId, BoxClient saClient, string saUserId,
            int num, int totalAppUser, TimeLimiter throttle)
        {
            // コンテンツの移動は本来不要なはずだが、なぜかGovernanceのエラーが出てしまうので実行。
            await throttle;
            Console.WriteLine($"start appuser:{userId}  {num}/{totalAppUser}");
            await saClient.UsersManager.MoveUserFolderAsync(userId, saUserId);

            Console.WriteLine($"moved contents appuser:{userId}  {num}/{totalAppUser}");
            // ユーザーの強制削除
            await throttle;
            await saClient.UsersManager.DeleteEnterpriseUserAsync(userId, notify: false, force: true);

            Console.WriteLine($"deleted appuser:{userId}  {num}/{totalAppUser}");
        }

        public static async Task FindAppUsersAsync()
        {
            Console.WriteLine("FindAppUsersAsync　Start");

            // BOX API実行用のClientを準備
            var config = Program.ConfigureBoxApi();
            var boxJwt = new BoxJWTAuth(config);
            var adminToken = boxJwt.AdminToken();
            var saClient = boxJwt.AdminClient(adminToken);

            // サービスアカウントが取れているかチェック
            var sa = await saClient.UsersManager.GetCurrentUserInformationAsync();
            Console.WriteLine("SA user ID:{0}, Login:{1}, Name:{2}", sa.Id, sa.Login, sa.Name);

            // 非同期処理の同時実行数を制限
            var throttling = new SemaphoreSlim(Config.ApiRateLimit);


            var users = await saClient.UsersManager.GetEnterpriseUsersAsync(limit: 1000);

            var appUserList = new List<string>();
            var listSize = users.Entries.Count;
            for (int i = 0; i < listSize; i++)
            {
                Console.Write("user {0}/{1}", i, listSize);
                Console.SetCursorPosition(0, Console.CursorTop);

                var user = users.Entries[i];

                if (user.Name.StartsWith("ST_APP_USER_") && user.Login.StartsWith("AppUser_"))
                {
                    if (user.Name == "ST_APP_USER_SEARCH")
                    {
                        Console.WriteLine("Search appUser ID: {0} ", user.Id);
                    }

                    appUserList.Add(user.Id);
                }
            }

            Console.WriteLine($"Found AppUser {appUserList.Count}");

            // ファイルに書き出す
            await File.WriteAllTextAsync(PathToAppUserIDsForTest, JsonSerializer.Serialize(appUserList));

            Console.WriteLine("FindAppUsersAsync　Done");
        }
    }
}