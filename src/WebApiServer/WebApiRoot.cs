﻿using Aliyun.OSS;
using LiteDB;
using NTMiner.Controllers;
using NTMiner.Core;
using NTMiner.Core.Impl;
using NTMiner.Core.Mq.MqMessagePaths;
using NTMiner.Core.Mq.Senders.Impl;
using NTMiner.Core.Redis;
using NTMiner.Core.Redis.Impl;
using NTMiner.Impl;
using NTMiner.ServerNode;
using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Web.Http;

namespace NTMiner {
    public static class WebApiRoot {
        private static readonly EventWaitHandle WaitHandle = new AutoResetEvent(false);
        public static OssClient OssClient { get; private set; }
        public static MediaTypeHeaderValue BinaryContentType { get; private set; } = new MediaTypeHeaderValue("image/jpg");

        private static Mutex _sMutexApp;
        // 该程序编译为控制台程序，如果不启用内网支持则默认设置为开机自动启动
        [STAThread]
        static void Main() {
            NTMinerConsole.DisbleQuickEditMode();
            try {
                bool mutexCreated;
                try {
                    // 锁名称上带上本节点的端口号，从而允许一个服务器上运行多个WebApiServer节点，这在软升级服务端程序时有用。
                    // 升级WebApiServer程序的时候步骤是：
                    // 1，在另一个端口启动新版本的程序；
                    // 2，让Widnows将来自旧端口的所有tcp请求转发到新端口；
                    // 3，退出旧版本的程序并更新到新版本；
                    // 4，删除第2步添加的Windows的端口转发；
                    // 5，退出第1步运行的节点；
                    // TODO:实现软升级策略
                    _sMutexApp = new Mutex(true, $"NTMinerServicesMutex{ServerRoot.HostConfig.GetServerPort().ToString()}", out mutexCreated);
                }
                catch {
                    mutexCreated = false;
                }
                if (mutexCreated) {
                    try {
                        // 用本节点的地址作为队列名，消费消息时根据路由键区分消息类型
                        string queue = $"{ServerAppType.WebApiServer.GetName()}.{ServerRoot.HostConfig.ThisServerAddress}";
                        string durableQueue = queue + MqKeyword.DurableQueueEndsWith;
                        AbstractMqMessagePath[] mqMessagePaths = new AbstractMqMessagePath[] {
                            new UserMqMessagePath(durableQueue),
                            new MinerClientMqMessagePath(queue)
                        };
                        if (!ServerConnection.Create(ServerAppType.WebApiServer, mqMessagePaths, out IServerConnection serverConfig)) {
                            NTMinerConsole.UserError("启动失败，无法继续，因为服务器上下文创建失败");
                            return;
                        }
                        Console.Title = $"{ServerAppType.WebApiServer.GetName()}_{ServerRoot.HostConfig.ThisServerAddress}";
                        OssClient = new OssClient(ServerRoot.HostConfig.OssEndpoint, ServerRoot.HostConfig.OssAccessKeyId, ServerRoot.HostConfig.OssAccessKeySecret);
                        var minerClientMqSender = new MinerClientMqSender(serverConfig);
                        var userMqSender = new UserMqSender(serverConfig);

                        var minerRedis = new MinerRedis(serverConfig);
                        var speedDataRedis = new SpeedDataRedis(serverConfig);
                        var userRedis = new UserRedis(serverConfig);
                        var captchaRedis = new CaptchaRedis(serverConfig);

                        WsServerNodeRedis = new WsServerNodeRedis(serverConfig);
                        WsServerNodeAddressSet = new WsServerNodeAddressSet(WsServerNodeRedis);
                        UserSet = new UserSet(userRedis, userMqSender);
                        UserAppSettingSet = new UserAppSettingSet();
                        CaptchaSet = new CaptchaSet(captchaRedis);
                        CalcConfigSet = new CalcConfigSet();
                        NTMinerWalletSet = new NTMinerWalletSet();
                        GpuNameSet = new GpuNameSet();
                        ClientDataSet clientDataSet = new ClientDataSet(minerRedis, speedDataRedis, minerClientMqSender);
                        ClientDataSet = clientDataSet;
                        CoinSnapshotSet = new CoinSnapshotSet(clientDataSet);
                        MineWorkSet = new UserMineWorkSet();
                        MinerGroupSet = new UserMinerGroupSet();
                        NTMinerFileSet = new NTMinerFileSet();
                        OverClockDataSet = new OverClockDataSet();
                        KernelOutputKeywordSet = new KernelOutputKeywordSet(SpecialPath.LocalDbFileFullName, isServer: true);
                        ServerMessageSet = new ServerMessageSet(SpecialPath.LocalDbFileFullName, isServer: true);
                        UpdateServerMessageTimestamp();
                        if (VirtualRoot.LocalAppSettingSet.TryGetAppSetting(nameof(KernelOutputKeywordTimestamp), out IAppSetting appSetting) && appSetting.Value is DateTime value) {
                            KernelOutputKeywordTimestamp = value;
                        }
                        else {
                            KernelOutputKeywordTimestamp = Timestamp.UnixBaseTime;
                        }
                    }
                    catch (Exception e) {
                        NTMinerConsole.UserError(e.Message);
                        NTMinerConsole.UserError(e.StackTrace);
                        NTMinerConsole.UserInfo("按任意键退出");
                        Console.ReadKey();
                        return;
                    }
                    VirtualRoot.StartTimer();
                    NTMinerRegistry.SetAutoBoot("NTMinerServices", true);
                    Type thisType = typeof(WebApiRoot);
                    Run();
                }
            }
            catch (Exception e) {
                Logger.ErrorDebugLine(e);
            }
        }

        private static bool _isClosed = false;
        private static void Close() {
            if (!_isClosed) {
                _isClosed = true;
                _sMutexApp?.Dispose();
            }
        }

        public static void Exit() {
            Close();
            Environment.Exit(0);
        }

        private static void Run() {
            try {
                string baseAddress = $"http://{NTKeyword.Localhost}:{ServerRoot.HostConfig.GetServerPort().ToString()}";
                HttpServer.Start(baseAddress, doConfig: config => {
                    // 向后兼容
                    config.Routes.MapHttpRoute("CalcConfigs", "api/ControlCenter/CalcConfigs", new {
                        controller = RpcRoot.GetControllerName<ICalcConfigController>(),
                        action = nameof(ICalcConfigController.CalcConfigs)
                    });
                });
                Windows.ConsoleHandler.Register(Close);
                WaitHandle.WaitOne();
                Close();
            }
            catch (Exception e) {
                Logger.ErrorDebugLine(e);
            }
            finally {
                Close();
            }
        }

        static WebApiRoot() {
        }
        public static IWsServerNodeRedis WsServerNodeRedis { get; private set; }

        public static DateTime StartedOn { get; private set; } = DateTime.Now;

        public static IUserAppSettingSet UserAppSettingSet { get; private set; }

        public static IUserSet UserSet { get; private set; }

        public static IWsServerNodeAddressSet WsServerNodeAddressSet { get; private set; }

        public static ICaptchaSet CaptchaSet { get; private set; }

        public static ICalcConfigSet CalcConfigSet { get; private set; }

        public static INTMinerWalletSet NTMinerWalletSet { get; private set; }

        public static IClientDataSet ClientDataSet { get; private set; }

        public static IGpuNameSet GpuNameSet { get; private set; }

        public static ICoinSnapshotSet CoinSnapshotSet { get; private set; }

        public static IUserMineWorkSet MineWorkSet { get; private set; }

        public static IUserMinerGroupSet MinerGroupSet { get; private set; }

        public static INTMinerFileSet NTMinerFileSet { get; private set; }

        public static IOverClockDataSet OverClockDataSet { get; private set; }

        public static IKernelOutputKeywordSet KernelOutputKeywordSet { get; private set; }

        public static IServerMessageSet ServerMessageSet { get; private set; }

        public static DateTime ServerMessageTimestamp { get; set; }

        public static DateTime KernelOutputKeywordTimestamp { get; private set; }

        public static LiteDatabase CreateLocalDb() {
            return new LiteDatabase($"filename={SpecialPath.LocalDbFileFullName}");
        }

        /// <summary>
        /// 因为客户端具有不同的ClientVersion，考虑到兼容性问题将来可能不同版本的客户端需要获取不同版本的server.json，所以
        /// 服务端用返回什么版本的server.json应由客户端自己决定，所以该方法有个jsonVersionKey入参，这个入参就是来自客户端。
        /// </summary>
        /// <param name="jsonVersionKey">server.json的版本</param>
        /// <returns></returns>
        public static ServerStateResponse GetServerStateResponse(string jsonVersionKey) {
            string jsonVersion = string.Empty;
            string minerClientVersion = string.Empty;
            try {
                var fileData = NTMinerFileSet.LatestMinerClientFile;
                minerClientVersion = fileData != null ? fileData.Version : string.Empty;
                if (!VirtualRoot.LocalAppSettingSet.TryGetAppSetting(jsonVersionKey, out IAppSetting data) || data.Value == null) {
                    jsonVersion = string.Empty;
                }
                else {
                    jsonVersion = data.Value.ToString();
                }
            }
            catch (Exception e) {
                Logger.ErrorDebugLine(e);
            }
            return new ServerStateResponse {
                JsonFileVersion = jsonVersion,
                MinerClientVersion = minerClientVersion,
                Time = Timestamp.GetTimestamp(),
                MessageTimestamp = Timestamp.GetTimestamp(ServerMessageTimestamp),
                OutputKeywordTimestamp = Timestamp.GetTimestamp(KernelOutputKeywordTimestamp),
                WsStatus = WsServerNodeAddressSet.WsStatus
            };
        }

        public static WebApiServerState GetServerState() {
            var ram = Windows.Ram.Instance;
            var cpu = Windows.Cpu.Instance;
            var t = WsServerNodeRedis.GetAllAsync();
            t.Wait();
            var wsServerNodes = t.Result;
            return new WebApiServerState {
                WsServerNodes = wsServerNodes,
                Address = ServerRoot.HostConfig.ThisServerAddress,
                Description = string.Empty,
                AvailablePhysicalMemory = ram.AvailablePhysicalMemory,
                TotalPhysicalMemory = ram.TotalPhysicalMemory,
                Cpu = cpu.ToData(),
                OSInfo = Windows.OS.Instance.OsInfo,
                CpuPerformance = cpu.GetTotalCpuUsage(),
                ProcessMemoryMb = VirtualRoot.ProcessMemoryMb
            };
        }

        public static void UpdateServerMessageTimestamp() {
            var first = ServerMessageSet.AsEnumerable().OrderByDescending(a => a.Timestamp).FirstOrDefault();
            if (first == null) {
                ServerMessageTimestamp = DateTime.MinValue;
            }
            else {
                ServerMessageTimestamp = first.Timestamp;
            }
        }

        public static void UpdateKernelOutputKeywordTimestamp(DateTime timestamp) {
            KernelOutputKeywordTimestamp = timestamp;
            VirtualRoot.Execute(new SetLocalAppSettingCommand(new AppSettingData {
                Key = nameof(KernelOutputKeywordTimestamp),
                Value = timestamp
            }));
        }
    }
}
