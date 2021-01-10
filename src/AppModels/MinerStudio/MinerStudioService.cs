﻿using NTMiner.Core.MinerClient;
using NTMiner.Core.MinerServer;
using NTMiner.MinerStudio.Impl;
using System;
using System.Collections.Generic;

namespace NTMiner.MinerStudio {
    public class MinerStudioService : IMinerStudioService {
        public static MinerStudioService Instance { get; private set; } = new MinerStudioService();

        public readonly LocalMinerStudioService LocalMinerStudioService = new LocalMinerStudioService();
        private readonly ServerMinerStudioService _serverMinerStudioService = new ServerMinerStudioService();

        private MinerStudioService() { }

        private IMinerStudioService Service {
            get {
                if (RpcRoot.IsOuterNet) {
                    return _serverMinerStudioService;
                }
                else {
                    return LocalMinerStudioService;
                }
            }
        }

        public void GetLatestSnapshotsAsync(int limit, Action<GetCoinSnapshotsResponse, Exception> callback) {
            Service.GetLatestSnapshotsAsync(limit, callback);
        }

        public void QueryClientsAsync(QueryClientsRequest query) {
            Service.QueryClientsAsync(query);
        }

        public void UpdateClientAsync(string objectId, string propertyName, object value, Action<ResponseBase, Exception> callback) {
            Service.UpdateClientAsync(objectId, propertyName, value, callback);
        }

        public void UpdateClientsAsync(string propertyName, Dictionary<string, object> values, Action<ResponseBase, Exception> callback) {
            Service.UpdateClientsAsync(propertyName, values, callback);
        }

        public void RemoveClientsAsync(List<string> objectIds, Action<ResponseBase, Exception> callback) {
            Service.RemoveClientsAsync(objectIds, callback);
        }

        public void GetConsoleOutLinesAsync(IMinerData client, long afterTime) {
            Service.GetConsoleOutLinesAsync(client, afterTime);
        }

        public void GetLocalMessagesAsync(IMinerData client, long afterTime) {
            Service.GetLocalMessagesAsync(client, afterTime);
        }

        public void SwitchRadeonGpuAsync(IMinerData client, bool on) {
            Service.SwitchRadeonGpuAsync(client, on);
        }

        public void StartMineAsync(IMinerData client, Guid workId) {
            Service.StartMineAsync(client, workId);
        }

        public void StopMineAsync(IMinerData client) {
            Service.StopMineAsync(client);
        }

        public void UpgradeNTMinerAsync(IMinerData client, string ntminerFileName) {
            Service.UpgradeNTMinerAsync(client, ntminerFileName);
        }

        public void GetDrivesAsync(IMinerData client) {
            Service.GetDrivesAsync(client);
        }

        public void SetVirtualMemoryAsync(IMinerData client, Dictionary<string, int> data) {
            Service.SetVirtualMemoryAsync(client, data);
        }

        public void GetLocalIpsAsync(IMinerData client) {
            Service.GetLocalIpsAsync(client);
        }

        public void SetLocalIpsAsync(IMinerData client, List<LocalIpInput> data) {
            Service.SetLocalIpsAsync(client, data);
        }

        public void GetOperationResultsAsync(IMinerData client, long afterTime) {
            Service.GetOperationResultsAsync(client, afterTime);
        }

        public void GetSelfWorkLocalJsonAsync(IMinerData client) {
            Service.GetSelfWorkLocalJsonAsync(client);
        }

        public void SaveSelfWorkLocalJsonAsync(IMinerData client, string localJson, string serverJson) {
            Service.SaveSelfWorkLocalJsonAsync(client, localJson, serverJson);
        }

        public void GetGpuProfilesJsonAsync(IMinerData client) {
            Service.GetGpuProfilesJsonAsync(client);
        }

        public void SaveGpuProfilesJsonAsync(IMinerData client, string json) {
            Service.SaveGpuProfilesJsonAsync(client, json);
        }
    }
}
