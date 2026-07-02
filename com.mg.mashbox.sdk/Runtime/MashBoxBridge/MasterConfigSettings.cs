using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxBridge.Common.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MashBoxBridge
{
    public enum ForcePullStyle
    {
        CurrentOrSelected,
        ClosestPullable
    }

    [CreateAssetMenu(fileName = "MasterConfigSettings", menuName = "MashBox/MasterConfigSettings")]
    public class MasterConfigSettings : ScriptableObject
    {
        public static MasterConfigSettings ConfigSettings
        {
            get
            {
                if(!_configSettings)
                {
                    _configSettings = Resources.Load<MasterConfigSettings>(Application.productName);
                }
                
                return _configSettings;
            }
        }

        // --- PLATFORM FLAGS SYSTEM ---

        [Flags]
        public enum PlatformFlags
        {
            None = 0,
            Steam = 1 << 0,
            Xbox = 1 << 1,
            PlayStation = 1 << 2,
            All = ~0
        }

        [Serializable]
        public class PlatformFeatureFlag
        {
            public string FeatureName;
            public PlatformFlags EnabledOnPlatforms = PlatformFlags.All;
        }

        [Header("Platform-Specific Feature Flags")]
        [SerializeField] private List<PlatformFeatureFlag> _featureFlags = new();

        public bool IsFeatureEnabled(string featureName)
        {
            var flag = _featureFlags.FirstOrDefault(f => f.FeatureName == featureName);
            if (flag == null) return true; // default to true if not defined

            return (flag.EnabledOnPlatforms & GetCurrentPlatformFlag()) != 0;
        }

        private PlatformFlags GetCurrentPlatformFlag()
        {
            #if UNITY_STANDALONE
            return PlatformFlags.Steam;
            #elif UNITY_GAMECORE || UNITY_XBOXONE || UNITY_GAMECORE_XBOXONE || UNITY_GAMECORE_SCARLETT
            return PlatformFlags.Xbox;
            #elif UNITY_PS4 || UNITY_PS5
            return PlatformFlags.PlayStation;
            #else
            return PlatformFlags.None;
            #endif
        }

        // Optional: Define common feature names as constants to avoid typos
        public static class FeatureNames
        {
            public const string HasMODIO = "HasMODIO";
        }

        // --- END PLATFORM FLAGS SYSTEM ---


        // --- Existing Fields (unchanged, trimmed for brevity) ---

        public Texture2D GameLogo => _gameLogo;
        [Header("Game Logo")]
        [SerializeField] Texture2D _gameLogo;

        // MasterConfigSettings.cs  (add near your other Game Logo fields)
        [Header("Game Pause menu Logo Layout")]
        [SerializeField] private Vector2 _gameLogoPauseMenuSize = new Vector2(256, 256); // width, height in px
        [SerializeField] private Vector2 _gameLogoPauseMenuOffset = Vector2.zero;        // anchoredPosition offset in px

        public Vector2 GameLogoSize  => _gameLogoPauseMenuSize;
        public Vector2 GameLogoOffset => _gameLogoPauseMenuOffset;
        
        public Texture2D GameLogoIcon => _gameLogoIcon;
        [Header("Game Logo Icon")]
        [SerializeField] Texture2D _gameLogoIcon;

        public Texture2D BoxArtBKGBranding => _boxArtBKGBrandingImage;
        [Header("Box Art BKG Branding Image")]
        [SerializeField] Texture2D _boxArtBKGBrandingImage;

        public uint SteamAppID
        {
            get
            {
                if (_steamAppID == 0)
                    throw new System.Exception("SteamAppID has not been set");
                return _steamAppID;
            }
            set { _steamAppID = value; }
        }

        [SerializeField] public string _xboxSCID;
        
        [SerializeField] private uint _steamAppID;

        [Header("Fusion App ID")]
        [SerializeField] private string _fusionAppID;
        public string FusionAppID => _fusionAppID;
            
        
        [Header("Discord URL")]
        [SerializeField] private string _discordURL;
        public string DiscordURL => _discordURL;

        [Header("Map Bundles Location")]
        [SerializeField] private string _mapBundlesLocation = "D:\\Map Bundles";
        public string MapBundlesLocation => _mapBundlesLocation;    
        
        // --- MAP SECURITY / WHITELIST ---

        [Header("Maps Whitelist (Exact folder/file names)")]
        [SerializeField]
        private List<string> _allowedMapNames = new()
        {
            "manifest.json",
            "version_log.json",
            "alpha chuck bailey_map",
            "applewood forest_map",
            "california alpha_map",
            "conservatory_map",
            "jammas skatehouse_map",
            "spillway_map",
            "tits_map",
        };
        
        public IReadOnlyList<string> AllowedMapNames => _allowedMapNames;
        
        public bool IsMapAllowed(string name)
        {
            return _allowedMapNames.Any(
                allowed => string.Equals(
                    allowed,
                    name,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }
        
        [Header("Customization Content Location")]
        [SerializeField] private string _customizationContentLocation = "D:\\BMXStreets_CC\\ServerData";
        public string CustomizationContentLocation => _customizationContentLocation;    

        [Header("Customization Content Location Steam Library")]
        [SerializeField] private string _customizationContentLocationSteamLibrary = "";
        public string CustomizationContentLocationSteamLibrary => _customizationContentLocationSteamLibrary;

        public bool LoadContentFromSteamLibraryEditorMode;
        
        private static MasterConfigSettings _configSettings;
        private static VisualTreeAsset _masterUIVisualTreeAsset;
        public static VisualTreeAsset MasterUIVisualTreeAsset
        {
            get
            {
                if(!_masterUIVisualTreeAsset)
                {
                    _masterUIVisualTreeAsset = Resources.Load<VisualTreeAsset>(Application.productName);
                }
                return _masterUIVisualTreeAsset;
            }
        }

        [Header("Title Screen")]
        [SerializeField] [MashBoxBridge.CustomAttributes.AssetsOnly] private string _titleScreenPath;
        public string TitleScreenPath => _titleScreenPath;

        [Header("Main Menu")]
        [SerializeField] private string _mainMenuScenePath = "Assets/MashBox/Addons/GameLoop/MainMenu/MainMenu.unity";
        public string MainMenu => _mainMenuScenePath;
        
        
        public GameObject ModalPrefab => _modalPrefab;
        [Header("Modal Prefab")]
        [SerializeField] private GameObject _modalPrefab;

        [Header("Content")]
        [SerializeField] private string _contentLoadPath;
        public string ContentLoadPath => _contentLoadPath;

        [Header("Content")]
        [SerializeField] private string _contentCatalogPath;
        public string ContentCatalogPath => _contentCatalogPath;

        [Header("Allow Playfab Login")]
        [SerializeField] private bool _allowPlayfabLogin = true;
        public bool AllowPlayfabLogin => _allowPlayfabLogin;
        
        [Header("Playfab EndPoint URL")]
        [SerializeField] private string _playfabEndPointURL;

        public string PlayfabEndPointURL => _playfabEndPointURL;
        
        public bool HasMODIO => IsFeatureEnabled(FeatureNames.HasMODIO);
        
        [Header("Gameplay Feature")]
        [SerializeField] private bool _hasFreeRunning = true;
        public bool HasFreeRunning => _hasFreeRunning;

        [Header("Force Pull")]
        [SerializeField] private ForcePullStyle _forcePullStyle = ForcePullStyle.CurrentOrSelected;
        public ForcePullStyle ForcePullStyle => _forcePullStyle;
        
        [Header("Has Collect Letters Challenge")]
        [SerializeField] private bool _hasCollectLettersChallenge = true;
        public bool HasCollectLettersChallenge => _hasCollectLettersChallenge;

        public bool HasBeyondMeatSystem = false;
        
        public bool AlwaysTweak => _alwaysTweak;
        [Header("AlwaysTweak")]
        [SerializeField] private bool _alwaysTweak = false;

        public ScriptableObject AirTrickSet => _airTrickSet;
        [Header("Air Trick Set")]
        [SerializeField] private ScriptableObject _airTrickSet;

        public ScriptableObject GrindPoseDataSet => _grindPoseDataSet;
        [Header("Grind Pose Set")]
        [SerializeField] private ScriptableObject _grindPoseDataSet;

        public ScriptableObject CharactersDataList
        {
            get
            {
                if (!Application.isEditor)
                {
#if !UNITY_STANDALONE
                    var list = (IList<UnityEngine.Object>)_charactersDataList;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (list[i] != null && list[i] is ICharacterData characterData)
                        {
                            if (characterData.CharacterName == "Jim Jim")
                            {
                                list.RemoveAt(i);
                            }
                        }
                    }
#endif
                }

                return _charactersDataList;
            }
        }

        [Header("Characters Data List")]
        [SerializeField] private ScriptableObject _charactersDataList;

        public GameObject DefaultSessionMarker => _defaultSessionMarker;
        [Header("DefaultSessionMarker")]
        [SerializeField] private GameObject _defaultSessionMarker;

        public GameObject HUD => _hud;
        [Header("HUD")]
        [SerializeField] private GameObject _hud;
        
        public Object PlayerSpawnPrefab => _playerSpawnPrefab;
        [Header("PlayerSpawn Prefab")]
        [SerializeField] private GameObject _playerSpawnPrefab;
        public GameObject NetworkGameManagerPrefab => _networkGameManagerPrefab;
        [Header("Network Game Manager Prefab")]
        [SerializeField] private GameObject _networkGameManagerPrefab;
            
        public GameObject NetworkVehiclePrefab => _networkVehiclePrefab;
        [Header("Network Vehicle Prefab")] [SerializeField]
        private GameObject _networkVehiclePrefab;

        public GameObject MenuVehiclePrefab => _menuVehiclePrefab;
        [Header("Menu Vehicle Prefab")] [SerializeField]
        private GameObject _menuVehiclePrefab;
        
        [Header("Quick Settings")] [SerializeField]
        private GameObject _quickSettingsPrefab;
        public GameObject QuickSettingsPrefab => _quickSettingsPrefab;
        
        public GameObject LoadingScreenFab => _loadingScreenFab;
        [Header("LoadingScreenFab")]
        [SerializeField] private GameObject _loadingScreenFab;

        public GameObject RaceGate => _raceGate;
        [Header("RaceGate")]
        [SerializeField] private GameObject _raceGate;
        public GameObject CollectiblePrefab => _collectiblePrefab;
        [Header("Collectible Prefab")]
        [SerializeField] private GameObject _collectiblePrefab;
   
        
        public Object XboxSpriteSheet => _xboxSpriteSheet;
        [SerializeField] private Object _xboxSpriteSheet;
        
        public Object PlaystationSpriteSheet => _playstationSpriteSheet;
        [SerializeField] private Object _playstationSpriteSheet;

        public bool RunMusicPlayer = true;
        
        private void OnValidate()
        {
            #if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetString("Map Bundles Location", MapBundlesLocation);
            UnityEditor.EditorPrefs.SetString("Customization Content Location", CustomizationContentLocation);
            AddMashBoxDefine();
            #endif
        }
        
#if UNITY_EDITOR
        private static void AddMashBoxDefine()
        {
#if UNITY_2021_2_OR_NEWER
            var target = UnityEditor.Build.NamedBuildTarget.Standalone;
            var defines = UnityEditor.PlayerSettings.GetScriptingDefineSymbols(target);
#else
            var target = UnityEditor.BuildTargetGroup.Standalone;
            var defines = UnityEditor.PlayerSettings.GetScriptingDefineSymbolsForGroup(target);
#endif

            if (!defines.Contains("MashBoxGameTitle"))
            {
                defines += ";MashBoxGameTitle";
#if UNITY_2021_2_OR_NEWER
                UnityEditor.PlayerSettings.SetScriptingDefineSymbols(target, defines);
#else
                UnityEditor.PlayerSettings.SetScriptingDefineSymbolsForGroup(target, defines);
#endif

                Debug.Log("Added scripting define: MashBoxGameTitle");
            }
        }
#endif
    }
}
