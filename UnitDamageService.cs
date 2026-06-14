using System;
using BepInEx.Logging;
using SHCDESE.API;
using SHCDESE.EventAPI;
using SHCDESE.EventAPI.Units;
using SHCDESE.Interop;

namespace AIBuff {
    internal class UnitDamageService {
        private readonly GameUnitManagerAPI _unitManager = GameUnitManagerAPI.Instance;
        private readonly GamePlayerManagerAPI _playerManager = GamePlayerManagerAPI.Instance;
        private readonly GameProjectileManagerAPI _projectileManager = GameProjectileManagerAPI.Instance;
        private readonly SettingsService _settings;
        private readonly ManualLogSource _logger;

        public UnitDamageService(SettingsService settings, ManualLogSource logger) {
            _settings = settings;
            _logger = logger;
        }

        public void ModifyMeleeDamage(UnitTakeDamageByMeleeEventArgs e) {
            if (e.Phase != EventHookPhase.Pre)
                return;

            try {
                e.Damage = GetModifiedMeleeDamage(e.AttackingUnitId, e.DamagedUnitId, e.Damage);
            } catch (Exception ex) {
                _logger.LogError(ex);
            }
        }

        public void ModifyProjectileDamage(UnitTakeDamageByProjectileExEventArgs e) {
            if (e.Phase != EventHookPhase.Pre)
                return;

            try {
                e.Damage = GetModifiedProjectileDamage(e.AttackingUnitId, e.AttackedUnitId, e.ProjectileId, e.Damage);
            } catch (Exception ex) {
                _logger.LogError(ex);
            }
        }

        private unsafe int GetModifiedMeleeDamage(int attackingUnit, int defendingUnit, int damage) {
            _settings.ProcessPendingConfigReload();

            if (!TryGetUnitDamageContext(defendingUnit, out eChimps defendingUnitType, out float defenderHpMultiplier))
                return damage;

            if (Constants.CivilianTypes.Contains(defendingUnitType))
                return damage;

            if (!_unitManager.TryGetUnitById(attackingUnit, out GameUnit* attackingGameUnit))
                return damage;

            int attackingUnitOwner = _unitManager.GetOwner(attackingUnit);
            float attackerDmgMultiplier = GetUnitDamageMultiplier(attackingUnitOwner);
            eChimps attackingUnitType = _unitManager.GetType(attackingUnit);

            if (damage == 0) {
                damage = _unitManager.GetMeleeDamageFromTo(attackingUnitType, defendingUnitType);
            }

            return ApplyDamageMultipliers(damage, defenderHpMultiplier, attackerDmgMultiplier);
        }

        private unsafe int GetModifiedProjectileDamage(int attackingUnit, int defendingUnit, int projectile, int damage) {
            _settings.ProcessPendingConfigReload();

            if (!TryGetUnitDamageContext(defendingUnit, out eChimps defendingUnitType, out float defenderHpMultiplier))
                return damage;

            if (Constants.CivilianTypes.Contains(defendingUnitType))
                return damage;

            float attackerDmgMultiplier = Constants.DefaultDmgMultiplier;

            if (_unitManager.TryGetUnitById(attackingUnit, out GameUnit* attackingGameUnit)) {
                int attackingUnitOwner = _unitManager.GetOwner(attackingUnit);
                attackerDmgMultiplier = GetUnitDamageMultiplier(attackingUnitOwner);
            } else if (_projectileManager.TryGetProjectileById(projectile, out GameProjectile* proj)) {
                int attackingUnitOwner = _projectileManager.GetSourcePlayer(projectile);
                attackerDmgMultiplier = GetUnitDamageMultiplier(attackingUnitOwner);
            }

            return ApplyDamageMultipliers(damage, defenderHpMultiplier, attackerDmgMultiplier);
        }

        private unsafe bool TryGetUnitDamageContext(int unitId, out eChimps unitType, out float hpMultiplier) {
            unitType = default;
            hpMultiplier = Constants.DefaultHpMultiplier;

            if (!_unitManager.TryGetUnitById(unitId, out GameUnit* unit))
                return false;

            int owner = _unitManager.GetOwner(unitId);
            unitType = _unitManager.GetType(unitId);
            hpMultiplier = GetUnitHpMultiplier(owner);

            return true;
        }

        private int ApplyDamageMultipliers(int damage, float defenderHpMultiplier, float attackerDmgMultiplier) {
            if (damage <= 0)
                return damage;

            float finalDmgMultiplier = 1.0f / defenderHpMultiplier * attackerDmgMultiplier;
            return Math.Max(1, (int)(damage * finalDmgMultiplier));
        }

        private float GetUnitDamageMultiplier(int ownerId) {
            if (_settings.UsesAIMultipliers(ownerId)) {
                return _settings.DmgMultiplier;
            } else {
                return Constants.DefaultDmgMultiplier;
            }
        }

        private float GetUnitHpMultiplier(int ownerId) {
            if (_settings.UsesAIMultipliers(ownerId)) {
                return _settings.HpMultiplier;
            } else {
                return Constants.DefaultHpMultiplier;
            }
        }
    }
}
