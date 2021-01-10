using NTMiner.Core.MinerServer;
using NTMiner.Vms;
using System;
using System.Windows.Input;

namespace NTMiner.MinerStudio.Vms {
    public class MinerClientSettingViewModel : ViewModelBase {
        public readonly Guid Id = Guid.NewGuid();
        private bool _isAutoBoot;
        private bool _isAutoStart;

        public ICommand Save { get; private set; }

        [Obsolete(message: NTKeyword.WpfDesignOnly, error: true)]
        public MinerClientSettingViewModel() {
            if (!WpfUtil.IsInDesignMode) {
                throw new InvalidProgramException(NTKeyword.WpfDesignOnly);
            }
        }

        public MinerClientSettingViewModel(MinerClientViewModel[] minerClients) {
            if (minerClients != null && minerClients.Length == 1) {
                _isAutoBoot = minerClients[0].IsAutoBoot;
                _isAutoStart = minerClients[0].IsAutoStart;
            }
            this.Save = new DelegateCommand(() => {
                if (minerClients != null && minerClients.Length != 0) {
                    foreach (var item in minerClients) {
                    }
                }
                VirtualRoot.Execute(new CloseWindowCommand(this.Id));
            });
        }
    }
}
