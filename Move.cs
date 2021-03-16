using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Box.V2;
using Box.V2.Exceptions;
using Box.V2.JWTAuth;
using Box.V2.Models;
using ComposableAsync;
using RateLimiter;

namespace script
{
    public static class Move
    {
        public static async Task CreateRootFolderAndMoveAllFolders()
        {
            Console.WriteLine("CreateRootFolderAndMoveAllFolders　Start");

            // サービスアカウントのClientを準備
            var config = Program.ConfigureBoxApi();
            var boxJwt = new BoxJWTAuth(config);
            var adminToken = boxJwt.AdminToken();
            var saClient = boxJwt.AdminClient(adminToken);

            // サービスアカウントが取れているかチェック
            var sa = await saClient.UsersManager.GetCurrentUserInformationAsync();
            Console.WriteLine("SA user ID:{0}, Login:{1}, Name:{2}", sa.Id, sa.Login, sa.Name);
            
            // RateLimit対応スロットリング
            var throttle = TimeLimiter.GetFromMaxCountByInterval(Config.ApiRateLimit, TimeSpan.FromSeconds(1));

            // AppUser毎の処理
            var totalAppUsers = Config.AppUsers.Length;
            for (var i = 0; i < totalAppUsers; i++)
            {
                var appUserCounter = i + 1;
                var appUserId = Config.AppUsers[i];

                Console.WriteLine($"Start AppUser:{appUserId} {appUserCounter}/{totalAppUsers}");

                // 検索用ユーザーだった場合は何もしない
                if (appUserId == Config.SearchUserId)
                {
                    Console.WriteLine($"Skip. AppUser:{appUserId} is Search User");
                    continue;
                }
                
                // AppUser専用のClientを用意
                var auClient = new BoxClient(saClient.Config, saClient.Auth, asUser: appUserId);

                // タスクの待ち合わせ用リスト
                var tasks = new List<Task>();

                // トップレベルに親フォルダを作成する（作成済みの場合はそれを利用する）
                var topFolder = await EnsureTopFolder(auClient, throttle);

                // 親フォルダに検索用AppUserのコラボレーションを作成する（作成済みの場合は無視する）
                await CreateSearchUserCollaboration(topFolder, auClient, throttle);

                // コンソール表示時の排他用オブジェクト
                var forLock = new object();
                // プロセスが終わったフォルダの数をカウント
                var folderCount = 0;
                
                // 1ユーザーのフォルダリストを処理
                var offset = 0;
                var totalCount = 0;
                do
                {
                    // AppUserのトップレベルのフォルダを一覧 基本的に1回で済む想定(約480 folder)だが、念の為1000件づつすべてのフォルダを処理する
                    var folderItems = await GetTopLevelFolderItems(auClient, offset, throttle);

                    if (totalCount == 0)
                    {
                        totalCount = folderItems.TotalCount;
                    }

                    offset += 1000;

                    // 取得したトップレベルのアイテムをループする
                    var totalFolders = folderItems.Entries.Count;
                    for (var j = 0; j < totalFolders; j++)
                    {
                        // アイテムを取り出す
                        var item = folderItems.Entries[j];

                        // フォルダではないか、フォルダが移動先の場合は無視
                        if (item.Type != "folder" || item.Id == topFolder.Id)
                        {
                            continue;
                        }

                        // 以下の処理を非同期で行う。
                        var task = Task.Run(async () =>
                        {
                            // 検索ユーザーをコラボレーションから外す
                            await RemoveSearchUserCollaboration(auClient, item, throttle);

                            // フォルダを更新し、親フォルダを変更する
                            await MoveFolder(item, topFolder, auClient, throttle);


                            // コンソールの表示が非同期でされないように順番待ちをする
                            lock (forLock)
                            {
                                folderCount += 1;
                                // 進捗の表示
                                Console.Write("AppUser {0}/{1}, Folder {2}/{3}",
                                    appUserCounter, totalAppUsers, folderCount, totalFolders);
                                Console.SetCursorPosition(0, Console.CursorTop);
                            }
                        });

                        tasks.Add(task);
                    }
                } while (totalCount > offset);
                
                // 非同期処理の待機
                await Task.WhenAll(tasks);
                Console.WriteLine();
                
            } // end for appUsers
            
            Console.WriteLine("CreateRootFolderAndMoveAllFolders　Done");
        }

        private static async Task<BoxFolder> EnsureTopFolder(BoxClient auClient, TimeLimiter throttle)
        {
            BoxFolder topFolder;
            try
            {
                // トップレベルに親フォルダを作成
                var folderParams = new BoxFolderRequest()
                {
                    Name = Config.TopFolderName,
                    Parent = new BoxRequestEntity()
                    {
                        Id = "0"
                    }
                };
                await throttle;
                topFolder = await auClient.FoldersManager.CreateAsync(folderParams);
            }
            catch (BoxConflictException<BoxFolder> bce)
            {
                // スクリプトを一度実行して、既にトップレベルフォルダが存在している
                topFolder = bce.ConflictingItems.First();
                Console.WriteLine($"{topFolder.Name} already exists");
            }

            return topFolder;
        }

        private static async Task<BoxCollection<BoxItem>> GetTopLevelFolderItems(BoxClient auClient, int offset,
            TimeLimiter throttle)
        {
            await throttle;
            var folderItems = await auClient.FoldersManager.GetFolderItemsAsync("0", 1000, offset: offset);
            return folderItems;
        }

        private static async Task MoveFolder(BoxItem item, BoxFolder topFolder, BoxClient auClient,
            TimeLimiter throttle)
        {
            var folderRequest = new BoxFolderRequest()
            {
                Id = item.Id,
                Parent = new BoxRequestEntity()
                {
                    Id = topFolder.Id
                }
            };
            await throttle;
            await auClient.FoldersManager.UpdateInformationAsync(folderRequest);
        }

        private static async Task RemoveSearchUserCollaboration(BoxClient auClient, BoxItem item, TimeLimiter throttle)
        {
            // フォルダのコラボレーションを一覧
            await throttle;
            var collabs = await auClient.FoldersManager
                .GetCollaborationsAsync(item.Id);

            var collab = collabs.Entries.SingleOrDefault(c => c.AccessibleBy.Id == Config.SearchUserId);
            if (collab != null)
            {
                // 検索用ユーザーのコラボレーションを外す
                await throttle;
                await auClient.CollaborationsManager.RemoveCollaborationAsync(id: collab.Id);
            }
        }

        private static async Task CreateSearchUserCollaboration(BoxFolder topFolder, BoxClient auClient,
            TimeLimiter throttle)
        {
            var requestParams = new BoxCollaborationRequest()
            {
                Item = new BoxRequestEntity()
                {
                    Type = BoxType.folder,
                    Id = topFolder.Id
                },
                Role = Config.SearchUserRole,
                AccessibleBy = new BoxCollaborationUserRequest()
                {
                    Id = Config.SearchUserId
                }
            };
            try
            {
                await throttle;
                await auClient.CollaborationsManager.AddCollaborationAsync(requestParams);
            }
            catch (BoxException be)
            {
                if (be.Message.Contains("user_already_collaborator"))
                {
                    // ignore
                    Console.WriteLine("コラボレーション設定済み");
                }
                else
                {
                    throw;
                }
            }
        }
    }
}