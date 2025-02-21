﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Timers;

namespace NTMiner {
    public static partial class VirtualRoot {
        // 因为多个受保护区域中可能会互相访问，用一把锁可以避免死锁。不用多把锁是因为没有精力去检查每一个受保护区域确保它们不会互相访问导致死锁。
        private static readonly object _locker = new object();
        public static readonly string AppFileFullName = Process.GetCurrentProcess().MainModule.FileName;
        public static readonly string ProcessName = Process.GetCurrentProcess().ProcessName;
        private static PerformanceCounter _performanceCounter = null;
        private static bool _performanceCounterError = false;
        private static PerformanceCounter PerformanceCounter {
            get {
                if (_performanceCounterError) {
                    return null;
                }
                if (_performanceCounter == null) {
                    lock (_locker) {
                        if (_performanceCounter == null) {
                            try {
                                _performanceCounter = new PerformanceCounter("Process", "Working Set - Private", ProcessName);
                            }
                            catch (Exception e) {
                                _performanceCounterError = true;
                                Logger.ErrorDebugLine(e);
                            }
                        }
                    }
                }
                return _performanceCounter;
            }
        }

        public static double ProcessMemoryMb {
            get {
                if (PerformanceCounter == null) {
                    return 0.0;
                }
                return PerformanceCounter.RawValue / NTKeyword.DoubleM;
            }
        }
        /// <summary>
        /// 是否是Win10或更新版本的windows
        /// </summary>
        public static readonly bool IsGEWin10 = Environment.OSVersion.Version >= new Version(6, 2);
        /// <summary>
        /// 是否是比Win10更旧版本的windows
        /// </summary>
        public static readonly bool IsLTWin10 = !IsGEWin10;

        private static string _cpuId = null;
        public static string CpuId {
            get {
                if (_cpuId == null) {
                    try {
                        using (ManagementClass mc = new ManagementClass("Win32_Processor"))
                        using (ManagementObjectCollection moc = mc.GetInstances()) {
                            foreach (ManagementObject mo in moc) {
                                _cpuId = mo.Properties["ProcessorId"].Value.ToString();
                                break;
                            }
                        }
                    }
                    catch {
                    }
                    if (_cpuId == null) {
                        _cpuId = "unknow";
                    }
                }
                return _cpuId;
            }
        }

        public static readonly SessionEndingEventHandler SessionEndingEventHandler = (sender, e) => {
            OsSessionEndingEvent.ReasonSessionEnding reason;
            switch (e.Reason) {
                case SessionEndReasons.Logoff:
                    reason = OsSessionEndingEvent.ReasonSessionEnding.Logoff;
                    break;
                case SessionEndReasons.SystemShutdown:
                    reason = OsSessionEndingEvent.ReasonSessionEnding.Shutdown;
                    break;
                default:
                    reason = OsSessionEndingEvent.ReasonSessionEnding.Unknown;
                    break;
            }
            RaiseEvent(new OsSessionEndingEvent(reason));
        };

        public static Task Delay(this TimeSpan timeSpan) {
            var tcs = new TaskCompletionSource<object>();
            var timer = new Timer(timeSpan.TotalMilliseconds) { AutoReset = false };
            timer.Elapsed += (sender, e) => {
                timer.Stop();
                timer.Dispose();
                tcs.SetResult(null);
            };
            timer.Start();
            return tcs.Task;
        }

        public static Task MillisecondsDelay(this int n) {
            var tcs = new TaskCompletionSource<object>();
            var timer = new Timer(n) { AutoReset = false };
            timer.Elapsed += (sender, e) => {
                timer.Stop();
                timer.Dispose();
                tcs.SetResult(null);
            };
            timer.Start();
            return tcs.Task;
        }

        public static Task SecondsDelay(this int n) {
            var tcs = new TaskCompletionSource<object>();
            BuildViaTimesLimitPath<Per1SecondEvent>("倒计时", LogEnum.None, message => {
                n--;
                if (n == 0) {
                    tcs.SetResult(null);
                }
            }, viaTimesLimit: n, AnonymousMessagePath.Location);
            return tcs.Task;
        }

        /// <summary>
        /// 如果是在比如Wpf的界面线程中调用该方法，注意用UIThread回调
        /// </summary>
        public static void SetInterval(TimeSpan per, Action perCallback, Action stopCallback, TimeSpan timeout, Func<bool> requestStop) {
            var timer = new Timer(per.TotalMilliseconds) { AutoReset = true };
            double milliseconds = 0;
            timer.Elapsed += (sender, e) => {
                milliseconds += per.TotalMilliseconds;
                perCallback?.Invoke();
                if (milliseconds >= timeout.TotalMilliseconds || (requestStop != null && requestStop.Invoke())) {
                    timer.Stop();
                    timer.Dispose();
                    stopCallback?.Invoke();
                }
            };
            timer.Start();
        }

        // 登录名中的保留字
        private static readonly HashSet<string> _reservedLoginNameWords = new HashSet<string> {
            "ntminer",
            "bitzero"
        };
        public static bool IsValidLoginName(string loginName, out string message) {
            message = string.Empty;
            if (string.IsNullOrEmpty(loginName)) {
                message = "登录名不能为空";
                return false;
            }
            foreach (var word in _reservedLoginNameWords) {
                if (loginName.IndexOf(word, StringComparison.OrdinalIgnoreCase) != -1) {
                    message = "登录名中不能包含保留字";
                    return false;
                }
            }
            if (loginName.IndexOf(' ') != -1) {
                message = "登录名中不能包含空格";
                return false;
            }
            if (loginName.IndexOf('@') != -1) {
                message = "登录名中不能包含@符号";
                return false;
            }
            if (loginName.Length == 11 && loginName.All(a => char.IsDigit(a))) {
                message = "登录名不能是11位的纯数字，提示：请添加非数字字符以和手机号码区分";
                return false;
            }
            return true;
        }
    }
}
