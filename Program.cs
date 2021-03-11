using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Box.V2.Config;

namespace script
{
    public static class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                var timer = Stopwatch.StartNew();
                if (args.Length != 1)
                {
                    PrintHelp();
                    return;
                }

                var command = args.First();

                switch (command)
                {
                    case "move":
                        Move.CreateRootFolderAndMoveAllFolders().Wait();
                        break;
                    case "check":
                        Check.CheckAppUsersAndFolders().Wait();
                        break;
                    case "test:create":
                        Test.CreateAppUsersAndFoldersAsync().Wait();
                        break;
                    case "test:delete":
                        Test.DeleteAppUsersAsync().Wait();
                        break;
                    case "test:find":
                        Test.FindAppUsersAsync().Wait();
                        break;
                    default:
                        Console.WriteLine("{0} という引数には対応していません", command);
                        PrintHelp();
                        break;
                }

                Console.WriteLine("実行完了  {0} ms", timer.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public static IBoxConfig ConfigureBoxApi()
        {
            IBoxConfig config = null;
            using (FileStream fs = new FileStream(Config.PathToConfigFile, FileMode.Open))
            {
                config = BoxConfig.CreateFromJsonFile(fs);
            }

            return config;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("使い方");
            Console.WriteLine("事前準備：Config.csの設定を行ってください。");
            Console.WriteLine("dotnet run move : 親フォルダを作り、トップレベルのフォルダ移動します。検索用AppUserのコラボレーションも削除します。");
        }
    }
}