﻿using NTMiner.Controllers;
using NTMiner.Core.MinerServer;
using System;

namespace NTMiner.Services.Official {
    public class CoinSnapshotService {
        private readonly string _controllerName = RpcRoot.GetControllerName<ICoinSnapshotController>();

        internal CoinSnapshotService() {
        }

        #region GetLatestSnapshotsAsync
        public void GetLatestSnapshotsAsync(int limit, Action<GetCoinSnapshotsResponse, Exception> callback) {
            GetCoinSnapshotsRequest request = new GetCoinSnapshotsRequest {
                Limit = limit
            };
            RpcRoot.JsonRpc.SignPostAsync(RpcRoot.OfficialServerHost, RpcRoot.OfficialServerPort, _controllerName, nameof(ICoinSnapshotController.LatestSnapshots), data: request, callback);
        }
        #endregion
    }
}
