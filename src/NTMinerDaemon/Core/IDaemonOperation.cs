﻿using NTMiner.Core.Daemon;
using NTMiner.Core.MinerClient;
using System.Collections.Generic;

namespace NTMiner.Core {
    public interface IDaemonOperation {
        bool IsNTMinerOpened();
        void CloseDaemon();
        ResponseBase SwitchRadeonGpu(bool on);
        string GetSelfWorkLocalJson();
        bool SaveSelfWorkLocalJson(WorkRequest request);
        string GetGpuProfilesJson();
        bool SaveGpuProfilesJson(string json);
        bool SetAutoBootStart(bool autoBoot, bool autoStart);
        ResponseBase StartMine(WorkRequest request);
        ResponseBase StopMine();
        ResponseBase UpgradeNTMiner(UpgradeNTMinerRequest request);
        ResponseBase SetVirtualMemory(Dictionary<string, int> data);
        ResponseBase SetLocalIps(List<LocalIpInput> data);
    }
}