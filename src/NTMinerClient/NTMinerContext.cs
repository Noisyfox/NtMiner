﻿using Microsoft.Win32;
using NTMiner.Core;
using NTMiner.Core.Impl;
using NTMiner.Core.Kernels;
using NTMiner.Core.Profile;
using NTMiner.Core.Profiles;
using NTMiner.Core.Profiles.Impl;
using NTMiner.Cpus;
using NTMiner.Cpus.Impl;
using NTMiner.Gpus;
using NTMiner.Gpus.Impl;
using NTMiner.Impl;
using NTMiner.Mine;
using NTMiner.Report;
using NTMiner.ServerNode;
using NTMiner.Windows;
using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace NTMiner {
    public partial class NTMinerContext : INTMinerContext {
        // 因为多个受保护区域中可能会互相访问，用一把锁可以避免死锁。不用多把锁是因为没有精力去检查每一个受保护区域确保它们不会互相访问导致死锁。
        private static readonly object _locker = new object();
        public DateTime CreatedOn { get; private set; }

        private NTMinerContext() {
            CreatedOn = DateTime.Now;
            LocalMessageSet = new LocalMessageSet();
            if (ClientAppType.IsMinerClient) {
                SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            }
            VirtualRoot.BuildEventPath<AppExitEvent>($"程序退出时的{nameof(NTMinerContext)}退出逻辑", LogEnum.None,
                message => {
                    if (LockedMineContext != null) {
                        StopMine(StopMineReason.ApplicationExit);
                    }
                    SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
                }, typeof(NTMinerContext));
        }

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e) {
            VirtualRoot.ThisLocalInfo(nameof(NTMinerContext), $"Windows 会话切换, 因为{((WindowsSessionSwitchReason)e.Reason).GetDescription()}");
        }

        #region Init
        public void Init(Action callback) {
            Task.Factory.StartNew(() => {
                bool isSelfWork = Environment.GetCommandLineArgs().Contains("--selfWork", StringComparer.OrdinalIgnoreCase);
                bool isWork = isSelfWork || Environment.GetCommandLineArgs().Contains("--work", StringComparer.OrdinalIgnoreCase);
                _workType = isSelfWork ? WorkType.SelfWork : (isWork ? WorkType.MineWork : WorkType.None);
                if (ClientAppType.IsMinerClient) {
                    NTMinerRegistry.SetWorkType(_workType);
                }
                if (isWork) {
                    DoInit(callback);
                }
                else {
                    // 如果是Debug模式且不是群控客户端则使用本地数据库初始化
                    bool useLocalDb = DevMode.IsDevMode && !ClientAppType.IsMinerStudio;
                    if (useLocalDb) {
                        DoInit(callback);
                    }
                    else {
                        Logger.InfoDebugLine(nameof(RpcRoot.OSSService.AliyunOSSService.GetAliyunServerJson));
                        RpcRoot.OSSService.AliyunOSSService.GetAliyunServerJson((data) => {
                            // 如果server.json未下载成功则不覆写本地server.json
                            if (data != null && data.Length != 0) {
                                Logger.InfoDebugLine($"{nameof(RpcRoot.OSSService.AliyunOSSService.GetAliyunServerJson)} ok");
                                var serverJson = Encoding.UTF8.GetString(data);
                                if (!string.IsNullOrEmpty(serverJson)) {
                                    HomePath.WriteServerJsonFile(serverJson);
                                }
                                RpcRoot.OfficialServer.AppSettingService.GetJsonFileVersionAsync(HomePath.ExportServerJsonFileName, serverState => {
                                    SetServerJsonVersion(serverState.JsonFileVersion);
                                    AppVersionChangedEvent.PublishIfNewVersion(serverState.MinerClientVersion);
                                    if (Math.Abs((long)Timestamp.GetTimestamp() - (long)serverState.Time) >= Timestamp.DesyncSeconds) {
                                        NTMinerConsole.UserWarn($"本机和服务器时间不同步，请调整，本地：{DateTime.Now.ToString()}，服务器：{Timestamp.FromTimestamp(serverState.Time).ToString()}。此问题不影响挖矿。");
                                    }
                                });
                            }
                            else {
                                if (!File.Exists(HomePath.ServerJsonFileFullName)) {
                                    VirtualRoot.ThisLocalError(nameof(NTMinerContext), "配置文件下载失败，这是第一次运行开源矿工，配置文件至少需要成功下载一次，请检查网络是否可用", OutEnum.Warn);
                                }
                                else {
                                    VirtualRoot.ThisLocalWarn(nameof(NTMinerContext), "配置文件下载失败，使用最近一次成功下载的配置文件");
                                }
                            }
                            DoInit(callback);
                        });
                        #region 发生了用户活动时检查serverJson是否有新版本
                        VirtualRoot.BuildEventPath<UserActionEvent>("发生了用户活动时检查serverJson是否有新版本", LogEnum.DevConsole,
                            path: message => {
                                RefreshServerJsonFile();
                            }, location: this.GetType());
                        #endregion
                    }
                }
                VirtualRoot.ThisLocalInfo(nameof(NTMinerContext), $"启动{VirtualRoot.AppName}");
            });
        }

        private MinerProfile _minerProfile;
        private void DoInit(Action callback) {
            this.ReporterDataProvider = new ReportDataProvider();
            this.CalcConfigSet = new CalcConfigSet(this);
            this.ServerContext = new ServerContext();
            this.GpuProfileSet = new GpuProfileSet(this);
            this.KernelProfileSet = new KernelProfileSet();
            this.GpusSpeed = new GpusSpeed(this);
            this.CoinShareSet = new CoinShareSet(this);
            this.OverClockDataSet = new OverClockDataSet(this);
            this.ServerMessageSet = new ServerMessageSet(HomePath.LocalDbFileFullName, isServer: false);
            this._minerProfile = new MinerProfile(this);
            var cpuPackage = new CpuPackage(_minerProfile);
            this.CpuPackage = cpuPackage;

            // 这几个注册表内部区分挖矿端和群控客户端
            NTMinerRegistry.SetLocation(ClientAppType.AppType, VirtualRoot.AppFileFullName);
            NTMinerRegistry.SetArguments(ClientAppType.AppType, string.Join(" ", CommandLineArgs.Args));
            NTMinerRegistry.SetCurrentVersion(ClientAppType.AppType, EntryAssemblyInfo.CurrentVersionStr);
            NTMinerRegistry.SetCurrentVersionTag(ClientAppType.AppType, EntryAssemblyInfo.CurrentVersionTag);

            if (ClientAppType.IsMinerClient) {
                Link();
                // 当显卡温度变更时守卫温度防线
                TempGruarder.Instance.Init(this);
            }

            callback?.Invoke();
            cpuPackage.Init();
        }

        // MinerProfile对应local.litedb或local.json
        // 群控客户端管理作业时调用该方法切换MinerProfile上下文
        public void ReInitMinerProfile(WorkType workType) {
            if (_workType != workType) {
                _workType = workType;
            }
            // 注意这行是必须的，非作业模式时不是使用的local.json，但这一行只是把_localJsonInited置为false
            ReInitLocalJson();
            this._minerProfile.ReInit(this);
            RefreshArgsAssembly.Invoke("重新初始化MinerProfile后");
        }

        private void RefreshServerJsonFile() {
            RpcRoot.OfficialServer.AppSettingService.GetJsonFileVersionAsync(HomePath.ExportServerJsonFileName, serverState => {
                AppVersionChangedEvent.PublishIfNewVersion(serverState.MinerClientVersion);
                string localServerJsonFileVersion = GetServerJsonVersion();
                if (!string.IsNullOrEmpty(serverState.JsonFileVersion) && localServerJsonFileVersion != serverState.JsonFileVersion) {
                    RpcRoot.OSSService.AliyunOSSService.GetAliyunServerJson((data) => {
                        if (data == null || data.Length == 0) {
                            return;
                        }
                        string rawJson = Encoding.UTF8.GetString(data);
                        HomePath.WriteServerJsonFile(rawJson);
                        SetServerJsonVersion(serverState.JsonFileVersion);
                        ContextReInit();
                        VirtualRoot.ThisLocalInfo(nameof(NTMinerContext), $"刷新server.json配置", toConsole: true);
                    });
                }
                else {
                    NTMinerConsole.DevDebug("server.json没有新版本");
                }
                if (serverState.WsStatus == WsStatus.Online) {
                    VirtualRoot.RaiseEvent(new WsServerOkEvent());
                }
            });
        }

        #endregion

        public string GetServerJsonVersion() {
            string serverJsonVersion = string.Empty;
            if (VirtualRoot.LocalAppSettingSet.TryGetAppSetting(NTKeyword.ServerJsonVersionAppSettingKey, out IAppSetting setting) && setting.Value != null) {
                serverJsonVersion = setting.Value.ToString();
            }
            return serverJsonVersion;
        }

        private void SetServerJsonVersion(string serverJsonVersion) {
            AppSettingData appSettingData = new AppSettingData() {
                Key = NTKeyword.ServerJsonVersionAppSettingKey,
                Value = serverJsonVersion
            };
            string oldVersion = GetServerJsonVersion();
            VirtualRoot.Execute(new SetLocalAppSettingCommand(appSettingData));
            VirtualRoot.RaiseEvent(new ServerJsonVersionChangedEvent(oldVersion, serverJsonVersion));
        }

        #region private methods

        private void ContextReInit() {
            ReInitServerJson();
            this.ServerContext.ReInit();
            ReInitMinerProfile(_workType);
        }

        private void Link() {
            VirtualRoot.BuildCmdPath<RegCmdHereCommand>(path: message => {
                try {
                    Windows.Cmd.RegCmdHere();
                    VirtualRoot.ThisLocalInfo(nameof(NTMinerContext), "添加windows右键命令行成功");
                }
                catch (Exception e) {
                    Logger.ErrorDebugLine(e);
                    VirtualRoot.ThisLocalError(nameof(NTMinerContext), "添加windows右键命令行失败", OutEnum.Warn);
                }
            }, location: this.GetType());
            VirtualRoot.BuildCmdPath<UnRegCmdHereCommand>(path: message => {
                try {
                    Windows.Cmd.UnRegCmdHere();
                    VirtualRoot.ThisLocalInfo(nameof(NTMinerContext), "移除windows右键命令行成功");
                }
                catch (Exception e) {
                    Logger.ErrorDebugLine(e);
                    VirtualRoot.ThisLocalError(nameof(NTMinerContext), "移除windows右键命令行失败", OutEnum.Warn);
                }
            }, location: this.GetType());
            #region 挖矿开始时将无份额内核重启份额计数置0
            int shareCount = 0;
            DateTime shareOn = DateTime.Now;
            DateTime hightSpeedOn = DateTime.Now;
            VirtualRoot.BuildEventPath<MineStartedEvent>("挖矿开始后将无份额内核重启份额计数置0", LogEnum.DevConsole,
                path: message => {
                    // 将无份额内核重启份额计数置0
                    shareCount = 0;
                    hightSpeedOn = DateTime.Now;
                    if (!message.MineContext.IsRestart) {
                        // 当不是内核重启时更新shareOn，如果是内核重启不用更新shareOn从而给不干扰无内核矿机重启的逻辑
                        shareOn = DateTime.Now;
                    }
                }, location: this.GetType());
            #endregion
            #region 每20秒钟检查是否需要重启
            VirtualRoot.BuildEventPath<Per20SecondEvent>("每20秒钟检查是否需要重启", LogEnum.None,
                path: message => {
                    #region 低算力重启电脑
                    if (IsMining && LockedMineContext.ProcessCreatedOn != DateTime.MinValue) {
                        var coinProfile = MinerProfile.GetCoinProfile(MinerProfile.CoinId);
                        if (coinProfile.IsLowSpeedRestartComputer && coinProfile.LowSpeed != 0 && coinProfile.LowSpeedRestartComputerMinutes > 0) {
                            IGpuSpeed totalSpeed = GpusSpeed.CurrentSpeed(GpuAllId);
                            if (totalSpeed.MainCoinSpeed.SpeedOn.AddMinutes(coinProfile.LowSpeedRestartComputerMinutes) >= message.BornOn) {
                                if (totalSpeed.MainCoinSpeed.Value.ToNearSpeed(coinProfile.LowSpeed) >= coinProfile.LowSpeed) {
                                    hightSpeedOn = message.BornOn;
                                }
                            }
                            if (hightSpeedOn.AddMinutes(coinProfile.LowSpeedRestartComputerMinutes) < message.BornOn) {
                                string coinCode = string.Empty;
                                if (ServerContext.CoinSet.TryGetCoin(MinerProfile.CoinId, out ICoin coin)) {
                                    coinCode = coin.Code;
                                }
                                VirtualRoot.ThisLocalWarn(nameof(NTMinerContext), $"{coinCode}总算力持续{coinProfile.LowSpeedRestartComputerMinutes}分钟低于{coinProfile.LowSpeed}重启电脑", toConsole: true);
                                VirtualRoot.Execute(new ShowRestartWindowsCommand(countDownSeconds: 10));
                                if (!MinerProfile.IsAutoBoot || !NTMinerRegistry.GetIsAutoStart()) {
                                    VirtualRoot.Execute(new SetAutoStartCommand(true, true));
                                }
                                return;
                            }
                        }
                        else {
                            hightSpeedOn = message.BornOn;
                        }
                    }
                    #endregion

                    #region 周期重启电脑
                    try {
                        if (MinerProfile.IsPeriodicRestartComputer) {
                            if ((DateTime.Now - this.CreatedOn).TotalMinutes > 60 * MinerProfile.PeriodicRestartComputerHours + MinerProfile.PeriodicRestartComputerMinutes) {
                                string content = $"每运行{MinerProfile.PeriodicRestartKernelHours.ToString()}小时{MinerProfile.PeriodicRestartComputerMinutes.ToString()}分钟重启电脑";
                                VirtualRoot.ThisLocalWarn(nameof(NTMinerContext), content, toConsole: true);
                                VirtualRoot.Execute(new ShowRestartWindowsCommand(countDownSeconds: 10));
                                if (!MinerProfile.IsAutoBoot || !NTMinerRegistry.GetIsAutoStart()) {
                                    VirtualRoot.Execute(new SetAutoStartCommand(true, true));
                                }
                                return;// 退出
                            }
                        }
                    }
                    catch (Exception e) {
                        Logger.ErrorDebugLine(e);
                    }
                    #endregion

                    #region 周期重启内核
                    try {
                        if (IsMining && MinerProfile.IsPeriodicRestartKernel && LockedMineContext.MineStartedOn != DateTime.MinValue) {
                            if ((DateTime.Now - LockedMineContext.MineStartedOn).TotalMinutes > 60 * MinerProfile.PeriodicRestartKernelHours + MinerProfile.PeriodicRestartKernelMinutes) {
                                VirtualRoot.ThisLocalWarn(nameof(NTMinerContext), $"每运行{MinerProfile.PeriodicRestartKernelHours.ToString()}小时{MinerProfile.PeriodicRestartKernelMinutes.ToString()}分钟重启内核", toConsole: true);
                                RestartMine();
                                return;// 退出
                            }
                        }
                    }
                    catch (Exception e) {
                        Logger.ErrorDebugLine(e);
                    }
                    #endregion

                    #region 无份额重启内核
                    try {
                        if (IsMining && this.LockedMineContext.MainCoin != null) {
                            int totalShare = 0;
                            bool restartComputer = MinerProfile.NoShareRestartComputerMinutes >0 && MinerProfile.IsNoShareRestartComputer && (DateTime.Now - shareOn).TotalMinutes > MinerProfile.NoShareRestartComputerMinutes;
                            bool restartKernel = MinerProfile.NoShareRestartKernelMinutes > 0 && MinerProfile.IsNoShareRestartKernel && (DateTime.Now - shareOn).TotalMinutes > MinerProfile.NoShareRestartKernelMinutes;
                            if (restartComputer || restartKernel) {
                                ICoinShare mainCoinShare = this.CoinShareSet.GetOrCreate(this.LockedMineContext.MainCoin.GetId());
                                totalShare = mainCoinShare.TotalShareCount;
                                if ((this.LockedMineContext is IDualMineContext dualMineContext) && dualMineContext.DualCoin != null) {
                                    ICoinShare dualCoinShare = this.CoinShareSet.GetOrCreate(dualMineContext.DualCoin.GetId());
                                    totalShare += dualCoinShare.TotalShareCount;
                                }
                                // 如果份额没有增加
                                if (shareCount == totalShare) {
                                    bool isMineStartedARestartComputerMinutes = (DateTime.Now - this.LockedMineContext.MineStartedOn).TotalMinutes > MinerProfile.NoShareRestartComputerMinutes;
                                    if (restartComputer && isMineStartedARestartComputerMinutes) {
                                        if (!MinerProfile.IsAutoBoot || !NTMinerRegistry.GetIsAutoStart()) {
                                            VirtualRoot.Execute(new SetAutoStartCommand(true, true));
                                        }
                                        string content = $"{MinerProfile.NoShareRestartComputerMinutes.ToString()}分钟无份额重启电脑";
                                        VirtualRoot.ThisLocalWarn(nameof(NTMinerContext), content, toConsole: true);
                                        VirtualRoot.Execute(new ShowRestartWindowsCommand(countDownSeconds: 10));
                                        return;// 退出
                                    }
                                    bool isMineStartedARestartKernelMinutes = (DateTime.Now - this.LockedMineContext.MineStartedOn).TotalMinutes > MinerProfile.NoShareRestartKernelMinutes;
                                    if (restartKernel && isMineStartedARestartKernelMinutes) {
                                        VirtualRoot.ThisLocalWarn(nameof(NTMinerContext), $"{MinerProfile.NoShareRestartKernelMinutes.ToString()}分钟无份额重启内核", toConsole: true);
                                        RestartMine();
                                        return;// 退出
                                    }
                                }
                                if (totalShare > shareCount) {
                                    shareCount = totalShare;
                                    shareOn = DateTime.Now;
                                }
                            }
                        }
                    }
                    catch (Exception e) {
                        Logger.ErrorDebugLine(e);
                    }
                    #endregion
                }, location: this.GetType());
            #endregion
            VirtualRoot.BuildEventPath<Per10SecondEvent>("周期刷新显卡状态", LogEnum.None,
                path: message => {
                    // 因为遇到显卡系统状态变更时可能费时
                    Task.Factory.StartNew(() => {
                        GpuSet.LoadGpuState();
                    });
                }, location: this.GetType());
        }
        #endregion

        public StopMineReason StopReason { get; private set; }
        #region StopMine
        public void StopMineAsync(StopMineReason stopReason, Action callback = null) {
            if (!IsMining) {
                callback?.Invoke();
                return;
            }
            Task.Factory.StartNew(() => {
                StopMine(stopReason);
                callback?.Invoke();
            });
        }
        private void StopMine(StopMineReason stopReason) {
            this.StopReason = stopReason;
            var mineContext = LockedMineContext;
            LockedMineContext = null;
            mineContext?.Close();
            if (!IsMining) {
                return;
            }
            try {
                VirtualRoot.ThisLocalWarn(nameof(NTMinerContext), $"挖矿停止。原因：{stopReason.GetDescription()}", toConsole: true);
                VirtualRoot.RaiseEvent(new MineStopedEvent(mineContext, stopReason));
            }
            catch (Exception e) {
                Logger.ErrorDebugLine(e);
            }
        }
        #endregion

        #region RestartMine
        public void RestartMine(WorkType workType = WorkType.None, string workerName = null) {
            bool isPreIsWork = _workType != WorkType.None;
            _workType = workType;
            _workerName = workerName;
            if (workType == WorkType.SelfWork) {
                if (!File.Exists(HomePath.SelfWorkLocalJsonFileFullName) ||
                    !File.Exists(HomePath.SelfWorkServerJsonFileFullName)) {
                    VirtualRoot.RaiseEvent(new StartingMineFailedEvent($"开始挖矿失败，因为单机作业不存在，请先在群控端编辑单机作业后再开始挖矿。"));
                    return;
                }
            }
            else if (workType == WorkType.MineWork) {
                if (!File.Exists(HomePath.MineWorkLocalJsonFileFullName) ||
                    !File.Exists(HomePath.MineWorkServerJsonFileFullName)) {
                    VirtualRoot.RaiseEvent(new StartingMineFailedEvent($"开始挖矿失败，因为挖矿作业不存在，请在群控端为矿机分配作业后再开始挖矿。"));
                    return;
                }
            }
            NTMinerRegistry.SetWorkType(workType);
            if (workType != WorkType.None || isPreIsWork) {
                ContextReInit();
            }
            StartMine(isRestart: true);
        }
        #endregion

        private bool GetProfileData(
            out ICoin mainCoin, out ICoinProfile mainCoinProfile, out IPool mainCoinPool, out ICoinKernel mainCoinKernel, out IKernel kernel,
            out IKernelInput kernelInput, out IKernelOutput kernelOutput, out string errorMsg) {
            mainCoinProfile = null;
            mainCoinPool = null;
            mainCoinKernel = null;
            kernel = null;
            kernelInput = null;
            kernelOutput = null;
            errorMsg = string.Empty;
            IWorkProfile minerProfile = this.MinerProfile;
            if (!ServerContext.CoinSet.TryGetCoin(minerProfile.CoinId, out mainCoin)) {
                errorMsg = "没有选择主挖币种。";
                return false;
            }
            mainCoinProfile = minerProfile.GetCoinProfile(minerProfile.CoinId);
            if (!ServerContext.PoolSet.TryGetPool(mainCoinProfile.PoolId, out mainCoinPool)) {
                errorMsg = "没有选择主币矿池。";
                return false;
            }
            if (!ServerContext.CoinKernelSet.TryGetCoinKernel(mainCoinProfile.CoinKernelId, out mainCoinKernel)) {
                errorMsg = "没有选择挖矿内核。";
                return false;
            }
            if (!ServerContext.KernelSet.TryGetKernel(mainCoinKernel.KernelId, out kernel)) {
                errorMsg = "无效的挖矿内核。";
                return false;
            }
            if (!ServerContext.KernelInputSet.TryGetKernelInput(kernel.KernelInputId, out kernelInput)) {
                errorMsg = "未设置内核输入。";
                return false;
            }
            if (!ServerContext.KernelOutputSet.TryGetKernelOutput(kernel.KernelOutputId, out kernelOutput)) {
                errorMsg = "未设置内核输出。";
                return false;
            }
            return true;
        }

        #region StartMine
        public void StartMine(bool isRestart = false) {
            try {
                VirtualRoot.RaiseEvent(new StartingMineEvent());
                if (IsMining && isRestart) {
                    LockedMineContext.Start(isRestart: true);
                    VirtualRoot.ThisLocalInfo(nameof(NTMinerContext), "重启内核", toConsole: true);
                }
                else {
                    LockedMineContext?.Close();
                    if (IsMining) {
                        // 如果已经处在挖矿中了又点击了开始挖矿按钮（通常发生在群控端）
                        this.StopMine(StopMineReason.InStartMine);
                    }
                    IWorkProfile minerProfile = this.MinerProfile;
                    if (!GetProfileData(out ICoin mainCoin, out ICoinProfile mainCoinProfile, out IPool mainCoinPool, out ICoinKernel mainCoinKernel,
                        out IKernel kernel, out IKernelInput kernelInput, out IKernelOutput kernelOutput, out string errorMsg)) {
                        VirtualRoot.RaiseEvent(new StartingMineFailedEvent(errorMsg));
                        return;
                    }
                    if (!kernel.IsSupported(mainCoin)) {
                        VirtualRoot.RaiseEvent(new StartingMineFailedEvent($"该内核不支持{GpuSet.GpuType.GetDescription()}卡。"));
                        return;
                    }
                    if (string.IsNullOrEmpty(mainCoinProfile.Wallet)) {
                        MinerProfile.SetCoinProfileProperty(mainCoin.GetId(), nameof(mainCoinProfile.Wallet), mainCoin.TestWallet);
                    }
                    if (mainCoinPool.IsUserMode) {
                        IPoolProfile poolProfile = minerProfile.GetPoolProfile(mainCoinPool.GetId());
                        string userName = poolProfile.UserName;
                        if (string.IsNullOrEmpty(userName)) {
                            VirtualRoot.RaiseEvent(new StartingMineFailedEvent("没有填写矿池用户名。"));
                            return;
                        }
                    }
                    if (string.IsNullOrEmpty(mainCoinProfile.Wallet) && !mainCoinPool.IsUserMode) {
                        VirtualRoot.RaiseEvent(new StartingMineFailedEvent("没有填写钱包地址。"));
                        return;
                    }
                    ICoinKernelProfile coinKernelProfile = minerProfile.GetCoinKernelProfile(mainCoinKernel.GetId());
                    ICoin dualCoin = null;
                    IPool dualCoinPool = null;
                    ICoinProfile dualCoinProfile = null;
                    if (coinKernelProfile.IsDualCoinEnabled) {
                        if (!ServerContext.CoinSet.TryGetCoin(coinKernelProfile.DualCoinId, out dualCoin)) {
                            VirtualRoot.RaiseEvent(new StartingMineFailedEvent("没有选择双挖币种。"));
                            return;
                        }
                        dualCoinProfile = minerProfile.GetCoinProfile(coinKernelProfile.DualCoinId);
                        if (!ServerContext.PoolSet.TryGetPool(dualCoinProfile.DualCoinPoolId, out dualCoinPool)) {
                            VirtualRoot.RaiseEvent(new StartingMineFailedEvent("没有选择双挖矿池。"));
                            return;
                        }
                        if (string.IsNullOrEmpty(dualCoinProfile.DualCoinWallet)) {
                            MinerProfile.SetCoinProfileProperty(dualCoin.GetId(), nameof(dualCoinProfile.DualCoinWallet), dualCoin.TestWallet);
                        }
                        if (string.IsNullOrEmpty(dualCoinProfile.DualCoinWallet)) {
                            VirtualRoot.RaiseEvent(new StartingMineFailedEvent("没有填写双挖钱包。"));
                            return;
                        }
                    }
                    if (string.IsNullOrEmpty(kernel.Package)) {
                        VirtualRoot.RaiseEvent(new StartingMineFailedEvent(kernel.GetFullName() + "没有内核包"));
                        return;
                    }
                    if (string.IsNullOrEmpty(kernelInput.Args)) {
                        VirtualRoot.RaiseEvent(new StartingMineFailedEvent("没有配置运行参数。"));
                        return;
                    }
                    string packageZipFileFullName = Path.Combine(HomePath.PackagesDirFullName, kernel.Package);
                    if (!File.Exists(packageZipFileFullName)) {
                        VirtualRoot.ThisLocalInfo(nameof(NTMinerContext), kernel.GetFullName() + "本地内核包不存在，开始自动下载", toConsole: true);
                        VirtualRoot.Execute(new ShowKernelDownloaderCommand(kernel.GetId(), downloadComplete: (isSuccess, message) => {
                            if (isSuccess) {
                                NTMinerConsole.UserOk(message);
                                ContinueStartMine(kernel);
                            }
                            else {
                                VirtualRoot.RaiseEvent(new StartingMineFailedEvent("内核下载：" + message));
                            }
                        }));
                    }
                    else {
                        ContinueStartMine(kernel);
                    }
                }
            }
            catch (Exception e) {
                Logger.ErrorDebugLine(e);
            }
        }

        private void ContinueStartMine(IKernel kernel) {
            // 解压内核包
            if (!kernel.ExtractPackage()) {
                VirtualRoot.RaiseEvent(new StartingMineFailedEvent("内核解压失败，请卸载内核重试。"));
                return;
            }
            NTMinerConsole.UserOk("内核包解压成功");
            if (CurrentMineContext != null) {
                LockedMineContext = CurrentMineContext;
            }
            else {
                LockedMineContext = CreateMineContext();
                CurrentMineContext = LockedMineContext;
            }
            if (LockedMineContext == null) {
                return;
            }
            LockedMineContext.Start(isRestart: false);
            VirtualRoot.ThisLocalInfo(nameof(NTMinerContext), "开始挖矿", toConsole: true);
            if (LockedMineContext.UseDevices.Length < GpuSet.Count) {
                VirtualRoot.ThisLocalWarn(nameof(NTMinerContext), "未启用全部显卡挖矿", toConsole: true);
            }
        }
        #endregion

        private IMineContext _currentMineContext;
        public IMineContext CurrentMineContext {
            get { return _currentMineContext; }
            set {
                if (_currentMineContext != value) {
                    _currentMineContext = value;
                    VirtualRoot.RaiseEvent(new CurrentMineContextChangedEvent());
                }
            }
        }

        private IMineContext _lockedMineContext;
        public IMineContext LockedMineContext {
            get {
                return _lockedMineContext;
            }
            private set {
                if (_lockedMineContext != value) {
                    _lockedMineContext?.Close();
                    _lockedMineContext = value;
                }
            }
        }

        public bool IsMining {
            get {
                return LockedMineContext != null;
            }
        }

        public IReportDataProvider ReporterDataProvider { get; private set; }

        public IServerContext ServerContext { get; private set; }

        public IGpuProfileSet GpuProfileSet { get; private set; }

        public IWorkProfile MinerProfile {
            get { return _minerProfile; }
        }

        public IOverClockDataSet OverClockDataSet { get; private set; }

        public ICalcConfigSet CalcConfigSet { get; private set; }

        #region GpuSetInfo
        private string _gpuSetInfo = null;
        public string GpuSetInfo {
            get {
                if (_gpuSetInfo == null) {
                    StringBuilder sb = new StringBuilder();
                    int len = sb.Length;
                    foreach (var g in GpuSet.AsEnumerable().Where(a => a.Index != GpuAllId).GroupBy(a => a.Name)) {
                        if (sb.Length != len) {
                            sb.Append("/");
                        }
                        int gCount = g.Count();
                        if (gCount > 1) {
                            sb.Append(g.Key).Append(" x ").Append(gCount);
                        }
                        else {
                            sb.Append(g.Key);
                        }
                    }
                    _gpuSetInfo = sb.ToString();
                    if (_gpuSetInfo.Length == 0) {
                        _gpuSetInfo = "无";
                    }
                }
                return _gpuSetInfo;
            }
        }
        #endregion

        #region GpuSet
        private static bool IsNCard {
            get {
                try {
                    if (CommandLineArgs.Args.Contains("--amd")) {
                        return false;
                    }
                    using (var mos = new ManagementObjectSearcher("SELECT Caption FROM Win32_VideoController")) {
                        foreach (ManagementBaseObject item in mos.Get()) {
                            foreach (var property in item.Properties) {
                                if ((property.Value ?? string.Empty).ToString().IgnoreCaseContains("NVIDIA")) {
                                    return true;
                                }
                            }
                        }
                    }
                }
                catch (Exception e) {
                    Logger.ErrorDebugLine(e);
                }
                return false;
            }
        }

        private IGpuSet _gpuSet;
        public IGpuSet GpuSet {
            get {
                if (_gpuSet == null) {
                    lock (_locker) {
                        if (_gpuSet == null) {
                            if (ClientAppType.IsMinerClient) {
                                try {
                                    if (IsNCard) {
                                        _gpuSet = new NVIDIAGpuSet(this);
                                        if (AdlHelper.IsHasATIGpu) {
                                            VirtualRoot.ThisLocalWarn(this.GetType().Name, "检测到本机除了N卡外还插有A卡，开源不建议N卡A卡混插。", toConsole: true);
                                        }
                                    }
                                    else {
                                        _gpuSet = new AMDGpuSet();
                                    }
                                }
                                catch (Exception ex) {
                                    _gpuSet = EmptyGpuSet.Instance;
                                    Logger.ErrorDebugLine(ex);
                                }
                            }
                            else {
                                _gpuSet = EmptyGpuSet.Instance;
                            }
                            if (_gpuSet == null || (_gpuSet != EmptyGpuSet.Instance && _gpuSet.Count == 0)) {
                                _gpuSet = EmptyGpuSet.Instance;
                            }
                        }
                    }
                }
                return _gpuSet;
            }
        }
        #endregion

        public ICpuPackage CpuPackage { get; private set; }

        public IKernelProfileSet KernelProfileSet { get; private set; }

        public IGpusSpeed GpusSpeed { get; private set; }

        public ICoinShareSet CoinShareSet { get; private set; }

        private IKernelOutputKeywordSet _kernelOutputKeywordSet;
        public IKernelOutputKeywordSet KernelOutputKeywordSet {
            get {
                if (_kernelOutputKeywordSet == null) {
                    lock (_locker) {
                        if (_kernelOutputKeywordSet == null) {
                            _kernelOutputKeywordSet = new KernelOutputKeywordSet(HomePath.LocalDbFileFullName, isServer: false);
                        }
                    }
                }
                return _kernelOutputKeywordSet;
            }
        }

        public ILocalMessageSet LocalMessageSet { get; private set; }

        public IServerMessageSet ServerMessageSet { get; private set; }

        public Version MinAmdDriverVersion {
            get {
                return ServerContext.SysDicItemSet.TryGetDicItemValue(NTKeyword.ThisSystemSysDicCode, "MinAmdDriverVersion", new Version(17, 10, 2, 0));
            }
        }

        public Version MinNvidiaDriverVersion {
            get {
                return ServerContext.SysDicItemSet.TryGetDicItemValue(NTKeyword.ThisSystemSysDicCode, "MinNvidiaDriverVersion", new Version(399, 24));
            }
        }
    }
}
