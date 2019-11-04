﻿using NTMiner.Vms;
using System;
using System.Windows.Controls;

namespace NTMiner.Views.Ucs {
    public partial class StateBar : UserControl {
        private StateBarViewModel Vm {
            get {
                return (StateBarViewModel)this.DataContext;
            }
        }

        public StateBar() {
            InitializeComponent();
            if (WpfUtil.IsInDesignMode) {
                return;
            }
            this.RunOneceOnLoaded((window) => {
                window.Activated += (object sender, EventArgs e) => {
                    Vm.OnPropertyChanged(nameof(Vm.IsAutoAdminLogon));
                    Vm.OnPropertyChanged(nameof(Vm.AutoAdminLogonToolTip));
                    Vm.OnPropertyChanged(nameof(Vm.IsRemoteDesktopEnabled));
                    Vm.OnPropertyChanged(nameof(Vm.RemoteDesktopToolTip));
                };
                window.WindowContextEventPath<LocalIpSetRefreshedEvent>("本机IP集刷新后刷新状态栏", LogEnum.DevConsole,
                    action: message => {
                        UIThread.Execute(()=> Vm.RefreshLocalIps());
                    });
                window.WindowContextEventPath<MinutePartChangedEvent>("时间的分钟部分变更过更新计时器显示", LogEnum.None,
                    action: message => {
                        UIThread.Execute(() => {
                            Vm.UpdateDateTime();
                        });
                    });
                window.WindowContextEventPath<Per1SecondEvent>("挖矿计时秒表", LogEnum.None,
                    action: message => {
                        UIThread.Execute(() => {
                            DateTime now = DateTime.Now;
                            Vm.UpdateBootTimeSpan(now - NTMinerRoot.Instance.CreatedOn);
                            var mineContext = NTMinerRoot.Instance.CurrentMineContext;
                            if (mineContext != null) {
                                Vm.UpdateMineTimeSpan(now - mineContext.CreatedOn);
                            }
                        });
                    });
                window.WindowContextEventPath<AppVersionChangedEvent>("发现了服务端新版本", LogEnum.DevConsole,
                    action: message => {
                        UIThread.Execute(() => {
                            Vm.SetCheckUpdateForeground(isLatest: MainAssemblyInfo.CurrentVersion >= NTMinerRoot.ServerVersion);
                        });
                    });
                window.WindowContextEventPath<KernelSelfRestartedEvent>("内核自我重启时刷新计数器", LogEnum.DevConsole,
                    action: message => {
                        UIThread.Execute(() => {
                            Vm.OnPropertyChanged(nameof(Vm.KernelSelfRestartCountText));
                        });
                    });
                window.WindowContextEventPath<MineStartedEvent>("挖矿开始后将内核自我重启计数清零", LogEnum.DevConsole,
                    action: message => {
                        UIThread.Execute(() => {
                            Vm.OnPropertyChanged(nameof(Vm.KernelSelfRestartCountText));
                        });
                    });
            });
            var gpuSet = NTMinerRoot.Instance.GpuSet;
            // 建议每张显卡至少对应4G虚拟内存，否则标红
            if (NTMinerRoot.OSVirtualMemoryMb < gpuSet.Count * 4) {
                BtnShowVirtualMemory.Foreground = WpfUtil.RedBrush;
            }
        }
    }
}
