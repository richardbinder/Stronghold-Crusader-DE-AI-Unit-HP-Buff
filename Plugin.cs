using System;
using BepInEx;
using R3;
using SHCDESE.API;
using SHCDESE.API.Components.SaveData;
using SHCDESE.API.LowLevel;
using SHCDESE.EventAPI;
using SHCDESE.EventAPI.Buildings;
using SHCDESE.EventAPI.MapLoader;
using SHCDESE.EventAPI.Player;
using SHCDESE.EventAPI.Units;

namespace AIUnitBuff {
    [BepInDependency("000shcdese", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin("AIUnitBuff", "AI Unit Buff", "1.0.0")]
    public class Plugin : BaseUnityPlugin {
        private LobbySettingsModel _lobbySettings;
        private SettingsService _settings;
        private UnitDamageService _unitDamageService;
        private ResourceMultiplierService _resourceMultiplierService;

        private void Awake() {
            _settings = new SettingsService(Config, Logger);
            _unitDamageService = new UnitDamageService(_settings, Logger);
            _resourceMultiplierService = new ResourceMultiplierService(_settings, Logger);

            Logger.LogInfo("AI Unit Buff loaded.");

            CrusaderLibrary.Instance.LibraryLoaded += OnLibraryLoaded;

            MapLoaderR3EventHooks.OnStartMap.Observable.Subscribe(OnMapStart);
            MapLoaderR3EventHooks.OnUnloadMap.Observable.Subscribe(OnMapUnload);

            ModSaveDataAPI.Instance.RegisterModDataHandler(
                modIdentifier: "AIUnitBuff-savedata",
                saveCallback: OnSave,
                loadCallback: OnLoad
            );
        }

        private void OnLibraryLoaded(IntPtr moduleHandle, ReadOnlySpan<byte> memory) {
            _lobbySettings = new LobbySettingsModel();

            GameXAMLManagerAPI.Instance.RegisterLobbyModSettings(
                plugin: this,
                modName: "AIUnitBuff",
                viewModel: _lobbySettings,
                xamlSourceFile: "LobbySettings.xaml"
            );

            UnitR3EventHooks.OnUnitTakeMeleeDamage.Observable.Subscribe(_unitDamageService.ModifyMeleeDamage);
            UnitR3EventHooks.OnUnitTakeProjectileDamageEx.Observable.Subscribe(_unitDamageService.ModifyProjectileDamage);

            BuildingR3EventHooks.OnGoodsyardAddGood.Observable.Subscribe(_resourceMultiplierService.ModifyStoredGoodsAdded);
            PlayerR3EventHooks.OnPlayerAddResource.Observable.Subscribe(_resourceMultiplierService.TrackResourcesToIgnore);
            //PlayerR3EventHooks.OnPlayerCalculateTaxes.Observable.Subscribe(TripleCalculatedTaxIncome);

            Logger.LogInfo($"Lobby multipliers loaded: {_lobbySettings.EffectiveHpMultiplier}, damage multiplier loaded: {_lobbySettings.EffectiveDmgMultiplier}, resource multiplier loaded: {_lobbySettings.EffectiveResourceMultiplier}");
        }

        // Does not work for now, revisit this when we have a more concrete tax hook
        /*
        private void TripleCalculatedTaxIncome(PlayerCalculateTaxesEventArgs e) {
            if (e.Phase != EventHookPhase.Pre)
                return;

            Logger.LogInfo($"Month: {GameTimeManagerAPI.Instance.GetCurrentMonth()}, Player: {e.PlayerId}, TaxesMode: {e.TaxesMode}, Population: {e.Population}, ReturnValue: {e.ReturnValue}");

            if (e.ReturnValue <= 0)
                return;

            PlayerManager.AddPlayerGold(e.PlayerId, e.ReturnValue * 2);
        }
        */

        private void OnMapStart(MapStartEventArgs e) {
            _resourceMultiplierService.ClearTrackedResources();
            _settings.InitFromLobby(_lobbySettings.EffectiveHpMultiplier, _lobbySettings.EffectiveDmgMultiplier, _lobbySettings.EffectiveResourceMultiplier);

            Logger.LogDebug(
                $"Initialized data on map start. HP multiplier={_settings.SavedHpMultiplier}, damage multiplier={_settings.SavedDmgMultiplier}, resource multiplier={_settings.SavedResourceMultiplier}"
            );
        }

        private void OnMapUnload(MapUnloadEventArgs e) {
            _resourceMultiplierService.ClearTrackedResources();
            _settings.Reset();

            Logger.LogDebug("Reset save data on map unload.");
        }

        private byte[] OnSave(SaveContext context) {
            if (!context.IsSaveFile)
                return null;

            byte[] bytes = _settings.Serialize();

            Logger.LogDebug($"Saving: HP multiplier={_settings.SavedHpMultiplier}, damage multiplier={_settings.SavedDmgMultiplier}, resource multiplier={_settings.SavedResourceMultiplier}");

            return bytes;
        }

        private void OnLoad(byte[] bytes, LoadContext context) {
            if (!context.IsSaveFile)
                return;

            _settings.Deserialize(bytes);

            Logger.LogDebug($"Loaded: HP multiplier={_settings.SavedHpMultiplier}, damage multiplier={_settings.SavedDmgMultiplier}, resource multiplier={_settings.SavedResourceMultiplier}");
        }
    }
}
