#if MashBoxDev

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Dev
{
    /// <summary>
    /// Generates Unity Gaming Services leaderboard authoring files plus a MashBox
    /// metadata manifest. This is editor-only and is excluded unless MashBoxDev is defined.
    /// </summary>
    public sealed class LeaderboardGeneratorWindow : EditorWindow
    {
        private enum ScoreType
        {
            RaceTimeLowestWins,
            HighScoreHighestWins
        }

        private enum UpdateStrategy
        {
            Best,
            Latest,
            Total
        }

        private enum ActivityType
        {
            Race,
            ScoreChallenge,
            PvpMatch,
            Other
        }

        private enum AvailabilityScope
        {
            Map = 1,
            Global = 0
        }

        private enum Period
        {
            Daily,
            Weekly,
            Monthly,
            AllTime
        }

        private const string DefaultOutputFolder = "Assets/UGS/Leaderboards";
        private const string RuntimeCatalogFileName = "ProjectXLeaderboardCatalog.json";
        private const string SchemaUrl = "https://ugs-config-schemas.unity3d.com/v1/leaderboards.schema.json";
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        [SerializeField] private string _displayName = "Goats Gully";
        [SerializeField] private string _baseId = "GoatsGully";
        [SerializeField] private ScoreType _scoreType = ScoreType.RaceTimeLowestWins;
        [SerializeField] private UpdateStrategy _updateStrategy = UpdateStrategy.Best;
        [SerializeField] private ActivityType _activityType = ActivityType.Race;
        [SerializeField] private AvailabilityScope _availability = AvailabilityScope.Map;
        [SerializeField] private string _activityId = string.Empty;
        [SerializeField] private string _mapId = string.Empty;
        [SerializeField] private string _mapName = string.Empty;
        [SerializeField] private string _pvpModeId = string.Empty;
        [SerializeField] private string _scoreUnit = "seconds";
        [SerializeField] private string _description = string.Empty;
        [SerializeField] private bool _daily = true;
        [SerializeField] private bool _weekly = true;
        [SerializeField] private bool _monthly = true;
        [SerializeField] private bool _allTime = true;
        [SerializeField] private bool _duos;
        [SerializeField] private bool _archiveResetPeriods = true;
        [SerializeField] private int _bucketSize;
        [SerializeField] private string _outputFolder = DefaultOutputFolder;
        [SerializeField] private Vector2 _scroll;

        [MenuItem("MashBox/Dev/Services/Leaderboard Generator")]
        public static void Open()
        {
            LeaderboardGeneratorWindow window = GetWindow<LeaderboardGeneratorWindow>();
            window.titleContent = new GUIContent("Leaderboard Generator");
            window.minSize = new Vector2(520.0f, 680.0f);
            window.Show();
        }

        private void OnEnable()
        {
            // Migrate saved editor-window state from the removed Level and Map And Level options.
            if (_availability != AvailabilityScope.Map && _availability != AvailabilityScope.Global)
                _availability = AvailabilityScope.Map;
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawHeader();
            DrawIdentitySection();
            DrawScoringSection();
            DrawContextSection();
            DrawPeriodsSection();
            DrawOutputSection();
            DrawPreview();
            DrawGenerateButton();
            EditorGUILayout.EndScrollView();
        }

        private static void DrawHeader()
        {
            EditorGUILayout.Space(8.0f);
            EditorGUILayout.LabelField("LEADERBOARD GENERATOR", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates UGS .lb deployment configs and a companion MashBox metadata manifest. " +
                "No service-account credentials are stored or shipped.",
                MessageType.Info);
            EditorGUILayout.Space(6.0f);
        }

        private void DrawIdentitySection()
        {
            DrawSectionTitle("Identity");
            EditorGUI.BeginChangeCheck();
            _displayName = EditorGUILayout.TextField("Display Name", _displayName);
            if (EditorGUI.EndChangeCheck() && string.IsNullOrWhiteSpace(_baseId))
                _baseId = MakeIdentifier(_displayName);

            using (new EditorGUILayout.HorizontalScope())
            {
                _baseId = EditorGUILayout.TextField("Base ID", _baseId);
                if (GUILayout.Button("From Name", GUILayout.Width(90.0f)))
                    _baseId = MakeIdentifier(_displayName);
            }
            EditorGUILayout.LabelField("Example", MakeIdentifier(_baseId) + "_Daily", EditorStyles.miniLabel);
        }

        private void DrawScoringSection()
        {
            DrawSectionTitle("Scoring");
            _scoreType = (ScoreType)EditorGUILayout.EnumPopup("Leaderboard Type", _scoreType);
            _updateStrategy = (UpdateStrategy)EditorGUILayout.EnumPopup("Update Strategy", _updateStrategy);
            _scoreUnit = EditorGUILayout.TextField("Score Unit", _scoreUnit);
            _bucketSize = Mathf.Max(0, EditorGUILayout.IntField("Bucket Size", _bucketSize));

            string direction = _scoreType == ScoreType.RaceTimeLowestWins
                ? "Ascending: the lowest time/score ranks first."
                : "Descending: the highest score ranks first.";
            EditorGUILayout.HelpBox(direction, MessageType.None);
        }

        private void DrawContextSection()
        {
            DrawSectionTitle("Where It Is Achievable");
            _activityType = (ActivityType)EditorGUILayout.EnumPopup("Activity Type", _activityType);
            _availability = (AvailabilityScope)EditorGUILayout.EnumPopup("Availability", _availability);
            _activityId = EditorGUILayout.TextField("Activity / Event ID", _activityId);

            if (_availability == AvailabilityScope.Map)
            {
                _mapId = EditorGUILayout.TextField("Map ID", _mapId);
                _mapName = EditorGUILayout.TextField("Map Name", _mapName);
            }

            if (_activityType == ActivityType.PvpMatch)
                _pvpModeId = EditorGUILayout.TextField("PvP Mode ID", _pvpModeId);

            if (_activityType == ActivityType.Race)
            {
                _duos = EditorGUILayout.Toggle("Generate Duos", _duos);
                EditorGUILayout.HelpBox(
                    "Creates a second leaderboard family for two-rider team times. " +
                    "Each selected period is generated for both Solo and Duos.", MessageType.None);
            }

            EditorGUILayout.LabelField("Description");
            _description = EditorGUILayout.TextArea(_description, GUILayout.MinHeight(52.0f));
        }

        private void DrawPeriodsSection()
        {
            DrawSectionTitle("Periods");
            using (new EditorGUILayout.HorizontalScope())
            {
                _daily = GUILayout.Toggle(_daily, "Daily");
                _weekly = GUILayout.Toggle(_weekly, "Weekly");
                _monthly = GUILayout.Toggle(_monthly, "Monthly");
                _allTime = GUILayout.Toggle(_allTime, "All Time");
            }
            _archiveResetPeriods = EditorGUILayout.Toggle("Archive Old Periods", _archiveResetPeriods);
            EditorGUILayout.HelpBox(
                "Resets use UTC: daily at 00:00, weekly Monday at 00:00, and monthly on the first at 00:00. " +
                "All Time has no reset configuration.", MessageType.None);
        }

        private void DrawOutputSection()
        {
            DrawSectionTitle("Output");
            using (new EditorGUILayout.HorizontalScope())
            {
                _outputFolder = EditorGUILayout.TextField("Asset Folder", _outputFolder);
                if (GUILayout.Button("Choose", GUILayout.Width(76.0f)))
                    ChooseOutputFolder();
            }
            EditorGUILayout.LabelField(
                "Manifest",
                MakeIdentifier(_baseId) + ".mashbox-leaderboards.json",
                EditorStyles.miniLabel);
        }

        private void DrawPreview()
        {
            DrawSectionTitle("Files To Generate");
            List<Period> periods = GetSelectedPeriods();
            if (periods.Count == 0)
            {
                EditorGUILayout.HelpBox("Select at least one period.", MessageType.Warning);
                return;
            }

            string id = MakeIdentifier(_baseId);
            for (int i = 0; i < periods.Count; i++)
                EditorGUILayout.LabelField("• " + LeaderboardId(id, periods[i]) + ".lb");
            if (ShouldGenerateDuos())
            {
                string duoId = id + "_Duos";
                for (int i = 0; i < periods.Count; i++)
                    EditorGUILayout.LabelField("• " + LeaderboardId(duoId, periods[i]) + ".lb");
            }
            EditorGUILayout.LabelField("• " + id + ".mashbox-leaderboards.json");
        }

        private void DrawGenerateButton()
        {
            EditorGUILayout.Space(12.0f);
            string validationError = ValidateInputs();
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(validationError)))
            {
                if (GUILayout.Button("GENERATE / UPDATE LEADERBOARDS", GUILayout.Height(38.0f)))
                    Generate();
            }

            if (!string.IsNullOrEmpty(validationError))
                EditorGUILayout.HelpBox(validationError, MessageType.Error);
            EditorGUILayout.Space(10.0f);
        }

        private void Generate()
        {
            string validationError = ValidateInputs();
            if (!string.IsNullOrEmpty(validationError))
            {
                EditorUtility.DisplayDialog("Leaderboard Generator", validationError, "OK");
                return;
            }

            string outputAssetFolder = NormalizeAssetPath(_outputFolder);
            string outputAbsoluteFolder = AssetPathToAbsolute(outputAssetFolder);
            string baseId = MakeIdentifier(_baseId);
            List<Period> periods = GetSelectedPeriods();
            var files = new List<GeneratedFile>();
            DateTime utcNow = DateTime.UtcNow;

            for (int i = 0; i < periods.Count; i++)
            {
                Period period = periods[i];
                string leaderboardId = LeaderboardId(baseId, period);
                string assetPath = outputAssetFolder + "/" + leaderboardId + ".lb";
                files.Add(new GeneratedFile(assetPath, BuildLeaderboardJson(leaderboardId, period, utcNow)));
            }

            if (ShouldGenerateDuos())
            {
                string duoBaseId = baseId + "_Duos";
                for (int i = 0; i < periods.Count; i++)
                {
                    Period period = periods[i];
                    string leaderboardId = LeaderboardId(duoBaseId, period);
                    string assetPath = outputAssetFolder + "/" + leaderboardId + ".lb";
                    files.Add(new GeneratedFile(assetPath,
                        BuildLeaderboardJson(leaderboardId, period, utcNow, true)));
                }
            }

            string manifestPath = outputAssetFolder + "/" + baseId + ".mashbox-leaderboards.json";
            files.Add(new GeneratedFile(manifestPath, BuildManifestJson(baseId, periods, utcNow)));

            List<string> existing = new List<string>();
            for (int i = 0; i < files.Count; i++)
            {
                if (File.Exists(AssetPathToAbsolute(files[i].AssetPath)))
                    existing.Add(Path.GetFileName(files[i].AssetPath));
            }

            if (existing.Count > 0 && !EditorUtility.DisplayDialog(
                    "Update Existing Leaderboards?",
                    "The following files already exist and will be replaced:\n\n" + string.Join("\n", existing),
                    "Update Files",
                    "Cancel"))
            {
                return;
            }

            Directory.CreateDirectory(outputAbsoluteFolder);
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < files.Count; i++)
                    File.WriteAllText(AssetPathToAbsolute(files[i].AssetPath), files[i].Contents, Utf8WithoutBom);
                SyncRuntimeLeaderboardCatalog(baseId, periods);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(files[0].AssetPath);
            EditorGUIUtility.PingObject(Selection.activeObject);
            Debug.Log("[MashBox Leaderboards] Generated " + files.Count + " files in " + outputAssetFolder +
                      ". Deploy the .lb assets with the Unity Deployment window.");
            EditorUtility.DisplayDialog(
                "Leaderboards Generated",
                "Created " + (periods.Count * (ShouldGenerateDuos() ? 2 : 1)) +
                " leaderboard configuration(s), one MashBox metadata manifest, and synchronized the runtime browser catalog.\n\n" +
                outputAssetFolder,
                "OK");
        }

        private void SyncRuntimeLeaderboardCatalog(string baseId, List<Period> periods)
        {
            string[] catalogGuids = AssetDatabase.FindAssets(
                Path.GetFileNameWithoutExtension(RuntimeCatalogFileName) + " t:TextAsset");
            string catalogAssetPath = string.Empty;
            for (int i = 0; i < catalogGuids.Length; i++)
            {
                string candidate = AssetDatabase.GUIDToAssetPath(catalogGuids[i]);
                if (string.Equals(Path.GetFileName(candidate), RuntimeCatalogFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    catalogAssetPath = candidate;
                    break;
                }
            }

            if (string.IsNullOrEmpty(catalogAssetPath))
            {
                Debug.LogWarning("[MashBox Leaderboards] Runtime browser catalog was not found; " +
                                 "UGS configs and the manifest were still generated.");
                return;
            }

            string catalogAbsolutePath = AssetPathToAbsolute(catalogAssetPath);
            RuntimeCatalog catalog = JsonUtility.FromJson<RuntimeCatalog>(File.ReadAllText(catalogAbsolutePath));
            var boards = catalog?.boards != null
                ? new List<RuntimeCatalogBoard>(catalog.boards)
                : new List<RuntimeCatalogBoard>();
            string duoBaseId = baseId + "_Duos";
            int insertionIndex = boards.FindIndex(board => board != null &&
                (string.Equals(board.id, baseId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(board.id, duoBaseId, StringComparison.OrdinalIgnoreCase)));
            if (insertionIndex < 0)
                insertionIndex = boards.Count;
            boards.RemoveAll(board => board != null &&
                (string.Equals(board.id, baseId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(board.id, duoBaseId, StringComparison.OrdinalIgnoreCase)));

            boards.Insert(insertionIndex, BuildRuntimeCatalogBoard(baseId, periods, false));
            if (ShouldGenerateDuos())
                boards.Insert(insertionIndex + 1, BuildRuntimeCatalogBoard(duoBaseId, periods, true));

            if (catalog == null)
                catalog = new RuntimeCatalog();
            catalog.boards = boards.ToArray();
            File.WriteAllText(catalogAbsolutePath, JsonUtility.ToJson(catalog, true) + Environment.NewLine,
                Utf8WithoutBom);
            Debug.Log("[MashBox Leaderboards] Synchronized runtime browser metadata in " + catalogAssetPath + ".");
        }

        private RuntimeCatalogBoard BuildRuntimeCatalogBoard(string id, List<Period> periods, bool duos)
        {
            var runtimePeriods = new RuntimeCatalogPeriod[periods.Count];
            for (int i = 0; i < periods.Count; i++)
            {
                runtimePeriods[i] = new RuntimeCatalogPeriod
                {
                    range = EnumToken(periods[i]),
                    id = LeaderboardId(id, periods[i])
                };
            }

            string mapLabel = UsesMap()
                ? (!string.IsNullOrWhiteSpace(_mapName) ? _mapName.Trim() : _mapId.Trim())
                : "GLOBAL";
            return new RuntimeCatalogBoard
            {
                id = id,
                displayName = _displayName.Trim(),
                location = mapLabel,
                mapId = UsesMap() ? _mapId.Trim() : string.Empty,
                mapName = UsesMap() ? _mapName.Trim() : string.Empty,
                activityId = _activityId.Trim(),
                description = _description.Trim(),
                scoreFormat = _scoreType == ScoreType.RaceTimeLowestWins ? "time" : "points",
                category = RuntimeCategory(),
                metric = _scoreType == ScoreType.RaceTimeLowestWins ? "Best Time" : "Highest Score",
                mode = duos ? "Duos" : "Solo",
                defaultPeriod = DefaultRuntimePeriod(periods),
                periods = runtimePeriods,
                audiences = new[] { "global", "friends", "clanTag", "aroundMe" }
            };
        }

        private string RuntimeCategory()
        {
            switch (_activityType)
            {
                case ActivityType.Race: return "Races";
                case ActivityType.ScoreChallenge: return "Challenges";
                case ActivityType.PvpMatch: return "PvP";
                default: return "Other";
            }
        }

        private static string DefaultRuntimePeriod(List<Period> periods)
        {
            if (periods.Contains(Period.Weekly)) return "weekly";
            if (periods.Contains(Period.AllTime)) return "allTime";
            return periods.Count > 0 ? EnumToken(periods[0]) : "allTime";
        }

        private string BuildLeaderboardJson(string leaderboardId, Period period, DateTime utcNow, bool duos = false)
        {
            var json = new StringBuilder(512);
            json.AppendLine("{");
            AppendJsonProperty(json, "$schema", SchemaUrl, true, 2);
            AppendJsonProperty(json, "SortOrder", SortOrderValue(), true, 2);
            AppendJsonProperty(json, "UpdateType", UpdateTypeValue(), true, 2);
            if (_bucketSize > 0)
                AppendJsonNumber(json, "BucketSize", _bucketSize, true, 2);

            if (period != Period.AllTime)
            {
                DateTime start = GetNextResetStart(period, utcNow);
                json.AppendLine("  \"ResetConfig\": {");
                AppendJsonProperty(json, "Start", start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), true, 4);
                AppendJsonProperty(json, "Schedule", ResetSchedule(period), true, 4);
                AppendJsonBoolean(json, "Archive", _archiveResetPeriods, false, 4);
                json.AppendLine("  },");
            }

            AppendJsonProperty(json, "Name", DisplayNameForPeriod(period, duos), true, 2);
            AppendJsonProperty(json, "Id", leaderboardId, false, 2);
            json.AppendLine("}");
            return json.ToString();
        }

        private string BuildManifestJson(string baseId, List<Period> periods, DateTime utcNow)
        {
            var json = new StringBuilder(2048);
            json.AppendLine("{");
            AppendJsonNumber(json, "schemaVersion", 2, true, 2);
            AppendJsonProperty(json, "baseId", baseId, true, 2);
            AppendJsonProperty(json, "displayName", _displayName.Trim(), true, 2);
            AppendJsonProperty(json, "scoreType", ScoreTypeValue(), true, 2);
            AppendJsonProperty(json, "sortOrder", SortOrderValue(), true, 2);
            AppendJsonProperty(json, "updateType", UpdateTypeValue(), true, 2);
            AppendJsonProperty(json, "scoreUnit", _scoreUnit.Trim(), true, 2);
            AppendJsonProperty(json, "activityType", EnumToken(_activityType), true, 2);
            AppendJsonProperty(json, "activityId", _activityId.Trim(), true, 2);
            AppendJsonProperty(json, "availability", EnumToken(_availability), true, 2);
            AppendJsonProperty(json, "mapId", UsesMap() ? _mapId.Trim() : string.Empty, true, 2);
            AppendJsonProperty(json, "mapName", UsesMap() ? _mapName.Trim() : string.Empty, true, 2);
            AppendJsonProperty(json, "pvpModeId", _activityType == ActivityType.PvpMatch ? _pvpModeId.Trim() : string.Empty, true, 2);
            AppendJsonProperty(json, "description", _description.Trim(), true, 2);
            AppendJsonBoolean(json, "duosEnabled", ShouldGenerateDuos(), true, 2);
            AppendJsonProperty(json, "generatedUtc", utcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), true, 2);
            json.AppendLine("  \"leaderboards\": [");
            int leaderboardCount = periods.Count * (ShouldGenerateDuos() ? 2 : 1);
            int leaderboardIndex = 0;
            for (int formatIndex = 0; formatIndex < (ShouldGenerateDuos() ? 2 : 1); formatIndex++)
            {
                bool duos = formatIndex == 1;
                for (int i = 0; i < periods.Count; i++)
                {
                    Period period = periods[i];
                    string id = LeaderboardId(baseId + (duos ? "_Duos" : string.Empty), period);
                    json.AppendLine("    {");
                    AppendJsonProperty(json, "id", id, true, 6);
                    AppendJsonProperty(json, "name", DisplayNameForPeriod(period, duos), true, 6);
                    AppendJsonProperty(json, "period", EnumToken(period), true, 6);
                    AppendJsonProperty(json, "format", duos ? "duos" : "solo", true, 6);
                    AppendJsonNumber(json, "teamSize", duos ? 2 : 1, true, 6);
                    AppendJsonProperty(json, "configAsset", NormalizeAssetPath(_outputFolder) + "/" + id + ".lb", true, 6);
                    AppendJsonBoolean(json, "resets", period != Period.AllTime, true, 6);
                    AppendJsonBoolean(json, "archives", period != Period.AllTime && _archiveResetPeriods, false, 6);
                    json.Append("    }");
                    leaderboardIndex++;
                    json.AppendLine(leaderboardIndex < leaderboardCount ? "," : string.Empty);
                }
            }
            json.AppendLine("  ]");
            json.AppendLine("}");
            return json.ToString();
        }

        private string ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(_displayName))
                return "Display Name is required.";
            if (string.IsNullOrWhiteSpace(MakeIdentifier(_baseId)))
                return "Base ID must contain at least one letter or number.";
            if (GetSelectedPeriods().Count == 0)
                return "Select at least one leaderboard period.";
            if (string.IsNullOrWhiteSpace(_scoreUnit))
                return "Score Unit is required (for example seconds, milliseconds, points, or wins).";
            if (string.IsNullOrWhiteSpace(_activityId))
                return "Activity / Event ID is required so game code can identify where this leaderboard is earned.";
            if (UsesMap() && string.IsNullOrWhiteSpace(_mapId))
                return "Map ID is required for the selected availability.";
            if (_activityType == ActivityType.PvpMatch && string.IsNullOrWhiteSpace(_pvpModeId))
                return "PvP Mode ID is required for a PvP leaderboard.";

            string assetFolder = NormalizeAssetPath(_outputFolder);
            if (assetFolder != "Assets" && !assetFolder.StartsWith("Assets/", StringComparison.Ordinal))
                return "Output must be a folder inside this project's Assets directory.";

            string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string resolvedOutput = AssetPathToAbsolute(assetFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!resolvedOutput.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase) &&
                !resolvedOutput.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return "Output resolves outside this project's Assets directory.";
            }
            return string.Empty;
        }

        private List<Period> GetSelectedPeriods()
        {
            var periods = new List<Period>(4);
            if (_daily) periods.Add(Period.Daily);
            if (_weekly) periods.Add(Period.Weekly);
            if (_monthly) periods.Add(Period.Monthly);
            if (_allTime) periods.Add(Period.AllTime);
            return periods;
        }

        private void ChooseOutputFolder()
        {
            string current = AssetPathToAbsolute(NormalizeAssetPath(_outputFolder));
            string selected = EditorUtility.OpenFolderPanel("Leaderboard Output Folder", current, string.Empty);
            if (string.IsNullOrEmpty(selected))
                return;

            string projectAssets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            string normalized = Path.GetFullPath(selected).Replace('\\', '/').TrimEnd('/');
            if (!normalized.Equals(projectAssets, StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith(projectAssets + "/", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Invalid Output Folder", "Choose a folder inside this project's Assets directory.", "OK");
                return;
            }

            _outputFolder = "Assets" + normalized.Substring(projectAssets.Length);
        }

        private bool UsesMap()
        {
            return _availability == AvailabilityScope.Map;
        }

        private string SortOrderValue()
        {
            return _scoreType == ScoreType.RaceTimeLowestWins ? "asc" : "desc";
        }

        private string ScoreTypeValue()
        {
            return _scoreType == ScoreType.RaceTimeLowestWins ? "raceTime" : "highScore";
        }

        private string UpdateTypeValue()
        {
            switch (_updateStrategy)
            {
                case UpdateStrategy.Latest: return "keepLatest";
                case UpdateStrategy.Total: return "aggregate";
                default: return "keepBest";
            }
        }

        private bool ShouldGenerateDuos()
        {
            return _activityType == ActivityType.Race && _duos;
        }

        private string DisplayNameForPeriod(Period period, bool duos = false)
        {
            return _displayName.Trim() + (duos ? " - Duos " : " ") + PeriodDisplayName(period);
        }

        private static string PeriodDisplayName(Period period)
        {
            return period == Period.AllTime ? "All Time" : period.ToString();
        }

        private static string PeriodSuffix(Period period)
        {
            return period == Period.AllTime ? "AllTime" : period.ToString();
        }

        private static string LeaderboardId(string baseId, Period period)
        {
            return period == Period.AllTime ? baseId : baseId + "_" + PeriodSuffix(period);
        }

        private static DateTime GetNextResetStart(Period period, DateTime utcNow)
        {
            DateTime today = utcNow.Date;
            switch (period)
            {
                case Period.Daily:
                    return today.AddDays(1.0);
                case Period.Weekly:
                    int daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
                    if (daysUntilMonday == 0) daysUntilMonday = 7;
                    return today.AddDays(daysUntilMonday);
                case Period.Monthly:
                    return new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                default:
                    return today;
            }
        }

        private static string ResetSchedule(Period period)
        {
            switch (period)
            {
                case Period.Daily: return "0 0 * * *";
                case Period.Weekly: return "0 0 * * 1";
                case Period.Monthly: return "0 0 1 * *";
                default: return string.Empty;
            }
        }

        private static string MakeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var result = new StringBuilder(value.Length);
            bool capitalizeNext = true;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (!char.IsLetterOrDigit(character))
                {
                    capitalizeNext = true;
                    continue;
                }

                if (result.Length == 0 && char.IsDigit(character))
                    result.Append("LB");
                result.Append(capitalizeNext ? char.ToUpperInvariant(character) : character);
                capitalizeNext = false;
            }
            return result.ToString();
        }

        private static string EnumToken<T>(T value)
        {
            string text = value.ToString();
            return string.IsNullOrEmpty(text)
                ? string.Empty
                : char.ToLowerInvariant(text[0]) + text.Substring(1);
        }

        private static string NormalizeAssetPath(string path)
        {
            return (path ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, NormalizeAssetPath(assetPath)));
        }

        private static void DrawSectionTitle(string title)
        {
            EditorGUILayout.Space(10.0f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void AppendJsonProperty(StringBuilder json, string name, string value, bool comma, int indent)
        {
            json.Append(' ', indent).Append('\"').Append(EscapeJson(name)).Append("\": \"")
                .Append(EscapeJson(value)).Append('\"');
            if (comma) json.Append(',');
            json.AppendLine();
        }

        private static void AppendJsonNumber(StringBuilder json, string name, int value, bool comma, int indent)
        {
            json.Append(' ', indent).Append('\"').Append(EscapeJson(name)).Append("\": ").Append(value);
            if (comma) json.Append(',');
            json.AppendLine();
        }

        private static void AppendJsonBoolean(StringBuilder json, string name, bool value, bool comma, int indent)
        {
            json.Append(' ', indent).Append('\"').Append(EscapeJson(name)).Append("\": ")
                .Append(value ? "true" : "false");
            if (comma) json.Append(',');
            json.AppendLine();
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        [Serializable]
        private sealed class RuntimeCatalog
        {
            public RuntimeCatalogBoard[] boards;
        }

        [Serializable]
        private sealed class RuntimeCatalogBoard
        {
            public string id;
            public string displayName;
            public string location;
            public string mapId;
            public string mapName;
            public string activityId;
            public string description;
            public string scoreFormat;
            public string category;
            public string metric;
            public string mode;
            public string defaultPeriod;
            public RuntimeCatalogPeriod[] periods;
            public string[] audiences;
            public bool favoriteByDefault;
        }

        [Serializable]
        private sealed class RuntimeCatalogPeriod
        {
            public string range;
            public string id;
        }

        private readonly struct GeneratedFile
        {
            public GeneratedFile(string assetPath, string contents)
            {
                AssetPath = assetPath;
                Contents = contents;
            }

            public string AssetPath { get; }
            public string Contents { get; }
        }
    }
}

#endif
