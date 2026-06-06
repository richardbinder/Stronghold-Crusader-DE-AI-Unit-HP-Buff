using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Windows.Documents;
using BepInEx;
using MessagePack;
using R3;
using SHCDESE.API;
using SHCDESE.API.Components.SaveData;
using SHCDESE.API.LowLevel;
using SHCDESE.EventAPI;
using SHCDESE.EventAPI.MapLoader;
using SHCDESE.EventAPI.Tribes;
using SHCDESE.EventAPI.Units;
using SHCDESE.Interop;

namespace AIHPBoost {
    [BepInDependency("000shcdese", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin("AIUnitHPBuff", "AI Unit HP Buff", "1.0.0")]
    public class Plugin : BaseUnityPlugin {
        private static readonly HashSet<eChimps> CivlianTypes =
        [
            eChimps.CHIMP_TYPE_PEASANT,
                eChimps.CHIMP_TYPE_WOODCUTTER,
                eChimps.CHIMP_TYPE_FLETCHER,
                eChimps.CHIMP_TYPE_HUNTER,
                eChimps.CHIMP_TYPE_QUARRY_MASON,
                eChimps.CHIMP_TYPE_QUARRY_GRUNT,
                eChimps.CHIMP_TYPE_QUARRY_OX,
                eChimps.CHIMP_TYPE_PITCHMAN,
                eChimps.CHIMP_TYPE_FARMER_WHEAT,
                eChimps.CHIMP_TYPE_FARMER_HOPS,
                eChimps.CHIMP_TYPE_FARMER_APPLE,
                eChimps.CHIMP_TYPE_FARMER_CATTLE,
                eChimps.CHIMP_TYPE_MILLER,
                eChimps.CHIMP_TYPE_BAKER,
                eChimps.CHIMP_TYPE_BREWER,
                eChimps.CHIMP_TYPE_POLETURNER,
                eChimps.CHIMP_TYPE_BLACKSMITH,
                eChimps.CHIMP_TYPE_ARMOURER,
                eChimps.CHIMP_TYPE_TANNER,
                eChimps.CHIMP_TYPE_INNKEEPER,
                eChimps.CHIMP_TYPE_FIREMAN,
                eChimps.CHIMP_TYPE_MINER1,
                eChimps.CHIMP_TYPE_MINER2,
                eChimps.CHIMP_TYPE_TRADER,
                eChimps.CHIMP_TYPE_TRADER_HORSE
        ];

        private AIHpLobbySettings _lobbySettings;
        private SaveData _saveData = new SaveData();

        private void Awake() {
            Logger.LogInfo("AI Unit HP Buff loaded.");
            CrusaderLibrary.Instance.LibraryLoaded += OnLibraryLoaded;
            MapLoaderR3EventHooks.OnStartMap.Observable.Subscribe(OnMapStart);
            MapLoaderR3EventHooks.OnUnloadMap.Observable.Subscribe(OnMapUnload);
            ModSaveDataAPI.Instance.RegisterModDataHandler(
                modIdentifier: "AIUnitHPBuff-savedata",
                saveCallback: OnSave,
                loadCallback: OnLoad
            );
        }

        private void OnLibraryLoaded(IntPtr moduleHandle, ReadOnlySpan<byte> memory) {
            _lobbySettings = new AIHpLobbySettings();

            GameXAMLManagerAPI.Instance.RegisterLobbyModSettings(
                plugin: this,
                modName: "AIUnitHPBuff",
                viewModel: _lobbySettings,
                xamlSourceFile: "LobbySettings.xaml"
            );

            Logger.LogInfo($"Lobby HP multiplier loaded: {_lobbySettings.HpMultiplierPercent}%");

            UnitR3EventHooks.OnUnitCreate.Observable.Subscribe(OnUnitCreated);

            // This event is always called when a unit is being reassigned to a new unit group, which in theory should always happen when a new unit is built.
            // Unfortunately, we don't have a "unit built" event yet so this is a workaround.
            TribeR3EventHooks.OnTribeAssignUnit.Observable.Subscribe(OnUnitTribeAssigned);
        }

        private void OnUnitCreated(UnitCreateEventArgs e) {
            if (e.Phase == EventHookPhase.Pre)
                return;

            int unitId = (int)e.ReturnValue;

            try {
                TryBuffAiUnit(unitId, "UNIT_CREATED_HOOK");
            } catch (Exception ex) {
                Logger.LogError(ex);
            }
        }

        private void OnUnitTribeAssigned(TribeAssignUnitEventArgs e) {
            if (e.Phase == EventHookPhase.Pre)
                return;

            int unitId = (int)e.UnitId;

            // We need to wait a bit after the unit is built so that its information updates before we can change its HP
            GameTimeManagerAPI.Instance.GetTimerEngine().AddDelayedAction(1000, action: () => {
                try {
                    TryBuffAiUnit(unitId, "TRIBE_DELAYED_HOOK");
                } catch (Exception ex) {
                    Logger.LogError(ex);
                }
            }, null);
        }

        private void OnMapStart(MapStartEventArgs e) {
            _saveData = new SaveData();
            _saveData.Multiplier = _lobbySettings.HpMultiplierPercent;
            Logger.LogDebug("Initialized new Save Data on map start.");
        }

        private void OnMapUnload(MapUnloadEventArgs e) {
            _saveData = null;
            Logger.LogDebug("Reset Save Data on map unload.");
        }

        private byte[] OnSave(SaveContext context) {
            if (!context.IsSaveFile)
                return null;

            return MessagePackSerializer.Serialize(_saveData);
        }

        private void OnLoad(byte[] bytes, LoadContext context) {
            _saveData = MessagePackSerializer.Deserialize<SaveData>(bytes);

            Logger.LogDebug($"Loaded: multiplier={_saveData.Multiplier}, computedMaxHP={_saveData.GetComputeMaxHpReadableString()}");
        }

        private unsafe void TryBuffAiUnit(int unitId, string source) {
            var unitManager = GameUnitManagerAPI.Instance;
            var playerManager = GamePlayerManagerAPI.Instance;

            if (!unitManager.TryGetUnitById(unitId, out GameUnit* unit))
                return;

            int owner = unitManager.GetOwner(unitId);
            if (!playerManager.IsAIPlayer(owner))
                return;

            eChimps type = unitManager.GetType(unitId);

            if (CivlianTypes.Contains(type))
                return;

            int maxHp = unitManager.GetMaxHealth(unitId);
            int hp = unitManager.GetCurrentHealth(unitId);

            var key = (type, owner);

            int targetMaxHp = _saveData.ComputedMaxHp.GetOrAdd(key, _ => {

                return (int)Math.Min(
                    100_000_000,
                    (long)maxHp * _saveData.Multiplier / 100
                );
            });

            if (maxHp == targetMaxHp) {
                Logger.LogDebug($"{source} skipped already buffed unit: UnitId={unitId}, type={type}, Owner={owner}, HP={hp}/{maxHp}");
                return;
            }

            unitManager.SetMaxHealth(unitId, targetMaxHp);
            unitManager.SetCurrentHealth(unitId, targetMaxHp - 1);

            int newHp = unitManager.GetCurrentHealth(unitId);
            int newMaxHp = unitManager.GetMaxHealth(unitId);

            Logger.LogDebug($"{source} changed unit HP: UnitId={unitId}, type={type}, Owner={owner}, HP={hp}/{maxHp} -> {newHp}/{newMaxHp}");
        }
    }
    
    [MessagePackObject(true)]
    public class SaveData {
        public ConcurrentDictionary<(eChimps Type, int Owner), int> ComputedMaxHp { get; set; } = new ConcurrentDictionary<(eChimps Type, int Owner), int>();
        public int Multiplier { get; set; } = 100;

        public String GetComputeMaxHpReadableString() {
            String str = String.Empty;
            foreach (var kvp in ComputedMaxHp) {
                str += $"{kvp.Key}={kvp.Value} ";
            }

            return str;
        }
    }
    
}