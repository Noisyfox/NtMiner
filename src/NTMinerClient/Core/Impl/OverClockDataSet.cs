﻿using NTMiner.Core.MinerServer;
using NTMiner.Gpus;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NTMiner.Core.Impl {
    public class OverClockDataSet : IOverClockDataSet {
        private readonly Dictionary<Guid, OverClockData> _dicById = new Dictionary<Guid, OverClockData>();
        private readonly INTMinerContext _root;

        public OverClockDataSet(INTMinerContext root) {
            _root = root;
            VirtualRoot.BuildCmdPath<AddOverClockDataCommand>(path: (message) => {
                if (message == null || message.Input == null || message.Input.GetId() == Guid.Empty) {
                    throw new ArgumentNullException();
                }
                if (string.IsNullOrEmpty(message.Input.Name)) {
                    throw new ValidationException("OverClockData name can't be null or empty");
                }
                if (_dicById.ContainsKey(message.Input.GetId())) {
                    return;
                }
                OverClockData entity = new OverClockData().Update(message.Input);
                RpcRoot.OfficialServer.OverClockDataService.AddOrUpdateOverClockDataAsync(entity, (response, e) => {
                    if (response.IsSuccess()) {
                        _dicById.Add(entity.Id, entity);
                        VirtualRoot.RaiseEvent(new OverClockDataAddedEvent(message.MessageId, entity));
                    }
                    else {
                        VirtualRoot.Out.ShowError(response.ReadMessage(e), autoHideSeconds: 4);
                    }
                });
            }, location: this.GetType());
            VirtualRoot.BuildCmdPath<UpdateOverClockDataCommand>(path: (message) => {
                if (message == null || message.Input == null || message.Input.GetId() == Guid.Empty) {
                    throw new ArgumentNullException();
                }
                if (string.IsNullOrEmpty(message.Input.Name)) {
                    throw new ValidationException("minerGroup name can't be null or empty");
                }
                if (!_dicById.TryGetValue(message.Input.GetId(), out OverClockData entity)) {
                    return;
                }
                OverClockData oldValue = new OverClockData().Update(entity);
                entity.Update(message.Input);
                RpcRoot.OfficialServer.OverClockDataService.AddOrUpdateOverClockDataAsync(entity, (response, e) => {
                    if (!response.IsSuccess()) {
                        entity.Update(oldValue);
                        VirtualRoot.RaiseEvent(new OverClockDataUpdatedEvent(message.MessageId, entity));
                        VirtualRoot.Out.ShowError(response.ReadMessage(e), autoHideSeconds: 4);
                    }
                });
                VirtualRoot.RaiseEvent(new OverClockDataUpdatedEvent(message.MessageId, entity));
            }, location: this.GetType());
            VirtualRoot.BuildCmdPath<RemoveOverClockDataCommand>(path: (message) => {
                if (message == null || message.EntityId == Guid.Empty) {
                    throw new ArgumentNullException();
                }
                if (!_dicById.ContainsKey(message.EntityId)) {
                    return;
                }
                OverClockData entity = _dicById[message.EntityId];
                RpcRoot.OfficialServer.OverClockDataService.RemoveOverClockDataAsync(entity.Id, (response, e) => {
                    if (response.IsSuccess()) {
                        _dicById.Remove(entity.Id);
                        VirtualRoot.RaiseEvent(new OverClockDataRemovedEvent(message.MessageId, entity));
                    }
                    else {
                        VirtualRoot.Out.ShowError(response.ReadMessage(e), autoHideSeconds: 4);
                    }
                });
            }, location: this.GetType());
        }

        private bool _isInited = false;
        private void Init() {
            if (_isInited) {
                return;
            }
            _isInited = true;
            RpcRoot.OfficialServer.OverClockDataService.GetOverClockDatasAsync((response, e) => {
                if (response.IsSuccess()) {
                    IEnumerable<OverClockData> query;
                    if (_root.GpuSet.GpuType == GpuType.Empty) {
                        query = response.Data;
                    }
                    else {
                        query = response.Data.Where(a => a.GpuType == _root.GpuSet.GpuType);
                    }
                    foreach (var item in query) {
                        if (!_dicById.ContainsKey(item.GetId())) {
                            _dicById.Add(item.GetId(), item);
                        }
                    }
                }
                VirtualRoot.RaiseEvent(new OverClockDataSetInitedEvent());
            });
        }

        public bool TryGetOverClockData(Guid id, out IOverClockData data) {
            Init();
            var r = _dicById.TryGetValue(id, out OverClockData temp);
            data = temp;
            return r;
        }

        public IEnumerable<IOverClockData> AsEnumerable() {
            Init();
            return _dicById.Values;
        }
    }
}
