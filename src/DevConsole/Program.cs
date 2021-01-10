using System;

namespace NTMiner
{
    internal unsafe class Program {
        private static volatile bool s_running = true;
        private static string s_poolIp;
        private static string s_keyword;
        private static bool s_ranOnce = false;

        // keyword=eth_submitLogin
        private static void Main(string[] args) {
            Console.CancelKeyPress += delegate { s_running = false; };

            if (args.Length >= 1) {
                if (args[0].StartsWith("keyword=")) {
                    s_keyword = args[0].Substring("keyword=".Length);
                }
                else {
                    s_poolIp = args[0];
                }
            }
            else {
                NTMinerConsole.UserError("ERROR: No poolIp argument was found.");
                NTMinerConsole.UserInfo("按任意键退出");
                Console.ReadKey();
                return;
            }
            if (args.Length >= 2) {
                Console.Title = args[1] + "开始时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff");
            }
            else {
                Console.Title = "开始时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff");
            }
        }
    }
}
