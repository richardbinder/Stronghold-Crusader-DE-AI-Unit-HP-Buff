using System;
using SHCDESE.API.Components.Network;
using SHCDESE.ViewModels;

namespace AIHPBoost
{
    public class AIHpLobbySettings : LobbyModSettingsBaseViewModel
    {
        private int _hpMultiplierPercent = 100;

        [SyncHostOnly]
        public int HpMultiplierPercent
        {
            get => _hpMultiplierPercent;
            set
            {
                int clamped = Math.Max(1, Math.Min(10000, value));

                if (_hpMultiplierPercent == clamped)
                    return;

                _hpMultiplierPercent = clamped;
                OnPropertyChanged(nameof(HpMultiplierPercent));
            }
        }
    }
}