﻿using NTMiner.Core;
using NTMiner.Hub;
using NTMiner.Vms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace NTMiner {
    /// <summary>
    /// 该类型不是用于绑定到视图的，AppStatic才是
    /// </summary>
    public static partial class AppRoot {
        public static ExtendedNotifyIcon NotifyIcon;
        public const string LowWinMessage = "Windows版本较低，建议使用Win10系统";

        private static readonly List<IMessagePathId> _contextPathIds = new List<IMessagePathId>();

        static AppRoot() {
        }

        #region methods
        // 因为是上下文路径，无需返回路径标识
        public static void BuildCmdPath<TCmd>(string description, LogEnum logType, Action<TCmd> path, Type location)
            where TCmd : ICmd {
            var messagePathId = VirtualRoot.BuildMessagePath(description, logType, path, location);
            _contextPathIds.Add(messagePathId);
        }

        // 因为是上下文路径，无需返回路径标识
        public static void BuildEventPath<TEvent>(string description, LogEnum logType, Action<TEvent> path, Type location)
            where TEvent : IEvent {
            var messagePathId = VirtualRoot.BuildMessagePath(description, logType, path, location);
            _contextPathIds.Add(messagePathId);
        }

        /// <summary>
        /// 解封路
        /// </summary>
        public static void Enable() {
            foreach (var pathId in _contextPathIds) {
                pathId.IsEnabled = true;
            }
        }

        /// <summary>
        /// 封路，禁止通行。没多大意义。
        /// </summary>
        public static void Disable() {
            foreach (var pathId in _contextPathIds) {
                pathId.IsEnabled = false;
            }
        }
        #endregion

        #region MainWindowHeight MainWindowWidth
        public static double MainWindowHeight {
            get {
                if (SystemParameters.WorkArea.Size.Height >= 620) {
                    return 620;
                }
                else if (SystemParameters.WorkArea.Size.Height >= 520) {
                    return 520;
                }
                return 480;
            }
        }

        public static double MainWindowWidth {
            get {
                if (SystemParameters.WorkArea.Size.Width >= 1090) {
                    return 1090;
                }
                else if (SystemParameters.WorkArea.Size.Width >= 1000) {
                    return 1000;
                }
                else if (SystemParameters.WorkArea.Size.Width >= 860) {
                    return 860;
                }
                else if (SystemParameters.WorkArea.Size.Width >= 800) {
                    return 800;
                }
                return 640;
            }
        }
        #endregion

        #region 字典项

        public static string NppPackageUrl {
            get {
                const string url = "https://minerjson.oss-cn-beijing.aliyuncs.com/npp.zip";
                if (WpfUtil.IsDevMode) {
                    return url;
                }
                return NTMinerContext.Instance.ServerContext.SysDicItemSet.TryGetDicItemValue("Tool", "npp", defaultValue: url);
            }
        }
        #endregion

        public static double OsVmPerGpu {
            get {
                double value = 5.0;
                if (WpfUtil.IsDevMode) {
                    return value;
                }
                return NTMinerContext.Instance.ServerContext.SysDicItemSet.TryGetDicItemValue(NTKeyword.ThisSystemSysDicCode, "OsVmPerGpu", value);
            }
        }

        public static string VirtualMemoryDescription {
            get {
                return $"挖 ETH 每卡至少设置 {OsVmPerGpu.ToString("f1")}G 虚拟内存，建议按虚拟内存比显存 1 比 1 设置。比如 6 张 6G 的 1066 显卡建议设置 6 x 6 = 36G。";
            }
        }

        #region Upgrade
        private static string GetUpdaterVersion() {
            string version = string.Empty;
            if (VirtualRoot.LocalAppSettingSet.TryGetAppSetting(NTKeyword.UpdaterVersionAppSettingKey, out IAppSetting setting) && setting.Value != null) {
                version = setting.Value.ToString();
            }
            return version;
        }

        private static void SetUpdaterVersion(string value) {
            VirtualRoot.Execute(new SetLocalAppSettingCommand(new AppSettingData {
                Key = NTKeyword.UpdaterVersionAppSettingKey,
                Value = value
            }));
        }

        public static void Upgrade(NTMinerAppType appType, string fileName, Action callback) {
            RpcRoot.OfficialServer.FileUrlService.GetNTMinerUpdaterUrlAsync((downloadFileUrl, e) => {
                try {
                    string argument = string.Empty;
                    if (!string.IsNullOrEmpty(fileName)) {
                        argument = "ntminerFileName=" + fileName;
                    }
                    if (appType == NTMinerAppType.MinerStudio) {
                        argument += " --minerstudio";
                    }
                    if (string.IsNullOrEmpty(downloadFileUrl)) {
                        if (File.Exists(HomePath.UpdaterFileFullName)) {
                            Windows.Cmd.RunClose(HomePath.UpdaterFileFullName, argument);
                        }
                        callback?.Invoke();
                        return;
                    }
                    Uri uri = new Uri(downloadFileUrl);
                    string localVersion = GetUpdaterVersion();
                    if (string.IsNullOrEmpty(localVersion) || !File.Exists(HomePath.UpdaterFileFullName) || uri.AbsolutePath != localVersion) {
                        VirtualRoot.Execute(new ShowFileDownloaderCommand(downloadFileUrl, "开源矿工更新器", (window, isSuccess, message, saveFileFullName) => {
                            try {
                                if (isSuccess) {
                                    string updateDirFullName = Path.GetDirectoryName(HomePath.UpdaterFileFullName);
                                    if (!Directory.Exists(updateDirFullName)) {
                                        Directory.CreateDirectory(updateDirFullName);
                                    }
                                    File.Delete(HomePath.UpdaterFileFullName);
                                    File.Move(saveFileFullName, HomePath.UpdaterFileFullName);
                                    SetUpdaterVersion(uri.AbsolutePath);
                                    window?.Close();
                                    Windows.Cmd.RunClose(HomePath.UpdaterFileFullName, argument);
                                    callback?.Invoke();
                                }
                                else {
                                    VirtualRoot.ThisLocalError(nameof(AppRoot), "下载新版本：" + message, toConsole: true);
                                    callback?.Invoke();
                                }
                            }
                            catch (Exception ex) {
                                Logger.ErrorDebugLine(ex);
                                callback?.Invoke();
                            }
                        }));
                    }
                    else {
                        Windows.Cmd.RunClose(HomePath.UpdaterFileFullName, argument);
                        callback?.Invoke();
                    }
                }
                catch (Exception ex) {
                    Logger.ErrorDebugLine(ex);
                    callback?.Invoke();
                }
            });
        }
        #endregion

        #region OpenMinerClientFinder
        private static string GetMinerClientFinderVersion() {
            string version = string.Empty;
            if (VirtualRoot.LocalAppSettingSet.TryGetAppSetting(NTKeyword.MinerClientFinderVersionAppSettingKey, out IAppSetting setting) && setting.Value != null) {
                version = setting.Value.ToString();
            }
            return version;
        }

        private static void SetMinerClientFinderVersion(string value) {
            VirtualRoot.Execute(new SetLocalAppSettingCommand(new AppSettingData {
                Key = NTKeyword.MinerClientFinderVersionAppSettingKey,
                Value = value
            }));
        }

        public static void OpenMinerClientFinder() {
            RpcRoot.OfficialServer.FileUrlService.GetMinerClientFinderUrlAsync((downloadFileUrl, e) => {
                try {
                    if (string.IsNullOrEmpty(downloadFileUrl)) {
                        if (File.Exists(MinerClientTempPath.MinerClientFinderFileFullName)) {
                            Windows.Cmd.RunClose(MinerClientTempPath.MinerClientFinderFileFullName, string.Empty, waitForExit: false);
                        }
                        return;
                    }
                    Uri uri = new Uri(downloadFileUrl);
                    string localVersion = GetMinerClientFinderVersion();
                    if (string.IsNullOrEmpty(localVersion) || !File.Exists(MinerClientTempPath.MinerClientFinderFileFullName) || uri.AbsolutePath != localVersion) {
                        VirtualRoot.Execute(new ShowFileDownloaderCommand(downloadFileUrl, "下载矿机雷达", (window, isSuccess, message, saveFileFullName) => {
                            try {
                                if (isSuccess) {
                                    File.Delete(MinerClientTempPath.MinerClientFinderFileFullName);
                                    File.Move(saveFileFullName, MinerClientTempPath.MinerClientFinderFileFullName);
                                    SetMinerClientFinderVersion(uri.AbsolutePath);
                                    window?.Close();
                                    Windows.Cmd.RunClose(MinerClientTempPath.MinerClientFinderFileFullName, string.Empty, waitForExit: false);
                                }
                                else {
                                    VirtualRoot.ThisLocalError(nameof(AppRoot), "下载矿机雷达：" + message, toConsole: true);
                                }
                            }
                            catch (Exception ex) {
                                Logger.ErrorDebugLine(ex);
                            }
                        }));
                    }
                    else {
                        Windows.Cmd.RunClose(MinerClientTempPath.MinerClientFinderFileFullName, string.Empty, waitForExit: false);
                    }
                }
                catch (Exception ex) {
                    Logger.ErrorDebugLine(ex);
                }
            });
        }
        #endregion

        #region OpenAtikmdagPatcher
        private static string GetAtikmdagPatcherVersion() {
            string version = string.Empty;
            if (VirtualRoot.LocalAppSettingSet.TryGetAppSetting(NTKeyword.AtikmdagPatcherVersionAppSettingKey, out IAppSetting setting) && setting.Value != null) {
                version = setting.Value.ToString();
            }
            return version;
        }

        private static void SetAtikmdagPatcherVersion(string value) {
            VirtualRoot.Execute(new SetLocalAppSettingCommand(new AppSettingData {
                Key = NTKeyword.AtikmdagPatcherVersionAppSettingKey,
                Value = value
            }));
        }

        public static void OpenAtikmdagPatcher() {
            RpcRoot.OfficialServer.FileUrlService.GetAtikmdagPatcherUrlAsync((downloadFileUrl, e) => {
                try {
                    if (string.IsNullOrEmpty(downloadFileUrl)) {
                        if (File.Exists(MinerClientTempPath.AtikmdagPatcherFileFullName)) {
                            VirtualRoot.Execute(new UnTopmostCommand());
                            Windows.Cmd.RunClose(MinerClientTempPath.AtikmdagPatcherFileFullName, string.Empty, waitForExit: false);
                        }
                        return;
                    }
                    Uri uri = new Uri(downloadFileUrl);
                    string localVersion = GetAtikmdagPatcherVersion();
                    if (string.IsNullOrEmpty(localVersion) || !File.Exists(MinerClientTempPath.AtikmdagPatcherFileFullName) || uri.AbsolutePath != localVersion) {
                        VirtualRoot.Execute(new ShowFileDownloaderCommand(downloadFileUrl, "下载A卡驱动签名工具", (window, isSuccess, message, saveFileFullName) => {
                            try {
                                if (isSuccess) {
                                    File.Delete(MinerClientTempPath.AtikmdagPatcherFileFullName);
                                    File.Move(saveFileFullName, MinerClientTempPath.AtikmdagPatcherFileFullName);
                                    SetAtikmdagPatcherVersion(uri.AbsolutePath);
                                    window?.Close();
                                    VirtualRoot.Execute(new UnTopmostCommand());
                                    Windows.Cmd.RunClose(MinerClientTempPath.AtikmdagPatcherFileFullName, string.Empty, waitForExit: false);
                                }
                                else {
                                    VirtualRoot.ThisLocalError(nameof(AppRoot), "下载A卡驱动签名工具：" + message, toConsole: true);
                                }
                            }
                            catch (Exception ex) {
                                Logger.ErrorDebugLine(ex);
                            }
                        }));
                    }
                    else {
                        VirtualRoot.Execute(new UnTopmostCommand());
                        Windows.Cmd.RunClose(MinerClientTempPath.AtikmdagPatcherFileFullName, string.Empty, waitForExit: false);
                    }
                }
                catch (Exception ex) {
                    Logger.ErrorDebugLine(ex);
                }
            });
        }
        #endregion

        #region OpenSwitchRadeonGpu
        private static string GetSwitchRadeonGpuVersion() {
            string version = string.Empty;
            if (VirtualRoot.LocalAppSettingSet.TryGetAppSetting(NTKeyword.SwitchRadeonGpuVersionAppSettingKey, out IAppSetting setting) && setting.Value != null) {
                version = setting.Value.ToString();
            }
            return version;
        }

        private static void SetSwitchRadeonGpuVersion(string value) {
            VirtualRoot.Execute(new SetLocalAppSettingCommand(new AppSettingData {
                Key = NTKeyword.SwitchRadeonGpuVersionAppSettingKey,
                Value = value
            }));
        }

        private static void ShowSwitchRadeonGpu(bool on) {
            if (on) {
                VirtualRoot.ThisLocalInfo(nameof(AppRoot), "开启A卡计算模式成功", OutEnum.Success);
            }
            else {
                VirtualRoot.ThisLocalInfo(nameof(AppRoot), "关闭A卡计算模式成功", OutEnum.Success);
            }
        }

        public static void SwitchRadeonGpu(bool on) {
            RpcRoot.OfficialServer.FileUrlService.GetSwitchRadeonGpuUrlAsync((downloadFileUrl, e) => {
                try {
                    string args = $"--compute={(on ? "on" : "off")} --admin --restart";
                    if (string.IsNullOrEmpty(downloadFileUrl)) {
                        if (File.Exists(MinerClientTempPath.SwitchRadeonGpuFileFullName)) {
                            Windows.Cmd.RunClose(MinerClientTempPath.SwitchRadeonGpuFileFullName, args, waitForExit: true);
                            ShowSwitchRadeonGpu(on);
                        }
                        return;
                    }
                    Uri uri = new Uri(downloadFileUrl);
                    string localVersion = GetSwitchRadeonGpuVersion();
                    if (string.IsNullOrEmpty(localVersion) || !File.Exists(MinerClientTempPath.SwitchRadeonGpuFileFullName) || uri.AbsolutePath != localVersion) {
                        VirtualRoot.Execute(new ShowFileDownloaderCommand(downloadFileUrl, "下载开启A卡计算模式工具", (window, isSuccess, message, saveFileFullName) => {
                            try {
                                if (isSuccess) {
                                    File.Delete(MinerClientTempPath.SwitchRadeonGpuFileFullName);
                                    File.Move(saveFileFullName, MinerClientTempPath.SwitchRadeonGpuFileFullName);
                                    SetSwitchRadeonGpuVersion(uri.AbsolutePath);
                                    window?.Close();
                                    Windows.Cmd.RunClose(MinerClientTempPath.SwitchRadeonGpuFileFullName, args, waitForExit: true);
                                    ShowSwitchRadeonGpu(on);
                                }
                                else {
                                    VirtualRoot.ThisLocalError(nameof(AppRoot), "下载开启A卡计算模式工具：" + message, toConsole: true);
                                }
                            }
                            catch (Exception ex) {
                                Logger.ErrorDebugLine(ex);
                            }
                        }));
                    }
                    else {
                        Windows.Cmd.RunClose(MinerClientTempPath.SwitchRadeonGpuFileFullName, args, waitForExit: true);
                        ShowSwitchRadeonGpu(on);
                    }
                }
                catch (Exception ex) {
                    Logger.ErrorDebugLine(ex);
                }
            });
        }
        #endregion

        #region OpenLiteDb
        public static void OpenLiteDb(string dbFileFullName) {
            string liteDbExplorerDir = Path.Combine(MinerClientTempPath.ToolsDirFullName, "LiteDBExplorerPortable");
            string liteDbExplorerFileFullName = Path.Combine(liteDbExplorerDir, "LiteDbExplorer.exe");
            if (!Directory.Exists(liteDbExplorerDir)) {
                Directory.CreateDirectory(liteDbExplorerDir);
            }
            if (!File.Exists(liteDbExplorerFileFullName)) {
                RpcRoot.OfficialServer.FileUrlService.GetLiteDbExplorerUrlAsync((downloadFileUrl, e) => {
                    if (string.IsNullOrEmpty(downloadFileUrl)) {
                        return;
                    }
                    VirtualRoot.Execute(new ShowFileDownloaderCommand(downloadFileUrl, "LiteDB数据库管理工具", (window, isSuccess, message, saveFileFullName) => {
                        if (isSuccess) {
                            ZipUtil.DecompressZipFile(saveFileFullName, liteDbExplorerDir);
                            File.Delete(saveFileFullName);
                            window?.Close();
                            Windows.Cmd.RunClose(liteDbExplorerFileFullName, dbFileFullName);
                        }
                    }));
                });
            }
            else {
                Windows.Cmd.RunClose(liteDbExplorerFileFullName, dbFileFullName);
            }
        }
        #endregion

        #region context
        public static MinerProfileViewModel MinerProfileVm {
            get {
                return MinerProfileViewModel.Instance;
            }
        }

        public static CoinViewModels CoinVms {
            get {
                return CoinViewModels.Instance;
            }
        }

        public static GpuSpeedViewModels GpuSpeedVms {
            get {
                return GpuSpeedViewModels.Instance;
            }
        }

        public static StartStopMineButtonViewModel StartStopMineButtonVm {
            get {
                return StartStopMineButtonViewModel.Instance;
            }
        }

        public static PoolKernelViewModels PoolKernelVms {
            get {
                return PoolKernelViewModels.Instance;
            }
        }

        public static PackageViewModels PackageVms {
            get {
                return PackageViewModels.Instance;
            }
        }

        public static CoinGroupViewModels CoinGroupVms {
            get {
                return CoinGroupViewModels.Instance;
            }
        }

        public static FileWriterViewModels FileWriterVms {
            get {
                return FileWriterViewModels.Instance;
            }
        }

        public static FragmentWriterViewModels FragmentWriterVms {
            get {
                return FragmentWriterViewModels.Instance;
            }
        }

        public static CoinKernelViewModels CoinKernelVms {
            get {
                return CoinKernelViewModels.Instance;
            }
        }

        public static CoinProfileViewModels CoinProfileVms {
            get {
                return CoinProfileViewModels.Instance;
            }
        }

        public static DriveSetViewModel DriveSetVm {
            get {
                return DriveSetViewModel.Instance;
            }
        }

        public static GpuProfileViewModels GpuProfileVms {
            get {
                return GpuProfileViewModels.Instance;
            }
        }

        public static GpuViewModels GpuVms {
            get {
                return GpuViewModels.Instance;
            }
        }

        public static GroupViewModels GroupVms {
            get {
                return GroupViewModels.Instance;
            }
        }

        public static KernelInputViewModels KernelInputVms {
            get {
                return KernelInputViewModels.Instance;
            }
        }

        public static KernelOutputKeywordViewModels KernelOutputKeywordVms {
            get {
                return KernelOutputKeywordViewModels.Instance;
            }
        }

        public static KernelOutputTranslaterViewModels KernelOutputTranslaterVms {
            get {
                return KernelOutputTranslaterViewModels.Instance;
            }
        }

        public static KernelOutputViewModels KernelOutputVms {
            get {
                return KernelOutputViewModels.Instance;
            }
        }

        public static KernelViewModels KernelVms {
            get {
                return KernelViewModels.Instance;
            }
        }

        public static PoolProfileViewModels PoolProfileVms {
            get {
                return PoolProfileViewModels.Instance;
            }
        }

        public static PoolViewModels PoolVms {
            get {
                return PoolViewModels.Instance;
            }
        }

        public static ShareViewModels ShareVms {
            get {
                return ShareViewModels.Instance;
            }
        }

        public static WalletViewModels WalletVms {
            get {
                return WalletViewModels.Instance;
            }
        }

        public static SysDicViewModels SysDicVms {
            get {
                return SysDicViewModels.Instance;
            }
        }

        public static SysDicItemViewModels SysDicItemVms {
            get {
                return SysDicItemViewModels.Instance;
            }
        }

        public static GpuStatusBarViewModel GpuStatusBarVm {
            get {
                return GpuStatusBarViewModel.Instance;
            }
        }
        #endregion
    }
}
