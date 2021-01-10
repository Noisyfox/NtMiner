using System;
using System.Collections.Generic;
using System.Management;

namespace NTMiner.Windows {
    public class WMI {
        public static bool IsWmiEnabled {
            get {
                return true;
            }
        }

        /// <summary>
        /// 获取给定进程的完整命令行参数
        /// </summary>
        /// <param name="processName">可带.exe后缀也可不带，不带时方法内部会自动补上</param>
        /// <returns></returns>
        public static List<string> GetCommandLines(string processName) {
            if (!IsWmiEnabled) {
                return new List<string>();
            }
            List<string> results = new List<string>();
            if (string.IsNullOrEmpty(processName)) {
                return results;
            }
            if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                processName += ".exe";
            }
            string wmiQuery = $"select CommandLine from Win32_Process where Name='{processName}'";
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(wmiQuery))
            using (ManagementObjectCollection retObjectCollection = searcher.Get()) {
                foreach (ManagementObject retObject in retObjectCollection) {
                    results.Add((string)retObject["CommandLine"]);
                }
            }

            return results;
        }
    }
}
