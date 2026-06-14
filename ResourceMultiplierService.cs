using System;
using System.Collections.Generic;
using BepInEx.Logging;
using SHCDESE.API;
using SHCDESE.EventAPI;
using SHCDESE.EventAPI.Buildings;
using SHCDESE.EventAPI.Player;
using SHCDESE.Extensions;
using SHCDESE.GameGlobals;
using SHCDESE.Interop;

namespace AIBuff {
    internal class ResourceMultiplierService {
        private const int ResourceBonusAccumulatorScale = 10000;

        private readonly GamePlayerManagerAPI _playerManager = GamePlayerManagerAPI.Instance;
        private readonly GameBuildingManagerAPI _buildingManager = GameBuildingManagerAPI.Instance;
        private readonly SettingsService _settings;
        private readonly ManualLogSource _logger;

        private readonly Dictionary<(int PlayerId, eGoods Good), int> _trackedResourcesToIgnore = new();
        private readonly Dictionary<(int PlayerId, eGoods Good), int> _resourceBonusRemainders = new();

        public ResourceMultiplierService(SettingsService settings, ManualLogSource logger) {
            _settings = settings;
            _logger = logger;
        }

        public void ClearTrackedResources() {
            _trackedResourcesToIgnore.Clear();
            _resourceBonusRemainders.Clear();
        }

        public unsafe void ModifyStoredGoodsAdded(AddGoodToGoodsyardEventArgs e) {
            if (e.Phase != EventHookPhase.Pre)
                return;

            _settings.ProcessPendingConfigReload();

            if (e.AddAmount <= 0)
                return;

            if (!_buildingManager.TryGetBuildingById(e.BuildingId, out GameBuilding* building))
                return;

            var buildingType = _buildingManager.GetType(e.BuildingId);

            // Extra brewery check because this event is also triggered when hops is added to a brewery for some reason, and I experimentally verified that bonus hops on the brewery lengthens the brewing process (we cant have that).
            if (buildingType.Equals(eStructs.STRUCT_BREWERS_WORKSHOP))
                return;

            // Extra ale check because as of now it is not in the IsGoodsyardGood() check.
            if (!e.Good.IsGoodsyardGood() && !e.Good.Equals(eGoods.STORED_FOOD_ALE) && !e.Good.IsGranaryFood() && !e.Good.IsArmouryGood())
                return;

            int playerId = _buildingManager.GetOwner(e.BuildingId);

            if (!_settings.UsesAIMultipliers(playerId))
                return;

            // This event also tracks resources bought from the marketplace, but we do not want to add multipliers to those.
            int ignoredAmount = ConsumeResourcesToIgnore(playerId, e.Good, e.AddAmount);

            if (ignoredAmount >= e.AddAmount) {
                return;
            }

            int amountToModify = e.AddAmount - ignoredAmount;
            int bonusAmount = GetBonusStoredGoodsAmount(playerId, e.Good, amountToModify, _settings.ResourceMultiplier);

            if (bonusAmount <= 0)
                return;

            try {
                _playerManager.TryAddGood(playerId, e.Good, bonusAmount);
            } catch (Exception ex) {
                _logger.LogError(ex);
            }
        }

        private int ConsumeResourcesToIgnore(int playerId, eGoods good, int amount) {
            if (amount <= 0)
                return 0;

            var key = (playerId, good);

            if (!_trackedResourcesToIgnore.TryGetValue(key, out int pendingAmount))
                return 0;

            int consumedAmount = Math.Min(amount, pendingAmount);
            int remainingAmount = pendingAmount - consumedAmount;

            if (remainingAmount > 0) {
                _trackedResourcesToIgnore[key] = remainingAmount;
            } else {
                _trackedResourcesToIgnore.Remove(key);
            }

            return consumedAmount;
        }

        // Tracks resources bought from the marketplace, added bonus resources, and any other global added resources (if there are any others?)
        public void TrackResourcesToIgnore(PlayerAddResourceEventArgs e) {
            if (e.Phase != EventHookPhase.Pre)
                return;

            if (e.Amount <= 0)
                return;

            var key = (e.PlayerId, e.Good);

            if (_trackedResourcesToIgnore.TryGetValue(key, out int currentAmount)) {
                _trackedResourcesToIgnore[key] = currentAmount + e.Amount;
            } else {
                _trackedResourcesToIgnore[key] = e.Amount;
            }

            return;
        }

        private int GetBonusStoredGoodsAmount(int playerId, eGoods good, int originalAmount, float multiplier) {
            if (originalAmount <= 0)
                return 0;

            float clampedMultiplier = Constants.ClampResourceMultiplier(multiplier);
            double bonusAmountWithRemainder = originalAmount * clampedMultiplier - originalAmount + GetStoredResourceBonusRemainder(playerId, good);

            if (bonusAmountWithRemainder <= 0.0)
                return 0;

            int bonusAmount = (int)Math.Floor(bonusAmountWithRemainder);
            double newRemainder = bonusAmountWithRemainder - bonusAmount;

            StoreResourceBonusRemainder(playerId, good, newRemainder);

            return bonusAmount;
        }

        private double GetStoredResourceBonusRemainder(int playerId, eGoods good) {
            var key = (playerId, good);

            if (!_resourceBonusRemainders.TryGetValue(key, out int storedRemainder))
                return 0.0;

            return storedRemainder / (double)ResourceBonusAccumulatorScale;
        }

        private void StoreResourceBonusRemainder(int playerId, eGoods good, double remainder) {
            var key = (playerId, good);
            // Store remainders as integers, not float, because accumulating floating point operations may be unreliable and lead to desync issues in multiplayer
            int scaledRemainder = (int)Math.Round(remainder * ResourceBonusAccumulatorScale);

            if (scaledRemainder > 0) {
                _resourceBonusRemainders[key] = scaledRemainder;
            } else {
                _resourceBonusRemainders.Remove(key);
            }
        }
    }
}
