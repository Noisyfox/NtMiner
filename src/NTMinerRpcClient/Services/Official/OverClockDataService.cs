﻿using NTMiner.Controllers;
using NTMiner.Core.MinerServer;
using System;
using System.Collections.Generic;

namespace NTMiner.Services.Official {
    public class OverClockDataService {
        private readonly string _controllerName = RpcRoot.GetControllerName<IOverClockDataController>();

        internal OverClockDataService() {
        }

        #region GetOverClockDatasAsync
        public void GetOverClockDatasAsync(Action<DataResponse<List<OverClockData>>, Exception> callback) {
            object request = new object();
            RpcRoot.JsonRpc.PostAsync(RpcRoot.OfficialServerHost, RpcRoot.OfficialServerPort, _controllerName, nameof(IOverClockDataController.OverClockDatas), request, callback);
        }
        #endregion

        #region AddOrUpdateOverClockDataAsync
        public void AddOrUpdateOverClockDataAsync(OverClockData entity, Action<ResponseBase, Exception> callback) {
            DataRequest<OverClockData> request = new DataRequest<OverClockData>() {
                Data = entity
            };
            RpcRoot.JsonRpc.SignPostAsync(RpcRoot.OfficialServerHost, RpcRoot.OfficialServerPort, _controllerName, nameof(IOverClockDataController.AddOrUpdateOverClockData), data: request, callback);
        }
        #endregion

        #region RemoveOverClockDataAsync
        public void RemoveOverClockDataAsync(Guid id, Action<ResponseBase, Exception> callback) {
            DataRequest<Guid> request = new DataRequest<Guid>() {
                Data = id
            };
            RpcRoot.JsonRpc.SignPostAsync(RpcRoot.OfficialServerHost, RpcRoot.OfficialServerPort, _controllerName, nameof(IOverClockDataController.RemoveOverClockData), data: request, callback);
        }
        #endregion
    }
}
