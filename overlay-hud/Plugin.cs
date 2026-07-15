using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverlayHUD
{
    [BepInPlugin("local.overlay.overlay_hud", "OverlayHUD", "0.2.107")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private static Plugin instance;
        private static readonly List<string> seenMonsters = new List<string>();
        private static readonly List<Component> knownEnemyParents = new List<Component>();
        private static readonly HashSet<int> sentEnemyInstanceIds = new HashSet<int>();
        private static readonly HashSet<int> valuablesInDollarHaul = new HashSet<int>();
        private static readonly object seenLock = new object();
        private static readonly object enemyLock = new object();
        private static readonly object reflectionCacheLock = new object();
        private static readonly object networkQueueLock = new object();
        private static readonly Dictionary<string, MemberInfo> memberCache = new Dictionary<string, MemberInfo>();
        private static readonly HashSet<string> missingMemberCache = new HashSet<string>();
        private static readonly Dictionary<string, MethodInfo> noArgMethodCache = new Dictionary<string, MethodInfo>();
        private static readonly HashSet<string> missingNoArgMethodCache = new HashSet<string>();
        private static Task networkQueueTail = Task.CompletedTask;
        private static float mapValue;
        private static float mapValueInitial;
        private static float lostValue;

        private static readonly Dictionary<string, string> KnownMonsters = new Dictionary<string, string>
        {
            { "apexpredator", "Apex Predator" },
            { "animal", "Animal" },
            { "banger", "Banger" },
            { "bang", "Banger" },
            { "bella", "Bella" },
            { "beamer", "Clown" },
            { "birthdayboy", "Birthday Boy" },
            { "bowtie", "Bowtie" },
            { "chef", "Chef" },
            { "cheffrog", "Chef" },
            { "ceilingeye", "Peeper" },
            { "cleanupcrew", "Cleanup Crew" },
            { "clown", "Clown" },
            { "clownbeamer", "Clown" },
            { "duck", "Rugrat" },
            { "elsa", "Elsa" },
            { "floater", "Mentalist" },
            { "gambit", "Gambit" },
            { "gnome", "Gnomes" },
            { "headgrab", "Headgrab" },
            { "headgrabber", "Headgrab" },
            { "headman", "Headman" },
            { "hearthugger", "Heart Hugger" },
            { "hidden", "Hidden" },
            { "huntsman", "Huntsman" },
            { "hunter", "Huntsman" },
            { "loom", "Loom" },
            { "mentalist", "Mentalist" },
            { "oogly", "Oogly" },
            { "peeper", "Peeper" },
            { "reaper", "Reaper" },
            { "robe", "Robe" },
            { "rugrat", "Rugrat" },
            { "runner", "Gambit" },
            { "shadowchild", "Shadow Child" },
            { "shadow", "Shadow Child" },
            { "spewer", "Spewer" },
            { "slowmouth", "Spewer" },
            { "slowwalker", "Trudge" },
            { "spinny", "Bowtie" },
            { "thinman", "Reaper" },
            { "tick", "Tick" },
            { "trudge", "Trudge" },
            { "tricycle", "Birthday Boy" },
            { "tumbler", "Apex Predator" },
            { "upscream", "Upscream" },
            { "valuablethrower", "Rugrat" }
        };

        private static readonly KeyValuePair<string, string>[] TrackedPlayerUpgrades =
        {
            new KeyValuePair<string, string>("strength", "playerUpgradeStrength"),
            new KeyValuePair<string, string>("tumbleLaunch", "playerUpgradeLaunch"),
            new KeyValuePair<string, string>("range", "playerUpgradeRange"),
            new KeyValuePair<string, string>("sprintSpeed", "playerUpgradeSpeed"),
            new KeyValuePair<string, string>("tumbleWings", "playerUpgradeTumbleWings"),
            new KeyValuePair<string, string>("crouchRest", "playerUpgradeCrouchRest"),
            new KeyValuePair<string, string>("extraJump", "playerUpgradeExtraJump"),
            new KeyValuePair<string, string>("tumbleClimb", "playerUpgradeTumbleClimb")
        };

        private static readonly Dictionary<string, string> UpgradeStateKeyByPunMethod = new Dictionary<string, string>
        {
            { "UpgradePlayerGrabStrength", "strength" },
            { "UpgradePlayerTumbleLaunch", "tumbleLaunch" },
            { "UpgradePlayerGrabRange", "range" },
            { "UpgradePlayerSprintSpeed", "sprintSpeed" },
            { "UpgradePlayerTumbleWings", "tumbleWings" },
            { "UpgradePlayerCrouchRest", "crouchRest" },
            { "UpgradePlayerExtraJump", "extraJump" },
            { "UpgradePlayerTumbleClimb", "tumbleClimb" }
        };

        private static readonly HashSet<string> PlayerVisionLegacyFallbackMonsters = new HashSet<string>(StringComparer.Ordinal)
        {
            "Tick",
            "Upscream"
        };

        private static readonly string[] CurrentHealthMemberNames =
        {
            "currentHealth",
            "healthCurrent",
            "_currentSyncedHealth",
            "_syncedHealth",
            "currentHP",
            "healthValue",
            "HealthValue"
        };

        private static readonly string[] MaxHealthMemberNames =
        {
            "maxHealth",
            "MaxHealth",
            "healthMax",
            "HealthMax",
            "healthMaximum",
            "maximumHealth",
            "maxHP",
            "HPMax",
            "health",
            "Health"
        };

        private readonly Dictionary<string, float> lastSeenLoggedAt = new Dictionary<string, float>();
        private readonly Dictionary<int, string> resolvedMonsterNames = new Dictionary<int, string>();
        private readonly Dictionary<int, int> sourceIdsByEnemyParent = new Dictionary<int, int>();
        private readonly Dictionary<int, bool> lastAliveBySourceId = new Dictionary<int, bool>();
        private readonly Dictionary<int, string> lastHealthDebugBySourceId = new Dictionary<int, string>();
        private readonly Dictionary<int, object[]> healthSourcesByRootId = new Dictionary<int, object[]>();
        private readonly Dictionary<int, string> lastImmediateStatusBySourceId = new Dictionary<int, string>();
        private readonly Dictionary<int, string> lastTimerStatusBySourceId = new Dictionary<int, string>();
        private readonly Dictionary<int, float> lastTimerStatusSentAtBySourceId = new Dictionary<int, float>();
        private readonly Dictionary<int, bool> enemyHasVisionByParentId = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> enemyHasOnScreenByParentId = new Dictionary<int, bool>();
        private readonly Dictionary<int, VisionEnemyCache> visionEnemyCacheByVisionId = new Dictionary<int, VisionEnemyCache>();
        private readonly HashSet<int> pendingEncounterIds = new HashSet<int>();
        private float nextScanAt;
        private float nextStatusSyncAt;
        private float nextUpgradeSyncAt;
        private float nextMapValueSyncAt;
        private float nextDebugSummaryAt;
        private float nextBroadEnemyDiscoveryAt;
        private float scanPausedUntil;
        private int fallbackLevel = 1;
        private int lastSyncedLevel;
        private readonly Dictionary<string, int> lastSyncedUpgrades = new Dictionary<string, int>();
        private readonly HashSet<string> pendingUpgradeKeys = new HashSet<string>();
        private string lastRosterFingerprint = "";
        private string lastStatusFingerprint = "";
        private string pendingRosterFingerprint = "";
        private int rosterStableScans;
        private int broadEnemyDiscoveryAttempts;
        private bool rosterPublished;
        private bool enemyRosterDirty;
        private bool mapValueDirty;
        private bool pendingMapValueRefresh;
        private string pendingMapValueRefreshReason = "";
        private string lastMapValueFingerprint = "";
        private int cachedLocalPlayerViewId = int.MinValue;
        private bool gameplayActive;
        private bool? lastTabHeld;
        private Coroutine pendingGameplayActivation;

        private ConfigEntry<string> endpoint;
        private ConfigEntry<string> levelEndpoint;
        private ConfigEntry<float> scanInterval;
        private ConfigEntry<float> statusInterval;
        private ConfigEntry<bool> preferPlayerVisionDetection;
        private ConfigEntry<bool> debugLogging;
        private ConfigEntry<bool> autoStartOverlayApp;
        private ConfigEntry<bool> autoCloseOverlayApp;
        private ConfigEntry<string> overlayAppRelativePath;
        private ConfigEntry<string> overlayAppArchiveName;
        private Harmony harmony;
        private Process launchedOverlayProcess;

        private void Awake()
        {
            instance = this;
            KeepPluginObjectAlive();
            endpoint = Config.Bind("Overlay", "Endpoint", "http://127.0.0.1:8787/api/monster-seen", "Monster endpoint on this PC.");
            levelEndpoint = Config.Bind("Overlay", "LevelEndpoint", "http://127.0.0.1:8787/api/level", "Level sync endpoint on this PC.");
            scanInterval = Config.Bind("Detection", "ScanIntervalSeconds", 3f, "How often pending enemy roster sync is retried.");
            statusInterval = Config.Bind("Detection", "StatusIntervalSeconds", 2f, "How often monster health and respawn status are synced after the roster is known.");
            preferPlayerVisionDetection = Config.Bind("Detection", "PreferPlayerVisionDetection", true, "When enabled, show monsters when the player sees them. When disabled, use the older enemy-vision plus player-very-close detection.");
            debugLogging = Config.Bind("Debug", "Logging", false, "Write periodic bridge debug logs.");
            autoStartOverlayApp = Config.Bind("OverlayApp", "AutoStart", true, "Start the bundled OverlayHUD desktop app when the game starts.");
            autoCloseOverlayApp = Config.Bind("OverlayApp", "AutoClose", true, "Close the bundled OverlayHUD desktop app when the game exits.");
            overlayAppRelativePath = Config.Bind("OverlayApp", "ExecutableRelativePath", Path.Combine("OverlayHUD_app", "OverlayHUD.exe"), "Path to the bundled OverlayHUD executable relative to this plugin DLL.");
            overlayAppArchiveName = Config.Bind("OverlayApp", "ArchiveName", "OverlayHUD_app.zip", "Bundled OverlayHUD desktop app archive next to this plugin DLL.");
            bool configChanged = false;
            if (endpoint.Value == "http://192.168.1.198:8787/api/monster-seen")
            {
                endpoint.Value = "http://127.0.0.1:8787/api/monster-seen";
                configChanged = true;
            }
            if (levelEndpoint.Value == "http://192.168.1.198:8787/api/level")
            {
                levelEndpoint.Value = "http://127.0.0.1:8787/api/level";
                configChanged = true;
            }
            if (overlayAppRelativePath.Value == "OverlayHUD.exe"
                || overlayAppRelativePath.Value == Path.Combine("OverlayHUD-win32-x64", "OverlayHUD.exe")
                || overlayAppRelativePath.Value == Path.Combine("OverlayHUDApp", "OverlayHUD.exe")
                || overlayAppRelativePath.Value == Path.Combine("OverlayHUD_app", "OverlayHUD-win32-x64", "OverlayHUD.exe"))
            {
                overlayAppRelativePath.Value = Path.Combine("OverlayHUD_app", "OverlayHUD.exe");
                configChanged = true;
            }
            if (overlayAppArchiveName.Value == "OverlayHUDApp.zip")
            {
                overlayAppArchiveName.Value = "OverlayHUD_app.zip";
                configChanged = true;
            }
            if (configChanged) Config.Save();

            Logger.LogInfo("OverlayHUD is running. MonsterEndpoint=" + endpoint.Value + ", LevelEndpoint=" + levelEndpoint.Value + ", Logging=" + debugLogging.Value);

            StartOverlayAppIfNeeded();
            PatchGameUpdates();
        }

        private void StartOverlayAppIfNeeded()
        {
            if (!autoStartOverlayApp.Value) return;

            try
            {
                string exePath = ResolveOverlayAppPath();
                EnsureOverlayAppExtracted(exePath);
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    Logger.LogWarning("Bundled OverlayHUD executable was not found: " + exePath);
                    return;
                }

                string processName = Path.GetFileNameWithoutExtension(exePath);
                if (IsProcessAlreadyRunning(processName))
                {
                    Logger.LogInfo("OverlayHUD desktop app is already running; leaving the existing process alone.");
                    return;
                }

                launchedOverlayProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = true
                });

                if (launchedOverlayProcess != null)
                {
                    Logger.LogInfo("Started bundled OverlayHUD desktop app: " + exePath);
                }
            }
            catch (Exception error)
            {
                Logger.LogWarning("Failed to start bundled OverlayHUD desktop app: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private string ResolveOverlayAppPath()
        {
            string pluginPath = Assembly.GetExecutingAssembly().Location;
            string pluginDirectory = string.IsNullOrWhiteSpace(pluginPath)
                ? Paths.PluginPath
                : Path.GetDirectoryName(pluginPath);
            string relativePath = overlayAppRelativePath.Value;
            return Path.GetFullPath(Path.Combine(pluginDirectory, relativePath));
        }

        private void EnsureOverlayAppExtracted(string exePath)
        {
            if (File.Exists(exePath) && File.Exists(Path.Combine(Path.GetDirectoryName(exePath), "resources", "app.asar"))) return;

            string pluginDirectory = GetPluginDirectory();
            string archivePath = Path.Combine(pluginDirectory, overlayAppArchiveName.Value);
            if (!File.Exists(archivePath)) return;

            string targetDirectory = Path.GetDirectoryName(exePath);
            if (string.IsNullOrWhiteSpace(targetDirectory)) return;
            if (!IsPathInsideDirectory(pluginDirectory, targetDirectory))
            {
                Logger.LogWarning("Refusing to extract bundled OverlayHUD desktop app outside the plugin directory: " + targetDirectory);
                return;
            }

            try
            {
                if (Directory.Exists(targetDirectory))
                {
                    Directory.Delete(targetDirectory, true);
                }

                Directory.CreateDirectory(targetDirectory);
                ZipFile.ExtractToDirectory(archivePath, targetDirectory);
                Logger.LogInfo("Extracted bundled OverlayHUD desktop app to: " + targetDirectory);
            }
            catch (Exception error)
            {
                Logger.LogWarning("Failed to extract bundled OverlayHUD desktop app: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private static bool IsPathInsideDirectory(string parentDirectory, string childPath)
        {
            string parent = Path.GetFullPath(parentDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }

        private string GetPluginDirectory()
        {
            string pluginPath = Assembly.GetExecutingAssembly().Location;
            return string.IsNullOrWhiteSpace(pluginPath)
                ? Paths.PluginPath
                : Path.GetDirectoryName(pluginPath);
        }

        private static bool IsProcessAlreadyRunning(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;

            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                return processes != null && processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private void StopOverlayAppIfNeeded()
        {
            if (!autoCloseOverlayApp.Value || launchedOverlayProcess == null) return;

            try
            {
                if (launchedOverlayProcess.HasExited) return;

                bool closeRequested = launchedOverlayProcess.CloseMainWindow();
                if (!closeRequested || !launchedOverlayProcess.WaitForExit(2500))
                {
                    launchedOverlayProcess.Kill();
                    launchedOverlayProcess.WaitForExit(2500);
                }

                Logger.LogInfo("Closed bundled OverlayHUD desktop app.");
            }
            catch (Exception error)
            {
                Logger.LogWarning("Failed to close bundled OverlayHUD desktop app: " + error.GetType().Name + ": " + error.Message);
            }
            finally
            {
                launchedOverlayProcess = null;
            }
        }

        private void PatchGameUpdates()
        {
            try
            {
                harmony = new Harmony("local.overlay.overlay_hud");
                MethodInfo enemyParentSpawnRpc = AccessTools.Method("EnemyParent:SpawnRPC");
                MethodInfo enemyParentDespawnRpc = AccessTools.Method("EnemyParent:DespawnRPC");
                MethodInfo enemyParentDisableDecrease = AccessTools.Method("EnemyParent:DisableDecrease");
                MethodInfo enemyParentDespawnedTimerSet = AccessTools.Method("EnemyParent:DespawnedTimerSet");
                MethodInfo enemyParentPlayerCloseLogic = AccessTools.Method("EnemyParent:PlayerCloseLogic");
                MethodInfo enemyOnScreenLogic = AccessTools.Method("EnemyOnScreen:Logic");
                MethodInfo levelGeneratorGenerateDone = AccessTools.Method("LevelGenerator:GenerateDone");
                MethodInfo levelGeneratorStartRoomGeneration = AccessTools.Method("LevelGenerator:StartRoomGeneration");
                MethodInfo runManagerChangeLevel = AccessTools.Method("RunManager:ChangeLevel");
                MethodInfo roundDirectorExtractionCompleted = AccessTools.Method("RoundDirector:ExtractionCompleted");
                MethodInfo valuableDollarValueSetRpc = AccessTools.Method("ValuableObject:DollarValueSetRPC");
                MethodInfo valuableDollarValueSetLogic = AccessTools.Method("ValuableObject:DollarValueSetLogic");
                MethodInfo valuableDollarHaulAdd = AccessTools.Method("ValuableObject:AddToDollarHaulList");
                MethodInfo valuableDollarHaulAddRpc = AccessTools.Method("ValuableObject:AddToDollarHaulListRPC");
                MethodInfo valuableDollarHaulRemove = AccessTools.Method("ValuableObject:RemoveFromDollarHaulList");
                MethodInfo valuableDollarHaulRemoveRpc = AccessTools.Method("ValuableObject:RemoveFromDollarHaulListRPC");
                MethodInfo physGrabObjectBreakRpc = AccessTools.Method("PhysGrabObjectImpactDetector:BreakRPC");
                MethodInfo physGrabObjectDestroyRpc = AccessTools.Method("PhysGrabObject:DestroyPhysGrabObjectRPC");
                MethodInfo enemyVisionTrigger = AccessTools.Method("EnemyVision:VisionTrigger");
                MethodInfo enemyHealthHurt = AccessTools.Method("EnemyHealth:Hurt");
                MethodInfo enemyHealthHurtRpc = AccessTools.Method("EnemyHealth:HurtRPC");
                HarmonyMethod levelGeneratedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(LevelGeneratedPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod levelGenerationStartingPrefix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(LevelGenerationStartingPrefix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod enemySpawnedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(EnemyParentSpawnedPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod enemyDespawnedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(EnemyParentDespawnedPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod enemyTimerChangedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(EnemyParentTimerChangedPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod enemyPlayerCloseLogicPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(EnemyParentPlayerCloseLogicPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod enemyOnScreenLogicPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(EnemyOnScreenLogicPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod levelChangingPrefix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(LevelChangingPrefix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod levelChangedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(LevelChangedPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod playerUpgradeAppliedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(PlayerUpgradeAppliedPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod extractionCompletedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(ExtractionCompletedPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod valuableDollarValueSetRpcPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(ValuableDollarValueSetRpcPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod valuableDollarValueSetLogicPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(ValuableDollarValueSetLogicPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod valuableDollarHaulAddPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(ValuableDollarHaulAddPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod valuableDollarHaulRemovePostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(ValuableDollarHaulRemovePostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod physGrabObjectBreakPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(PhysGrabObjectBreakPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod physGrabObjectDestroyedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(PhysGrabObjectDestroyedPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod enemyVisionTriggerPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(EnemyVisionTriggerPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod enemyHealthChangedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(EnemyHealthChangedPostfix), BindingFlags.NonPublic | BindingFlags.Static));

                if (enemyParentSpawnRpc != null)
                {
                    harmony.Patch(enemyParentSpawnRpc, postfix: enemySpawnedPostfix);
                    Logger.LogInfo("Patched EnemyParent.SpawnRPC for enemy registration.");
                }

                if (enemyParentDespawnRpc != null)
                {
                    harmony.Patch(enemyParentDespawnRpc, postfix: enemyDespawnedPostfix);
                    Logger.LogInfo("Patched EnemyParent.DespawnRPC for immediate status sync.");
                }

                if (enemyParentDisableDecrease != null)
                {
                    harmony.Patch(enemyParentDisableDecrease, postfix: enemyTimerChangedPostfix);
                    Logger.LogInfo("Patched EnemyParent.DisableDecrease for event-driven respawn timer sync.");
                }

                if (enemyParentDespawnedTimerSet != null)
                {
                    harmony.Patch(enemyParentDespawnedTimerSet, postfix: enemyTimerChangedPostfix);
                    Logger.LogInfo("Patched EnemyParent.DespawnedTimerSet for event-driven respawn timer sync.");
                }

                if (enemyParentPlayerCloseLogic != null)
                {
                    harmony.Patch(enemyParentPlayerCloseLogic, postfix: enemyPlayerCloseLogicPostfix);
                    Logger.LogInfo("Patched EnemyParent.PlayerCloseLogic for blind enemy very-close encounters.");
                }

                if (enemyOnScreenLogic != null)
                {
                    harmony.Patch(enemyOnScreenLogic, postfix: enemyOnScreenLogicPostfix);
                    Logger.LogInfo("Patched EnemyOnScreen.Logic for player-visible enemy encounters.");
                }

                if (levelGeneratorGenerateDone != null)
                {
                    harmony.Patch(levelGeneratorGenerateDone, postfix: levelGeneratedPostfix);
                    Logger.LogInfo("Patched LevelGenerator.GenerateDone for monster reset.");
                }

                if (levelGeneratorStartRoomGeneration != null)
                {
                    harmony.Patch(levelGeneratorStartRoomGeneration, prefix: levelGenerationStartingPrefix);
                    Logger.LogInfo("Patched LevelGenerator.StartRoomGeneration for map value reset.");
                }

                if (runManagerChangeLevel != null)
                {
                    harmony.Patch(runManagerChangeLevel, prefix: levelChangingPrefix, postfix: levelChangedPostfix);
                    Logger.LogInfo("Patched RunManager.ChangeLevel for overlay visibility and activation.");
                }

                if (roundDirectorExtractionCompleted != null)
                {
                    harmony.Patch(roundDirectorExtractionCompleted, postfix: extractionCompletedPostfix);
                    Logger.LogInfo("Patched RoundDirector.ExtractionCompleted for map value refresh.");
                }

                if (valuableDollarValueSetRpc != null)
                {
                    harmony.Patch(valuableDollarValueSetRpc, postfix: valuableDollarValueSetRpcPostfix);
                    Logger.LogInfo("Patched ValuableObject.DollarValueSetRPC for map value tracking.");
                }

                if (valuableDollarValueSetLogic != null)
                {
                    harmony.Patch(valuableDollarValueSetLogic, postfix: valuableDollarValueSetLogicPostfix);
                    Logger.LogInfo("Patched ValuableObject.DollarValueSetLogic for map value tracking.");
                }

                if (valuableDollarHaulAdd != null)
                {
                    harmony.Patch(valuableDollarHaulAdd, postfix: valuableDollarHaulAddPostfix);
                    Logger.LogInfo("Patched ValuableObject.AddToDollarHaulList for submitted value tracking.");
                }

                if (valuableDollarHaulAddRpc != null)
                {
                    harmony.Patch(valuableDollarHaulAddRpc, postfix: valuableDollarHaulAddPostfix);
                    Logger.LogInfo("Patched ValuableObject.AddToDollarHaulListRPC for submitted value tracking.");
                }

                if (valuableDollarHaulRemove != null)
                {
                    harmony.Patch(valuableDollarHaulRemove, postfix: valuableDollarHaulRemovePostfix);
                    Logger.LogInfo("Patched ValuableObject.RemoveFromDollarHaulList for submitted value tracking.");
                }

                if (valuableDollarHaulRemoveRpc != null)
                {
                    harmony.Patch(valuableDollarHaulRemoveRpc, postfix: valuableDollarHaulRemovePostfix);
                    Logger.LogInfo("Patched ValuableObject.RemoveFromDollarHaulListRPC for submitted value tracking.");
                }

                if (physGrabObjectBreakRpc != null)
                {
                    harmony.Patch(physGrabObjectBreakRpc, postfix: physGrabObjectBreakPostfix);
                    Logger.LogInfo("Patched PhysGrabObjectImpactDetector.BreakRPC for map value loss.");
                }

                if (physGrabObjectDestroyRpc != null)
                {
                    harmony.Patch(physGrabObjectDestroyRpc, postfix: physGrabObjectDestroyedPostfix);
                    Logger.LogInfo("Patched PhysGrabObject.DestroyPhysGrabObjectRPC for map value removal.");
                }

                if (enemyVisionTrigger != null)
                {
                    harmony.Patch(enemyVisionTrigger, postfix: enemyVisionTriggerPostfix);
                    Logger.LogInfo("Patched EnemyVision.VisionTrigger for sighted enemy encounters.");
                }

                if (enemyHealthHurt != null)
                {
                    harmony.Patch(enemyHealthHurt, postfix: enemyHealthChangedPostfix);
                    Logger.LogInfo("Patched EnemyHealth.Hurt for immediate health sync.");
                }

                if (enemyHealthHurtRpc != null)
                {
                    harmony.Patch(enemyHealthHurtRpc, postfix: enemyHealthChangedPostfix);
                    Logger.LogInfo("Patched EnemyHealth.HurtRPC for immediate health sync.");
                }

                int patchedUpgradeTypes = 0;
                foreach (string methodName in UpgradeStateKeyByPunMethod.Keys)
                {
                    MethodInfo upgradeMethod = AccessTools.Method("PunManager:" + methodName);
                    if (upgradeMethod == null) continue;
                    harmony.Patch(upgradeMethod, postfix: playerUpgradeAppliedPostfix);
                    patchedUpgradeTypes++;
                }
                Logger.LogInfo("Patched " + patchedUpgradeTypes + " PunManager upgrade methods for event-driven sync.");

            }
            catch (Exception error)
            {
                Logger.LogWarning("Failed to patch game updates: " + error.GetType().Name + ": " + error.Message);
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void KeepPluginObjectAlive()
        {
            try
            {
                Transform pluginTransform = gameObject.transform;
                if (pluginTransform.parent != null)
                {
                    pluginTransform.parent = null;
                }
                gameObject.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(gameObject);
            }
            catch (Exception error)
            {
                Logger.LogWarning("Failed to detach plugin object: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private void Update()
        {
            if (!gameplayActive) return;
            SyncTabStateIfChanged();
            TickScan();
        }

        private static void LevelGeneratedPostfix()
        {
            instance?.HandleLevelGenerated();
        }

        private static void LevelGenerationStartingPrefix()
        {
            instance?.ResetMapValue("room generation started");
        }

        private static void LevelChangingPrefix()
        {
            instance?.HandleLevelChanging();
        }

        private static void LevelChangedPostfix(object __instance)
        {
            instance?.HandleLevelChanged(__instance);
        }

        private static void PlayerUpgradeAppliedPostfix(MethodBase __originalMethod, string _steamID, int __result)
        {
            string methodName = __originalMethod?.Name;
            if (methodName != null && UpgradeStateKeyByPunMethod.TryGetValue(methodName, out string stateKey))
            {
                instance?.SyncPlayerUpgradeValueIfLocal(stateKey, _steamID, __result);
            }
        }

        private static void EnemyParentSpawnedPostfix(object __instance)
        {
            if (__instance is Component component)
            {
                RegisterEnemyParent(component);
                instance?.SyncEnemyParentStatusChanged(component);
            }
        }

        private static void EnemyParentDespawnedPostfix(object __instance)
        {
            if (__instance is Component component)
            {
                instance?.ScheduleEnemyParentStatusChanged(component);
            }
        }

        private static void EnemyParentTimerChangedPostfix(object __instance)
        {
            if (__instance is Component component)
            {
                instance?.SyncEnemyParentTimerChanged(component);
            }
        }

        private static void EnemyParentPlayerCloseLogicPostfix(object __instance, ref IEnumerator __result)
        {
            if (__result == null || !(__instance is Component enemyParent) || HasEnemyVision(enemyParent)) return;
            __result = WatchBlindEnemyPlayerClose(__result, enemyParent);
        }

        private static void EnemyOnScreenLogicPostfix(object __instance, ref IEnumerator __result)
        {
            if (__result == null || !(__instance is Component enemyOnScreen)) return;
            __result = WatchEnemyOnScreen(__result, enemyOnScreen);
        }

        private static void ExtractionCompletedPostfix()
        {
            instance?.RefreshMapValue("extraction completed");
        }

        private static void ValuableDollarValueSetRpcPostfix(object __instance, float value)
        {
            instance?.ScheduleMapValueRefresh("valuable rpc");
        }

        private static void ValuableDollarValueSetLogicPostfix(object __instance)
        {
            if (!IsMasterClientOrSingleplayer()) return;
            instance?.ScheduleMapValueRefresh("valuable logic");
        }

        private static void ValuableDollarHaulAddPostfix(object __instance)
        {
            TrackDollarHaulValuable(__instance, true);
        }

        private static void ValuableDollarHaulRemovePostfix(object __instance)
        {
            TrackDollarHaulValuable(__instance, false);
        }

        private static void PhysGrabObjectBreakPostfix(object __instance, float valueLost, bool _loseValue)
        {
            if (!_loseValue) return;
            instance?.AddMapValue(-valueLost, "valuable break");
            instance?.AddLostValue(valueLost, "valuable break");
        }

        private static void PhysGrabObjectDestroyedPostfix(object __instance)
        {
            if (!IsRunLevel()) return;
            Component valuable = GetValuableComponent(__instance);
            if (valuable == null) return;

            float currentValue = ReadValuableCurrentValue(valuable);
            float originalValue = ReadValuableOriginalValue(valuable);
            if (originalValue > 0f && currentValue < originalValue * 0.15f) return;
            instance?.AddMapValue(-currentValue, "valuable destroyed");
            if (IsValuableInDollarHaul(valuable)) return;
            instance?.AddLostValue(currentValue, "valuable destroyed");
        }

        private static void EnemyVisionTriggerPostfix(object __instance, int playerID, object player, bool culled, bool playerNear)
        {
            instance?.HandleEnemyVisionTrigger(__instance, playerID);
        }

        private static void EnemyHealthChangedPostfix(object __instance)
        {
            instance?.SyncEnemyHealthChanged(__instance);
        }

        private void TickScan()
        {
            if (!gameplayActive) return;
            float now = Time.realtimeSinceStartup;
            if (mapValueDirty && now >= nextMapValueSyncAt)
            {
                SyncMapValueIfChanged();
            }
            if (now < scanPausedUntil) return;

            if (pendingUpgradeKeys.Count > 0 && now >= nextUpgradeSyncAt)
            {
                var keys = new HashSet<string>(pendingUpgradeKeys);
                pendingUpgradeKeys.Clear();
                SyncPlayerUpgradesIfChanged(keys);
            }

            if (rosterPublished && now >= nextStatusSyncAt)
            {
                nextStatusSyncAt = now + Math.Max(5f, statusInterval.Value);
                SyncMonsterStatuses(new List<ResolvedEnemyCandidate>());
            }

            if (!enemyRosterDirty) return;
            if (now < nextScanAt) return;
            nextScanAt = now + Math.Max(0.05f, scanInterval.Value);

            try
            {
                SyncKnownEnemies();
            }
            catch (Exception error)
            {
                Logger.LogWarning("Enemy sync failed: " + error.GetType().Name + ": " + error.Message + "\n" + error.StackTrace);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            harmony?.UnpatchSelf();
            if (gameplayActive) HandleLevelChanging();
            StopOverlayAppIfNeeded();
            if (instance == this) instance = null;
        }

        private void HandleLevelGenerated()
        {
            ScheduleGameplayActivation("level generator done");
        }

        private void HandleLevelChanged(object runManager)
        {
            if (debugLogging.Value)
            {
                Logger.LogInfo("RunManager.ChangeLevel completed, current level is " + DescribeCurrentLevel(runManager) + ".");
            }
            ScheduleGameplayActivation("RunManager.ChangeLevel");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!gameplayActive) return;
            if (IsRunLevel() || IsRunLevelName(scene.name)) return;
            HandleLevelChanging();
        }

        private void ScheduleGameplayActivation(string reason)
        {
            if (pendingGameplayActivation != null)
            {
                StopCoroutine(pendingGameplayActivation);
            }
            pendingGameplayActivation = StartCoroutine(ActivateGameplayAfterLevelChange(reason));
        }

        private IEnumerator ActivateGameplayAfterLevelChange(string reason)
        {
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                if (HandleGameplayDetected(reason + " attempt " + attempt))
                {
                    pendingGameplayActivation = null;
                    yield break;
                }
                if (IsNonGameplayContext())
                {
                    break;
                }
            }

            if (debugLogging.Value)
            {
                IsGameplayLevelCandidate(out string details);
                Logger.LogInfo("Gameplay activation was not confirmed after " + reason + " (" + details + ").");
            }
            pendingGameplayActivation = null;
        }

        private bool HandleGameplayDetected(string reason)
        {
            if (!IsGameplayLevelCandidate(out string details))
            {
                if (debugLogging.Value)
                {
                    Logger.LogInfo("Skipped gameplay activation from " + reason + " (" + details + ").");
                }
                if (gameplayActive) HandleLevelChanging();
                return false;
            }

            if (gameplayActive)
            {
                return true;
            }

            if (debugLogging.Value)
            {
                Logger.LogInfo("Activating gameplay overlay from " + reason + " (" + details + ").");
            }
            ResetSeenMonsters("new level generated");
            mapValue = 0f;
            mapValueInitial = 0f;
            lostValue = 0f;
            valuablesInDollarHaul.Clear();
            lastMapValueFingerprint = "";
            mapValueInitial = mapValue;
            gameplayActive = true;

            int level = ResolveCurrentLevel();
            if (level > 0) SyncLevelToOverlay(level, ResolveCurrentLevelName());
            return true;
        }

        private void HandleLevelChanging()
        {
            if (!gameplayActive) return;
            gameplayActive = false;
            SyncMapValueIfChanged();
            ResetMapValue("level changing");
            StartCoroutine(PostVisibility(false));
        }

        private void ResetMapValue(string reason)
        {
            mapValue = 0f;
            mapValueInitial = 0f;
            lostValue = 0f;
            valuablesInDollarHaul.Clear();
            lastMapValueFingerprint = "";
            mapValueDirty = false;
            pendingMapValueRefresh = false;
            pendingMapValueRefreshReason = "";
            if (debugLogging.Value)
            {
                Logger.LogInfo("Reset map value: " + reason + ".");
            }
        }

        private void RefreshMapValue(string reason)
        {
            if (!IsRunLevel()) return;
            mapValue = CalculateMapValue();
            if (debugLogging.Value)
            {
                Logger.LogInfo("Refreshed map value after " + reason + ": " + mapValue.ToString("0", CultureInfo.InvariantCulture) + ".");
            }
            MarkMapValueDirty();
        }

        private void ScheduleMapValueRefresh(string reason)
        {
            if (!IsRunLevel()) return;
            pendingMapValueRefresh = true;
            pendingMapValueRefreshReason = reason;
            MarkMapValueDirty();
        }

        private void AddMapValue(float delta, string reason)
        {
            if (!IsRunLevel()) return;
            if (float.IsNaN(delta) || float.IsInfinity(delta) || Math.Abs(delta) < 0.01f) return;
            mapValue = Math.Max(0f, mapValue + delta);
            if (debugLogging.Value)
            {
                Logger.LogInfo("Updated map value from " + reason + ": delta=" + delta.ToString("0.#", CultureInfo.InvariantCulture)
                    + ", value=" + mapValue.ToString("0", CultureInfo.InvariantCulture) + ".");
            }
            MarkMapValueDirty();
        }

        private void AddLostValue(float value, string reason)
        {
            if (!IsRunLevel()) return;
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0.01f) return;
            lostValue += value;
            if (debugLogging.Value)
            {
                Logger.LogInfo("Updated lost value from " + reason + ": value=" + value.ToString("0.#", CultureInfo.InvariantCulture)
                    + ", total=" + lostValue.ToString("0", CultureInfo.InvariantCulture) + ".");
            }
            MarkMapValueDirty();
        }

        private void MarkMapValueDirty()
        {
            mapValueDirty = true;
            nextMapValueSyncAt = Time.realtimeSinceStartup + 0.2f;
        }

        private void SyncMapValueIfChanged()
        {
            mapValueDirty = false;
            if (pendingMapValueRefresh)
            {
                pendingMapValueRefresh = false;
                mapValue = CalculateMapValue();
                if (mapValueInitial <= 0f && mapValue > 0f)
                {
                    mapValueInitial = mapValue;
                }
                if (debugLogging.Value)
                {
                    string reason = string.IsNullOrWhiteSpace(pendingMapValueRefreshReason) ? "scheduled refresh" : pendingMapValueRefreshReason;
                    Logger.LogInfo("Refreshed map value after " + reason + ": " + mapValue.ToString("0", CultureInfo.InvariantCulture) + ".");
                }
                pendingMapValueRefreshReason = "";
            }

            int value = Math.Max(0, (int)Math.Round(mapValue));
            int initial = Math.Max(0, (int)Math.Round(mapValueInitial));
            int lost = Math.Max(0, (int)Math.Round(lostValue));
            int? goal = ResolveExtractionGoal();
            string fingerprint = value.ToString(CultureInfo.InvariantCulture) + ":" + initial.ToString(CultureInfo.InvariantCulture) + ":" + lost.ToString(CultureInfo.InvariantCulture) + ":" + (goal.HasValue ? goal.Value.ToString(CultureInfo.InvariantCulture) : "");
            if (fingerprint == lastMapValueFingerprint) return;
            lastMapValueFingerprint = fingerprint;
            StartCoroutine(PostMapValue(value, initial, lost, goal));
        }

        private void ResetSeenMonsters(string reason)
        {
            lock (seenLock)
            {
                seenMonsters.Clear();
            }
            lock (enemyLock)
            {
                knownEnemyParents.Clear();
                sentEnemyInstanceIds.Clear();
            }
            lastSeenLoggedAt.Clear();
            resolvedMonsterNames.Clear();
            sourceIdsByEnemyParent.Clear();
            lastAliveBySourceId.Clear();
            lastHealthDebugBySourceId.Clear();
            healthSourcesByRootId.Clear();
            lastImmediateStatusBySourceId.Clear();
            lastTimerStatusBySourceId.Clear();
            lastTimerStatusSentAtBySourceId.Clear();
            enemyHasVisionByParentId.Clear();
            enemyHasOnScreenByParentId.Clear();
            visionEnemyCacheByVisionId.Clear();
            pendingEncounterIds.Clear();
            cachedLocalPlayerViewId = int.MinValue;
            lastRosterFingerprint = "";
            lastStatusFingerprint = "";
            pendingRosterFingerprint = "";
            rosterStableScans = 0;
            broadEnemyDiscoveryAttempts = 0;
            rosterPublished = false;
            enemyRosterDirty = true;
            scanPausedUntil = Time.realtimeSinceStartup + 2f;
            nextScanAt = scanPausedUntil;
            nextStatusSyncAt = scanPausedUntil;
            nextUpgradeSyncAt = scanPausedUntil;
            nextDebugSummaryAt = 0f;
            nextBroadEnemyDiscoveryAt = scanPausedUntil;
            Logger.LogInfo("Cleared seen monsters: " + reason + ".");
        }

        private void SyncKnownEnemies()
        {
            List<EnemyCandidate> found = FindEnemyCandidates();
            var resolvedEnemies = new List<ResolvedEnemyCandidate>();
            int candidates = found.Count;

            foreach (EnemyCandidate candidate in found)
            {
                int instanceId = candidate.Root.GetInstanceID();
                if (!resolvedMonsterNames.TryGetValue(instanceId, out string monsterName))
                {
                    monsterName = ResolveMonsterName(candidate.Component);
                    if (monsterName != null) resolvedMonsterNames[instanceId] = monsterName;
                }
                if (monsterName == null)
                {
                    if (debugLogging.Value && Time.realtimeSinceStartup >= nextDebugSummaryAt)
                    {
                        Logger.LogInfo("Unresolved enemy candidate: " + candidate.Component.GetType().Name + " / " + candidate.Root.name);
                    }
                    continue;
                }
                resolvedEnemies.Add(new ResolvedEnemyCandidate { Candidate = candidate, MonsterName = monsterName });
            }

            if (resolvedEnemies.Count == 0)
            {
                enemyRosterDirty = false;
                return;
            }

            if (!SyncMonsterRoster(resolvedEnemies))
            {
                if (debugLogging.Value && Time.realtimeSinceStartup >= nextDebugSummaryAt)
                {
                    nextDebugSummaryAt = Time.realtimeSinceStartup + 30f;
                    Logger.LogInfo("Collecting monster roster: candidates=" + candidates + ", resolved=" + resolvedEnemies.Count + ".");
                }
                return;
            }
            SyncMonsterStatuses(resolvedEnemies);
            TryPublishPendingVisionEncounters(resolvedEnemies);
            enemyRosterDirty = false;

            if (debugLogging.Value && Time.realtimeSinceStartup >= nextDebugSummaryAt)
            {
                nextDebugSummaryAt = Time.realtimeSinceStartup + 30f;
                Logger.LogInfo("Enemy sync summary: candidates=" + candidates + ", resolved=" + resolvedEnemies.Count + ".");
            }
        }

        private bool SyncMonsterRoster(List<ResolvedEnemyCandidate> enemies)
        {
            enemies.Sort((left, right) => left.Candidate.Root.GetInstanceID().CompareTo(right.Candidate.Root.GetInstanceID()));
            var fingerprint = new StringBuilder();
            for (int index = 0; index < enemies.Count; index++)
            {
                ResolvedEnemyCandidate enemy = enemies[index];
                int instanceId = enemy.Candidate.Root.GetInstanceID();
                fingerprint.Append(instanceId).Append(':').Append(enemy.MonsterName).Append(';');
            }

            string nextFingerprint = fingerprint.ToString();
            if (!rosterPublished)
            {
                if (enemies.Count == 0) return false;
                if (nextFingerprint != pendingRosterFingerprint)
                {
                    pendingRosterFingerprint = nextFingerprint;
                    rosterStableScans = 0;
                    return false;
                }

                rosterStableScans++;
                if (rosterStableScans < 2) return false;
                rosterPublished = true;
            }

            if (nextFingerprint == lastRosterFingerprint) return true;
            lastRosterFingerprint = nextFingerprint;
            var json = new StringBuilder("{\"monsters\":[");
            for (int index = 0; index < enemies.Count; index++)
            {
                ResolvedEnemyCandidate enemy = enemies[index];
                if (index > 0) json.Append(',');
                json.Append("{\"id\":").Append(enemy.Candidate.Root.GetInstanceID()).Append(",\"name\":\"").Append(EscapeJson(enemy.MonsterName)).Append("\"}");
            }
            json.Append("]}");
            StartCoroutine(PostMonsterRoster(json.ToString()));
            return true;
        }

        private void SyncMonsterStatuses(List<ResolvedEnemyCandidate> enemies)
        {
            var json = new StringBuilder("{\"statuses\":[");
            var fingerprint = new StringBuilder();
            int statusCount = 0;
            var statusCandidates = new Dictionary<int, EnemyCandidate>();

            for (int index = 0; index < enemies.Count; index++)
            {
                ResolvedEnemyCandidate enemy = enemies[index];
                int sourceId = enemy.Candidate.Root.GetInstanceID();
                statusCandidates[sourceId] = enemy.Candidate;
                Component enemyParent = GetEnemyParent(enemy.Candidate);
                if (enemyParent != null) sourceIdsByEnemyParent[enemyParent.GetInstanceID()] = sourceId;
            }

            foreach (Component enemyParent in GetKnownEnemyParentsSnapshot())
            {
                if (enemyParent == null) continue;
                AddStatusCandidate(statusCandidates, enemyParent);
            }

            foreach (Component enemyParent in FindSpawnedEnemiesFromDirector())
            {
                if (enemyParent == null) continue;
                RegisterEnemyParent(enemyParent);
                AddStatusCandidate(statusCandidates, enemyParent);
            }

            var sourceIds = new List<int>(statusCandidates.Keys);
            sourceIds.Sort();
            for (int index = 0; index < sourceIds.Count; index++)
            {
                int instanceId = sourceIds[index];
                EnemyCandidate candidate = statusCandidates[instanceId];
                if (!TryGetEnemyRespawnStatus(candidate, out bool alive, out float remaining)) continue;
                float health = 0f;
                float maxHealth = 0f;
                bool hasHealth = IsEnemySent(instanceId) && TryGetEnemyHealth(candidate, out health, out maxHealth);

                if (!lastAliveBySourceId.TryGetValue(instanceId, out bool previousAlive) || previousAlive != alive)
                {
                    lastAliveBySourceId[instanceId] = alive;
                    if (debugLogging.Value)
                    {
                        Logger.LogInfo("Monster respawn state " + instanceId + ": " + (alive ? "alive" : "cooldown")
                            + ", remaining=" + remaining.ToString("0.0", CultureInfo.InvariantCulture) + "s.");
                    }
                }

                float roundedRemaining = alive ? 0f : (float)Math.Ceiling(Math.Max(0f, remaining) * 10f) / 10f;
                string remainingText = roundedRemaining.ToString("0.0", CultureInfo.InvariantCulture);
                string healthText = hasHealth ? Math.Max(0f, health).ToString("0.#", CultureInfo.InvariantCulture) : "";
                string maxHealthText = hasHealth && maxHealth > 0f ? maxHealth.ToString("0.#", CultureInfo.InvariantCulture) : "";
                if (debugLogging.Value && hasHealth)
                {
                    string healthDebug = healthText + "/" + (maxHealthText.Length > 0 ? maxHealthText : "?");
                    if (!lastHealthDebugBySourceId.TryGetValue(instanceId, out string previousHealthDebug) || previousHealthDebug != healthDebug)
                    {
                        lastHealthDebugBySourceId[instanceId] = healthDebug;
                        Logger.LogInfo("Monster health state " + instanceId + ": hp=" + healthDebug + ".");
                    }
                }
                fingerprint.Append(instanceId).Append(':').Append(alive ? '1' : '0').Append(':').Append(remainingText)
                    .Append(':').Append(healthText).Append('/').Append(maxHealthText).Append(';');

                if (statusCount++ > 0) json.Append(',');
                json.Append("{\"id\":").Append(instanceId)
                    .Append(",\"alive\":").Append(alive ? "true" : "false")
                    .Append(",\"respawnRemaining\":").Append(remainingText);
                if (hasHealth)
                {
                    json.Append(",\"health\":").Append(healthText.Length > 0 ? healthText : "0");
                    if (maxHealthText.Length > 0) json.Append(",\"maxHealth\":").Append(maxHealthText);
                }
                json.Append('}');
            }

            if (statusCount == 0) return;
            string nextFingerprint = fingerprint.ToString();
            if (nextFingerprint == lastStatusFingerprint) return;
            lastStatusFingerprint = nextFingerprint;
            json.Append("]}");
            StartCoroutine(PostMonsterStatuses(json.ToString()));
        }

        private static bool TryGetEnemyRespawnStatus(EnemyCandidate candidate, out bool alive, out float remaining)
        {
            alive = true;
            remaining = 0f;
            Component enemyParent = GetEnemyParent(candidate);
            if (enemyParent == null) return false;

            object timerValue = ReadMember(enemyParent, "DespawnedTimer");
            if (!TryConvertFloat(timerValue, out remaining)) return false;

            remaining = Math.Max(0f, remaining);
            alive = ReadEnemySpawned(enemyParent);
            return true;
        }

        private void AddStatusCandidate(Dictionary<int, EnemyCandidate> statusCandidates, Component enemyParent)
        {
            GameObject root = GetEnemyRoot(enemyParent);
            int parentId = enemyParent.GetInstanceID();
            int sourceId;
            if (root != null)
            {
                sourceId = root.GetInstanceID();
                sourceIdsByEnemyParent[parentId] = sourceId;
            }
            else if (!sourceIdsByEnemyParent.TryGetValue(parentId, out sourceId))
            {
                return;
            }

            statusCandidates[sourceId] = new EnemyCandidate
            {
                Component = enemyParent,
                Root = root,
                Center = Vector3.zero
            };
        }

        private void SyncEnemyHealthChanged(object enemyHealthSource)
        {
            if (!gameplayActive || enemyHealthSource == null) return;

            Component enemy = ReadMember(enemyHealthSource, "enemy") as Component;
            Component enemyParent = ReadMember(enemy, "EnemyParent") as Component;
            SyncEnemyParentStatusChanged(enemyParent);
        }

        private void ScheduleEnemyParentStatusChanged(Component enemyParent)
        {
            if (!gameplayActive || enemyParent == null) return;
            StartCoroutine(SyncEnemyParentStatusChangedNextFrame(enemyParent));
        }

        private IEnumerator SyncEnemyParentStatusChangedNextFrame(Component enemyParent)
        {
            yield return null;
            SyncEnemyParentStatusChanged(enemyParent);
        }

        private void SyncEnemyParentTimerChanged(Component enemyParent)
        {
            if (!gameplayActive || enemyParent == null) return;
            GameObject root = GetEnemyRoot(enemyParent);
            if (root == null) return;

            int instanceId = root.GetInstanceID();
            if (!IsEnemySent(instanceId)) return;

            var candidate = new EnemyCandidate
            {
                Component = enemyParent,
                Root = root,
                Center = Vector3.zero
            };

            if (!TryGetEnemyRespawnStatus(candidate, out bool alive, out float remaining)) return;

            float roundedRemaining = alive ? 0f : (float)Math.Ceiling(Math.Max(0f, remaining) * 10f) / 10f;
            string remainingText = roundedRemaining.ToString("0.0", CultureInfo.InvariantCulture);
            string fingerprint = instanceId + ":" + (alive ? "1" : "0") + ":" + remainingText;
            bool aliveChanged = !lastAliveBySourceId.TryGetValue(instanceId, out bool previousAlive) || previousAlive != alive;
            if (!aliveChanged
                && lastTimerStatusBySourceId.TryGetValue(instanceId, out string previousFingerprint)
                && previousFingerprint == fingerprint)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (!aliveChanged
                && lastTimerStatusSentAtBySourceId.TryGetValue(instanceId, out float lastSentAt)
                && now - lastSentAt < 0.5f)
            {
                return;
            }

            lastAliveBySourceId[instanceId] = alive;
            lastTimerStatusBySourceId[instanceId] = fingerprint;
            lastTimerStatusSentAtBySourceId[instanceId] = now;

            var json = new StringBuilder("{\"statuses\":[{\"id\":")
                .Append(instanceId)
                .Append(",\"alive\":").Append(alive ? "true" : "false")
                .Append(",\"respawnRemaining\":").Append(remainingText)
                .Append("}]}");
            StartCoroutine(PostMonsterStatuses(json.ToString()));
        }

        private void SyncEnemyParentStatusChanged(Component enemyParent)
        {
            if (!gameplayActive || enemyParent == null) return;
            GameObject root = GetEnemyRoot(enemyParent);
            if (root == null) return;

            int instanceId = root.GetInstanceID();

            var candidate = new EnemyCandidate
            {
                Component = enemyParent,
                Root = root,
                Center = Vector3.zero
            };

            if (!TryGetEnemyRespawnStatus(candidate, out bool alive, out float remaining)) return;
            bool shouldIncludeHealth = IsEnemySent(instanceId);
            float health = 0f;
            float maxHealth = 0f;
            bool hasHealth = shouldIncludeHealth && TryGetEnemyHealth(candidate, out health, out maxHealth);

            float roundedRemaining = alive ? 0f : (float)Math.Ceiling(Math.Max(0f, remaining) * 10f) / 10f;
            string remainingText = roundedRemaining.ToString("0.0", CultureInfo.InvariantCulture);
            string healthText = hasHealth ? Math.Max(0f, health).ToString("0.#", CultureInfo.InvariantCulture) : "";
            string maxHealthText = hasHealth && maxHealth > 0f ? maxHealth.ToString("0.#", CultureInfo.InvariantCulture) : "";
            string fingerprint = instanceId + ":" + (alive ? "1" : "0") + ":" + remainingText + ":" + healthText + "/" + maxHealthText;
            if (lastImmediateStatusBySourceId.TryGetValue(instanceId, out string previousFingerprint)
                && previousFingerprint == fingerprint)
            {
                return;
            }
            lastImmediateStatusBySourceId[instanceId] = fingerprint;

            var json = new StringBuilder("{\"statuses\":[{\"id\":")
                .Append(instanceId)
                .Append(",\"alive\":").Append(alive ? "true" : "false")
                .Append(",\"respawnRemaining\":").Append(remainingText);
            if (hasHealth)
            {
                json.Append(",\"health\":").Append(healthText.Length > 0 ? healthText : "0");
                if (maxHealthText.Length > 0) json.Append(",\"maxHealth\":").Append(maxHealthText);
            }
            json.Append("}]}");
            StartCoroutine(PostMonsterStatuses(json.ToString()));
        }

        private static bool ReadEnemySpawned(Component enemyParent)
        {
            object spawnedValue = ReadMember(enemyParent, "Spawned");
            if (spawnedValue is bool spawned) return spawned;
            return enemyParent.gameObject != null && enemyParent.gameObject.activeInHierarchy;
        }

        private bool TryGetEnemyHealth(EnemyCandidate candidate, out float health, out float maxHealth)
        {
            health = 0f;
            maxHealth = 0f;
            bool hasHealth = false;

            foreach (object source in GetEnemyHealthSources(candidate))
            {
                if (source == null) continue;

                float currentValue;
                if (!hasHealth && TryReadFirstFloat(source, CurrentHealthMemberNames, out currentValue))
                {
                    health = Math.Max(0f, currentValue);
                    hasHealth = true;
                }

                float maxValue;
                if (maxHealth <= 0f && TryReadFirstFloat(source, MaxHealthMemberNames, out maxValue))
                {
                    maxHealth = Math.Max(0f, maxValue);
                }

                if (hasHealth && maxHealth > 0f) return true;
            }

            if (!hasHealth && maxHealth > 0f)
            {
                health = maxHealth;
                return true;
            }

            return hasHealth;
        }

        private IEnumerable<object> GetEnemyHealthSources(EnemyCandidate candidate)
        {
            if (candidate.Root != null)
            {
                int rootId = candidate.Root.GetInstanceID();
                if (!healthSourcesByRootId.TryGetValue(rootId, out object[] cachedSources))
                {
                    var sources = new List<object>();
                    Component enemy = candidate.Root.GetComponent("Enemy");
                    object linkedHealth = ReadMember(enemy, "Health");
                    if (linkedHealth != null) sources.Add(linkedHealth);

                    Component enemyHealth = candidate.Root.GetComponent("EnemyHealth");
                    if (enemyHealth != null && !sources.Contains(enemyHealth))
                    {
                        sources.Add(enemyHealth);
                    }

                    if (enemy != null && !sources.Contains(enemy)) sources.Add(enemy);
                    cachedSources = sources.ToArray();
                    healthSourcesByRootId[rootId] = cachedSources;
                }

                for (int index = 0; index < cachedSources.Length; index++)
                {
                    object source = cachedSources[index];
                    if (source is UnityEngine.Object unityObject && unityObject == null) continue;
                    yield return source;
                }
            }

            if (candidate.Component != null) yield return candidate.Component;

            Component enemyParent = GetEnemyParent(candidate);
            if (enemyParent != null) yield return enemyParent;

            if (candidate.Root == null) yield break;

            yield break;
        }

        private static bool TryReadFirstFloat(object source, string[] memberNames, out float value)
        {
            for (int index = 0; index < memberNames.Length; index++)
            {
                object rawValue = ReadMember(source, memberNames[index]);
                if (rawValue == null) continue;
                if (TryConvertFloat(rawValue, out value)) return true;
            }

            value = 0f;
            return false;
        }

        private static Component GetEnemyParent(EnemyCandidate candidate)
        {
            if (candidate.Component != null && candidate.Component.GetType().Name == "EnemyParent")
            {
                return candidate.Component;
            }

            Component enemy = candidate.Root == null ? null : candidate.Root.GetComponent("Enemy");
            return ReadMember(enemy ?? candidate.Component, "EnemyParent") as Component;
        }

        private static bool TryConvertFloat(object value, out float result)
        {
            try
            {
                result = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                result = 0f;
                return false;
            }
        }

        private static bool TryMarkEnemySent(int instanceId)
        {
            lock (enemyLock)
            {
                return sentEnemyInstanceIds.Add(instanceId);
            }
        }

        private static bool IsEnemySent(int instanceId)
        {
            lock (enemyLock)
            {
                return sentEnemyInstanceIds.Contains(instanceId);
            }
        }

        private void MarkMonsterSeen(string monsterName, EnemyCandidate candidate)
        {
            bool added = false;
            lock (seenLock)
            {
                if (!seenMonsters.Contains(monsterName))
                {
                    seenMonsters.Add(monsterName);
                    added = true;
                }
            }

            if (added || ShouldLogSeen(monsterName))
            {
                lastSeenLoggedAt[monsterName] = Time.realtimeSinceStartup;
                Logger.LogInfo("Marked monster for web overlay: " + monsterName + " from " + candidate.Component.GetType().Name + " / " + candidate.Root.name);
            }
        }

        private bool ShouldLogSeen(string monsterName)
        {
            if (!debugLogging.Value) return false;
            if (!lastSeenLoggedAt.TryGetValue(monsterName, out float lastLogged)) return true;
            return Time.realtimeSinceStartup - lastLogged >= 30f;
        }

        private int ResolveCurrentLevel()
        {
            Type runManagerType = AccessTools.TypeByName("RunManager");
            object runManager = ReadMember(runManagerType, "instance");
            object levelsCompletedValue = ReadMember(runManager, "levelsCompleted");
            if (levelsCompletedValue != null)
            {
                try
                {
                    int level = Math.Max(1, Convert.ToInt32(levelsCompletedValue) + 1);
                    fallbackLevel = level;
                    Logger.LogInfo("Resolved overlay level from completed levels: " + level + ".");
                    return level;
                }
                catch (Exception error)
                {
                    Logger.LogWarning("Could not convert RunManager.levelsCompleted: " + error.Message);
                }
            }

            fallbackLevel = Math.Max(1, lastSyncedLevel + 1);
            Logger.LogInfo("Using generated-level counter for overlay level: " + fallbackLevel + ".");
            return fallbackLevel;
        }

        private static bool IsRegularGameplayLevel()
        {
            Type runManagerType = AccessTools.TypeByName("RunManager");
            object runManager = ReadMember(runManagerType, "instance");
            object currentLevel = ReadMember(runManager, "levelCurrent");
            object levelsValue = ReadMember(runManager, "levels");
            if (currentLevel != null && levelsValue is IList levels && levels.Contains(currentLevel)) return true;
            if (IsNamedRunLevel(currentLevel)) return true;
            return IsRunLevel();
        }

        private static bool IsGameplayLevelCandidate(out string details, bool allowExpensiveFallback = false)
        {
            Type runManagerType = AccessTools.TypeByName("RunManager");
            object runManager = ReadMember(runManagerType, "instance");
            object currentLevel = ReadMember(runManager, "levelCurrent");
            object levelsValue = ReadMember(runManager, "levels");
            string currentLevelName = DescribeLevelObject(currentLevel);
            bool nonGameplayCurrent = IsNonGameplayLevelName(currentLevelName);
            bool listedLevel = !nonGameplayCurrent && currentLevel != null && levelsValue is IList levels && levels.Contains(currentLevel);
            bool namedLevel = IsNamedRunLevel(currentLevel);
            bool runLevel = !nonGameplayCurrent && IsRunLevel();
            bool hasLevelGenerator = false;
            bool levelGenerated = IsLevelGenerated();
            string activeSceneName = SceneManager.GetActiveScene().name;
            bool nonGameplayScene = IsNonGameplayLevelName(activeSceneName);
            string activeNamedLevelObject = "";
            if (nonGameplayCurrent || nonGameplayScene)
            {
                levelGenerated = false;
            }
            else if (allowExpensiveFallback)
            {
                hasLevelGenerator = HasActiveLevelGenerator();
                activeNamedLevelObject = FindActiveNamedRunLevelObject();
            }
            details = "current=" + currentLevelName
                + ", listed=" + listedLevel
                + ", named=" + namedLevel
                + ", semiRunLevel=" + runLevel
                + ", levelGenerator=" + hasLevelGenerator
                + ", generated=" + levelGenerated
                + ", scene=" + (string.IsNullOrWhiteSpace(activeSceneName) ? "<none>" : activeSceneName)
                + ", levelObject=" + (string.IsNullOrWhiteSpace(activeNamedLevelObject) ? "<none>" : activeNamedLevelObject);
            if (nonGameplayCurrent || nonGameplayScene) return false;
            return levelGenerated
                && (listedLevel || namedLevel || runLevel || hasLevelGenerator || IsRunLevelName(activeSceneName) || !string.IsNullOrWhiteSpace(activeNamedLevelObject));
        }

        private static bool HasActiveLevelGenerator()
        {
            Type levelGeneratorType = AccessTools.TypeByName("LevelGenerator");
            if (levelGeneratorType == null) return false;
            UnityEngine.Object[] generators = Resources.FindObjectsOfTypeAll(levelGeneratorType);
            for (int index = 0; index < generators.Length; index++)
            {
                if (generators[index] is Component component && component.gameObject != null && component.gameObject.scene.IsValid())
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsLevelGenerated()
        {
            Type levelGeneratorType = AccessTools.TypeByName("LevelGenerator");
            object levelGenerator = ReadMember(levelGeneratorType, "Instance") ?? ReadMember(levelGeneratorType, "instance");
            object generated = ReadMember(levelGenerator, "Generated");
            return generated is bool value && value;
        }

        private static string FindActiveNamedRunLevelObject()
        {
            UnityEngine.Object[] gameObjects = Resources.FindObjectsOfTypeAll(typeof(GameObject));
            for (int index = 0; index < gameObjects.Length; index++)
            {
                if (!(gameObjects[index] is GameObject gameObject)) continue;
                if (!gameObject.scene.IsValid()) continue;
                if (!IsRunLevelName(gameObject.name)) continue;
                return gameObject.name;
            }
            return "";
        }

        private static bool IsNamedRunLevel(object currentLevel)
        {
            if (currentLevel == null) return false;
            string levelName = DescribeLevelObject(currentLevel);
            return IsRunLevelName(levelName);
        }

        private static bool IsRunLevelName(string levelName)
        {
            return !string.IsNullOrWhiteSpace(levelName)
                && levelName.StartsWith("Level - ", StringComparison.Ordinal)
                && !IsNonGameplayLevelName(levelName);
        }

        private static bool IsNonGameplayLevelName(string levelName)
        {
            return !string.IsNullOrWhiteSpace(levelName)
                && (levelName.IndexOf("Lobby", StringComparison.OrdinalIgnoreCase) >= 0
                    || levelName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0
                    || levelName.IndexOf("Shop", StringComparison.OrdinalIgnoreCase) >= 0
                    || levelName.IndexOf("Splash", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsNonGameplayContext()
        {
            Type runManagerType = AccessTools.TypeByName("RunManager");
            object runManager = ReadMember(runManagerType, "instance");
            string currentLevelName = DescribeLevelObject(ReadMember(runManager, "levelCurrent"));
            string activeSceneName = SceneManager.GetActiveScene().name;
            return IsNonGameplayLevelName(currentLevelName) || IsNonGameplayLevelName(activeSceneName);
        }

        private static string DescribeCurrentLevel(object runManager)
        {
            return DescribeLevelObject(ReadMember(runManager, "levelCurrent"));
        }

        private static string DescribeLevelObject(object currentLevel)
        {
            if (currentLevel == null) return "<null>";
            if (currentLevel is UnityEngine.Object unityObject) return string.IsNullOrWhiteSpace(unityObject.name) ? unityObject.ToString() : unityObject.name;
            return currentLevel.ToString();
        }

        private static bool IsRunLevel()
        {
            Type semiFuncType = AccessTools.TypeByName("SemiFunc");
            object result = InvokeNoArgMethod(semiFuncType, "RunIsLevel");
            return result is bool value && value;
        }

        private static bool IsMasterClientOrSingleplayer()
        {
            Type semiFuncType = AccessTools.TypeByName("SemiFunc");
            object result = InvokeNoArgMethod(semiFuncType, "IsMasterClientOrSingleplayer");
            return !(result is bool value) || value;
        }

        private static float CalculateMapValue()
        {
            Type valuableType = AccessTools.TypeByName("ValuableObject");
            if (valuableType == null) return 0f;

            float total = 0f;
            UnityEngine.Object[] valuables = UnityEngine.Object.FindObjectsOfType(valuableType);
            for (int index = 0; index < valuables.Length; index++)
            {
                total += ReadValuableCurrentValue(valuables[index]);
            }
            return Math.Max(0f, total);
        }

        private static int? ResolveExtractionGoal()
        {
            Type roundDirectorType = AccessTools.TypeByName("RoundDirector");
            object roundDirector = ReadMember(roundDirectorType, "instance");
            object goal = ReadMember(roundDirector, "extractionHaulGoal");
            if (goal == null) return null;
            try
            {
                return Math.Max(0, Convert.ToInt32(goal));
            }
            catch
            {
                return null;
            }
        }

        private static Component GetValuableComponent(object source)
        {
            if (source is Component component)
            {
                Component valuable = component.GetComponent("ValuableObject");
                if (valuable != null) return valuable;
            }
            if (source is GameObject gameObject)
            {
                return gameObject.GetComponent("ValuableObject");
            }
            return null;
        }

        private static float ReadValuableCurrentValue(object source)
        {
            return ReadFloatMember(GetValuableSource(source), "dollarValueCurrent");
        }

        private static float ReadValuableOriginalValue(object source)
        {
            return ReadFloatMember(GetValuableSource(source), "dollarValueOriginal");
        }

        private static void TrackDollarHaulValuable(object source, bool inHaul)
        {
            if (!IsRunLevel()) return;
            int key = GetValuableKey(source);
            if (key == 0) return;
            if (inHaul) valuablesInDollarHaul.Add(key);
            else valuablesInDollarHaul.Remove(key);
        }

        private static bool IsValuableInDollarHaul(object source)
        {
            int key = GetValuableKey(source);
            if (key != 0 && valuablesInDollarHaul.Contains(key)) return true;

            object roundDirector = ReadMember(AccessTools.TypeByName("RoundDirector"), "instance");
            if (!(ReadMember(roundDirector, "dollarHaulList") is IEnumerable dollarHaulList)) return false;

            foreach (object item in dollarHaulList)
            {
                if (item == null) continue;
                int itemKey = GetValuableKey(item);
                if (key != 0 && itemKey == key) return true;
                if (ReferencesSameUnityObject(source, item)) return true;
            }

            return false;
        }

        private static int GetValuableKey(object source)
        {
            object valuable = GetValuableSource(source);
            if (valuable == null) return 0;

            object photonView = ReadMember(valuable, "photonView");
            int photonViewId = ReadIntMember(photonView, "ViewID");
            if (photonViewId > 0) return photonViewId;

            if (valuable is UnityEngine.Object unityObject) return unityObject.GetInstanceID();
            return 0;
        }

        private static bool ReferencesSameUnityObject(object left, object right)
        {
            int leftId = GetUnityObjectId(left);
            if (leftId == 0) return false;

            if (leftId == GetUnityObjectId(right)) return true;

            Component leftValuable = GetValuableComponent(left);
            Component rightValuable = GetValuableComponent(right);
            if (leftValuable != null && rightValuable != null && leftValuable.GetInstanceID() == rightValuable.GetInstanceID()) return true;

            GameObject leftGameObject = GetGameObject(left);
            GameObject rightGameObject = GetGameObject(right);
            return leftGameObject != null && rightGameObject != null && leftGameObject.GetInstanceID() == rightGameObject.GetInstanceID();
        }

        private static int GetUnityObjectId(object source)
        {
            if (source is UnityEngine.Object unityObject) return unityObject.GetInstanceID();
            return 0;
        }

        private static GameObject GetGameObject(object source)
        {
            if (source is GameObject gameObject) return gameObject;
            if (source is Component component) return component.gameObject;
            return null;
        }

        private static object GetValuableSource(object source)
        {
            Component valuable = GetValuableComponent(source);
            return valuable != null ? valuable : source;
        }

        private static float ReadFloatMember(object source, string memberName)
        {
            object value = ReadMember(source, memberName);
            if (value == null) return 0f;
            try
            {
                return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0f;
            }
        }

        private static int ReadIntMember(object source, string memberName)
        {
            object value = ReadMember(source, memberName);
            if (value == null) return 0;
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private string ResolveCurrentLevelName()
        {
            Type runManagerType = AccessTools.TypeByName("RunManager");
            object runManager = ReadMember(runManagerType, "instance");
            object currentLevel = ReadMember(runManager, "levelCurrent");
            string levelName = DescribeLevelObject(currentLevel);
            return levelName == "<null>" ? "" : levelName;
        }

        private void SyncLevelToOverlay(int level, string levelName)
        {
            lastSyncedLevel = level;
            StartCoroutine(PostLevel(level, levelName));
            lastSyncedUpgrades.Clear();
            pendingUpgradeKeys.Clear();
            for (int index = 0; index < TrackedPlayerUpgrades.Length; index++)
            {
                pendingUpgradeKeys.Add(TrackedPlayerUpgrades[index].Key);
            }
            nextUpgradeSyncAt = Time.realtimeSinceStartup + 2f;
        }

        private void SyncPlayerUpgradeValueIfLocal(string stateKey, string steamId, int value)
        {
            if (!gameplayActive || string.IsNullOrEmpty(stateKey)) return;

            string localSteamId = ResolveLocalPlayerSteamId();
            if (!string.IsNullOrEmpty(localSteamId) && !string.Equals(localSteamId, steamId, StringComparison.Ordinal)) return;

            int normalizedValue = Math.Max(0, value);
            if (lastSyncedUpgrades.TryGetValue(stateKey, out int previousValue) && previousValue == normalizedValue) return;

            pendingUpgradeKeys.Remove(stateKey);
            lastSyncedUpgrades[stateKey] = normalizedValue;
            var json = new StringBuilder("{\"upgrades\":{\"")
                .Append(stateKey)
                .Append("\":")
                .Append(normalizedValue)
                .Append("}}");
            StartCoroutine(PostPlayerUpgrades(json.ToString(), 1));
        }

        private void SyncTabStateIfChanged()
        {
            bool tabHeld = Input.GetKey(KeyCode.Tab);
            if (lastTabHeld.HasValue && lastTabHeld.Value == tabHeld) return;
            lastTabHeld = tabHeld;
            StartCoroutine(PostTabHidden(tabHeld));
        }

        private void SyncPlayerUpgradesIfChanged(HashSet<string> onlyKeys = null)
        {
            var json = new StringBuilder("{\"upgrades\":{");
            int changedCount = 0;
            for (int index = 0; index < TrackedPlayerUpgrades.Length; index++)
            {
                KeyValuePair<string, string> binding = TrackedPlayerUpgrades[index];
                if (onlyKeys != null && !onlyKeys.Contains(binding.Key)) continue;
                int? value = ResolveLocalPlayerUpgrade(binding.Value);
                if (!value.HasValue) continue;
                if (lastSyncedUpgrades.TryGetValue(binding.Key, out int previousValue) && previousValue == value.Value) continue;

                lastSyncedUpgrades[binding.Key] = value.Value;
                if (changedCount++ > 0) json.Append(',');
                json.Append('\"').Append(binding.Key).Append("\":").Append(value.Value);
            }

            if (changedCount == 0) return;
            json.Append("}}");
            StartCoroutine(PostPlayerUpgrades(json.ToString(), changedCount));
        }

        private static int? ResolveLocalPlayerUpgrade(string memberName)
        {
            Type statsManagerType = AccessTools.TypeByName("StatsManager");
            object statsManager = ReadMember(statsManagerType, "instance");
            object upgradesValue = ReadMember(statsManager, memberName);
            if (!(upgradesValue is IDictionary upgrades)) return null;

            string steamId = ResolveLocalPlayerSteamId();

            object value = !string.IsNullOrEmpty(steamId) && upgrades.Contains(steamId) ? upgrades[steamId] : null;
            if (value == null && upgrades.Count == 1)
            {
                foreach (DictionaryEntry entry in upgrades)
                {
                    value = entry.Value;
                    break;
                }
            }

            if (value == null) return null;
            try
            {
                return Math.Max(0, Convert.ToInt32(value));
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveLocalPlayerSteamId()
        {
            Type playerControllerType = AccessTools.TypeByName("PlayerController");
            object playerController = ReadMember(playerControllerType, "instance");
            string steamId = ReadMember(playerController, "playerSteamID") as string;
            if (!string.IsNullOrEmpty(steamId)) return steamId;

            object playerAvatar = ReadMember(playerController, "playerAvatarScript");
            return ReadMember(playerAvatar, "steamID") as string;
        }

        private static Camera FindBestCamera()
        {
            if (Camera.main != null) return Camera.main;

            Camera[] cameras = Camera.allCameras;
            Camera best = null;
            float bestDepth = float.MinValue;
            foreach (Camera camera in cameras)
            {
                if (camera == null || !camera.isActiveAndEnabled) continue;
                if (camera.depth >= bestDepth)
                {
                    best = camera;
                    bestDepth = camera.depth;
                }
            }

            return best;
        }

        private List<EnemyCandidate> FindEnemyCandidates()
        {
            var result = new List<EnemyCandidate>();
            var seenRoots = new HashSet<int>();

            foreach (Component component in GetKnownEnemyParentsSnapshot())
            {
                AddEnemyCandidate(result, seenRoots, component);
            }

            foreach (Component component in FindSpawnedEnemiesFromDirector())
            {
                AddEnemyCandidate(result, seenRoots, component);
            }

            float now = Time.realtimeSinceStartup;
            bool runBroadDiscovery = !rosterPublished
                && result.Count == 0
                && broadEnemyDiscoveryAttempts < 3
                && now >= nextBroadEnemyDiscoveryAt;
            if (runBroadDiscovery)
            {
                broadEnemyDiscoveryAttempts++;
                nextBroadEnemyDiscoveryAt = now + 5f;
                foreach (Component component in FindComponentsByTypeName("EnemyParent"))
                {
                    AddEnemyCandidate(result, seenRoots, component);
                }
                if (result.Count == 0)
                {
                    foreach (Component component in FindComponentsByTypeName("Enemy"))
                    {
                        AddEnemyCandidate(result, seenRoots, component);
                    }
                }
            }

            return result;
        }

        private static void RegisterEnemyParent(Component component)
        {
            if (component == null) return;
            lock (enemyLock)
            {
                knownEnemyParents.RemoveAll((enemy) => enemy == null);
                if (!knownEnemyParents.Contains(component))
                {
                    knownEnemyParents.Add(component);
                    if (Plugin.instance != null)
                    {
                        Plugin.instance.enemyRosterDirty = true;
                        Plugin.instance.nextScanAt = 0f;
                    }
                    if (Plugin.instance?.debugLogging.Value == true)
                    {
                        Plugin.instance.Logger.LogInfo("Registered spawned enemy parent: " + component.name + ".");
                    }
                }
            }
        }

        private static List<Component> GetKnownEnemyParentsSnapshot()
        {
            lock (enemyLock)
            {
                knownEnemyParents.RemoveAll((enemy) => enemy == null);
                return new List<Component>(knownEnemyParents);
            }
        }

        private static IEnumerable<Component> FindSpawnedEnemiesFromDirector(bool allowExpensiveFallback = false)
        {
            Type directorType = Type.GetType("EnemyDirector, Assembly-CSharp");
            if (directorType == null) yield break;

            object instance = ReadMember(directorType, "instance");
            if (instance != null)
            {
                foreach (Component component in EnumerateSpawnedEnemies(instance))
                {
                    yield return component;
                }
                yield break;
            }

            if (!allowExpensiveFallback) yield break;

            UnityEngine.Object[] directors = Resources.FindObjectsOfTypeAll(directorType);
            foreach (UnityEngine.Object director in directors)
            {
                foreach (Component component in EnumerateSpawnedEnemies(director))
                {
                    yield return component;
                }
            }
        }

        private static IEnumerable<Component> EnumerateSpawnedEnemies(object director)
        {
            object spawned = ReadMember(director, "enemiesSpawned");
            if (!(spawned is IEnumerable enumerable)) yield break;

            foreach (object item in enumerable)
            {
                if (item is Component component)
                {
                    yield return component;
                }
            }
        }

        private static IEnumerable<Component> FindComponentsByTypeName(string typeName)
        {
            Type type = Type.GetType(typeName + ", Assembly-CSharp");
            if (type == null || !typeof(Component).IsAssignableFrom(type))
            {
                yield break;
            }

            UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(type);
            foreach (UnityEngine.Object obj in objects)
            {
                if (obj is Component component)
                {
                    yield return component;
                }
            }
        }

        private static void AddEnemyCandidate(List<EnemyCandidate> result, HashSet<int> seenRoots, Component component)
        {
            if (component == null) return;

            GameObject root = GetEnemyRoot(component);
            if (root == null || !root.activeInHierarchy) return;

            Component enemyParent = component.GetType().Name == "EnemyParent"
                ? component
                : ReadMember(component, "EnemyParent") as Component;
            if (enemyParent != null) RegisterEnemyParent(enemyParent);

            int id = root.GetInstanceID();
            if (!seenRoots.Add(id)) return;

            result.Add(new EnemyCandidate
            {
                Component = component,
                Root = root,
                Center = GetObjectCenter(root)
            });
        }

        private static bool LooksLikeEnemyComponent(Component component)
        {
            string typeName = component.GetType().Name;
            if (typeName == "EnemyParent" || typeName == "Enemy" || typeName == "EnemyAvatar") return true;
            if (typeName.StartsWith("Enemy", StringComparison.OrdinalIgnoreCase)) return true;

            return FindKnownMonster(component.gameObject.name) != null;
        }

        private static GameObject GetEnemyRoot(Component component)
        {
            if (component.GetType().Name == "Enemy")
            {
                return component.gameObject;
            }

            if (component.GetType().Name == "EnemyParent")
            {
                object linkedEnemy = ReadMember(component, "Enemy");
                return linkedEnemy is Component enemyComponent ? enemyComponent.gameObject : null;
            }

            return component.gameObject;
        }

        private static Vector3 GetObjectCenter(GameObject root)
        {
            Component enemy = root.GetComponent("Enemy");
            if (enemy != null)
            {
                object centerTransform = ReadMember(enemy, "CenterTransform");
                if (centerTransform is Transform transform)
                {
                    return transform.position;
                }
            }

            Renderer renderer = root.GetComponentInChildren<Renderer>();
            if (renderer != null) return renderer.bounds.center;

            Collider collider = root.GetComponentInChildren<Collider>();
            if (collider != null) return collider.bounds.center;

            return root.transform.position + Vector3.up;
        }

        private static string ResolveMonsterName(Component component)
        {
            if (component == null) return null;
            Component enemyParent = null;
            if (component.GetType().Name == "EnemyParent")
            {
                enemyParent = component;
            }
            else
            {
                enemyParent = ReadMember(component, "EnemyParent") as Component;
            }

            if (enemyParent == null) return null;
            string enemyName = ReadMember(enemyParent, "enemyName") as string;
            return FindKnownMonster(enemyName);
        }

        private static bool ShouldUseLegacyVisionFallback(Component enemyParent)
        {
            string monsterName = ResolveMonsterName(enemyParent);
            return monsterName != null && PlayerVisionLegacyFallbackMonsters.Contains(monsterName);
        }

        private bool IsEnemyParentPlayerVeryClose(Component enemyParent)
        {
            object playerVeryClose = ReadMember(enemyParent, "playerVeryClose");
            return playerVeryClose is bool isPlayerVeryClose && isPlayerVeryClose;
        }

        private bool IsEnemyOnScreenVisible(Component enemyOnScreen)
        {
            object onScreenLocal = ReadMember(enemyOnScreen, "OnScreenLocal");
            object culledLocal = ReadMember(enemyOnScreen, "CulledLocal");
            return onScreenLocal is bool isOnScreen && isOnScreen
                && (!(culledLocal is bool isCulled) || !isCulled);
        }

        private static IEnumerator WatchBlindEnemyPlayerClose(IEnumerator inner, Component enemyParent)
        {
            bool wasPlayerVeryClose = instance != null && instance.IsEnemyParentPlayerVeryClose(enemyParent);
            while (inner.MoveNext())
            {
                yield return inner.Current;

                Plugin plugin = instance;
                if (plugin == null || !plugin.gameplayActive || enemyParent == null) continue;
                if (plugin.preferPlayerVisionDetection.Value
                    && HasEnemyOnScreen(enemyParent)
                    && !ShouldUseLegacyVisionFallback(enemyParent)) continue;

                bool isPlayerVeryClose = plugin.IsEnemyParentPlayerVeryClose(enemyParent);
                if (isPlayerVeryClose && !wasPlayerVeryClose)
                {
                    plugin.HandleBlindEnemyPlayerVeryClose(enemyParent);
                }
                wasPlayerVeryClose = isPlayerVeryClose;
            }
        }

        private static IEnumerator WatchEnemyOnScreen(IEnumerator inner, Component enemyOnScreen)
        {
            Component enemyParent = null;
            int instanceId = 0;
            bool encounterHandled = false;
            while (inner.MoveNext())
            {
                yield return inner.Current;

                Plugin plugin = instance;
                if (plugin == null || !plugin.gameplayActive || enemyOnScreen == null) continue;
                if (encounterHandled) continue;
                if (!plugin.preferPlayerVisionDetection.Value) continue;

                if (enemyParent == null) enemyParent = GetEnemyParentFromOnScreen(enemyOnScreen);
                if (enemyParent == null) continue;

                if (instanceId == 0) instanceId = ResolveEnemyInstanceId(enemyParent);
                if (instanceId != 0 && IsEnemySent(instanceId))
                {
                    encounterHandled = true;
                    continue;
                }

                if (!plugin.IsEnemyOnScreenVisible(enemyOnScreen)) continue;
                plugin.HandleEnemyOnScreenVisible(enemyParent, ref instanceId);
                if (instanceId != 0 && IsEnemySent(instanceId)) encounterHandled = true;
            }
        }

        private void HandleBlindEnemyPlayerVeryClose(Component enemyParent)
        {
            if (enemyParent == null || HasEnemyVision(enemyParent)) return;
            if (preferPlayerVisionDetection.Value
                && HasEnemyOnScreen(enemyParent)
                && !ShouldUseLegacyVisionFallback(enemyParent)) return;

            int instanceId = ResolveEnemyInstanceId(enemyParent);
            if (instanceId == 0 || IsEnemySent(instanceId)) return;

            RegisterEnemyParent(enemyParent);
            string monsterName = null;
            if (PublishEnemyParentEncounter(enemyParent, ref instanceId, ref monsterName)) return;

            pendingEncounterIds.Add(instanceId);
            enemyRosterDirty = true;
            nextScanAt = 0f;
        }

        private void HandleEnemyOnScreenVisible(Component enemyParent, ref int instanceId)
        {
            if (!preferPlayerVisionDetection.Value) return;
            if (enemyParent == null) return;
            if (instanceId == 0) instanceId = ResolveEnemyInstanceId(enemyParent);
            if (instanceId == 0 || IsEnemySent(instanceId)) return;

            RegisterEnemyParent(enemyParent);
            string monsterName = null;
            if (PublishEnemyParentEncounter(enemyParent, ref instanceId, ref monsterName)) return;

            pendingEncounterIds.Add(instanceId);
            enemyRosterDirty = true;
            nextScanAt = 0f;
        }

        private bool PublishEnemyParentEncounter(Component enemyParent, ref int instanceId, ref string monsterName)
        {
            if (!rosterPublished) return false;
            if (enemyParent == null) return false;

            GameObject root = GetEnemyRoot(enemyParent);
            if (root == null) return false;
            if (instanceId == 0) instanceId = root.GetInstanceID();
            if (instanceId == 0 || IsEnemySent(instanceId)) return true;

            if (monsterName == null)
            {
                monsterName = ResolveMonsterName(enemyParent);
                if (monsterName != null) resolvedMonsterNames[instanceId] = monsterName;
            }
            if (monsterName == null) return false;
            if (!TryMarkEnemySent(instanceId)) return true;

            var candidate = new EnemyCandidate
            {
                Component = enemyParent,
                Root = root,
                Center = GetObjectCenter(root)
            };
            MarkMonsterSeen(monsterName, candidate);
            if (debugLogging.Value)
            {
                Logger.LogInfo("Encountered nearby monster: " + monsterName + " from " + enemyParent.GetType().Name + " / " + root.name);
            }
            StartCoroutine(PostSeenMonster(monsterName, instanceId));
            SyncEnemyParentStatusChanged(enemyParent);
            return true;
        }

        private void HandleEnemyVisionTrigger(object vision, int playerId)
        {
            if (!gameplayActive || vision == null) return;
            int localViewId = GetLocalPlayerViewId();
            if (playerId != localViewId) return;

            if (!TryGetVisionEnemyCache(vision, out VisionEnemyCache visionEnemy)) return;
            Component enemyParent = visionEnemy.EnemyParent;
            if (preferPlayerVisionDetection.Value
                && HasEnemyOnScreen(enemyParent)
                && !ShouldUseLegacyVisionFallback(enemyParent)) return;
            int instanceId = visionEnemy.InstanceId;
            if (instanceId == 0 || IsEnemySent(instanceId)) return;
            if (pendingEncounterIds.Contains(instanceId)) return;

            RegisterEnemyParent(enemyParent);
            string monsterName = null;
            if (PublishEnemyParentEncounter(enemyParent, ref instanceId, ref monsterName))
            {
                pendingEncounterIds.Remove(instanceId);
                return;
            }

            pendingEncounterIds.Add(instanceId);
            enemyRosterDirty = true;
            nextScanAt = 0f;
        }

        private bool TryGetVisionEnemyCache(object vision, out VisionEnemyCache visionEnemy)
        {
            visionEnemy = default;
            int visionId = GetUnityObjectId(vision);
            if (visionId != 0
                && visionEnemyCacheByVisionId.TryGetValue(visionId, out visionEnemy)
                && visionEnemy.EnemyParent != null
                && visionEnemy.InstanceId != 0)
            {
                return true;
            }

            Component enemy = ReadMember(vision, "Enemy") as Component;
            Component enemyParent = ReadMember(enemy, "EnemyParent") as Component;
            if (enemyParent == null) return false;

            int instanceId = ResolveEnemyInstanceId(enemyParent);
            if (instanceId == 0) return false;

            visionEnemy = new VisionEnemyCache
            {
                EnemyParent = enemyParent,
                InstanceId = instanceId
            };

            if (visionId != 0) visionEnemyCacheByVisionId[visionId] = visionEnemy;
            return true;
        }

        private void TryPublishPendingVisionEncounters(List<ResolvedEnemyCandidate> resolvedEnemies)
        {
            if (pendingEncounterIds.Count == 0) return;

            for (int index = 0; index < resolvedEnemies.Count; index++)
            {
                ResolvedEnemyCandidate resolvedEnemy = resolvedEnemies[index];
                int instanceId = resolvedEnemy.Candidate.Root.GetInstanceID();
                if (!pendingEncounterIds.Contains(instanceId)) continue;

                Component enemyParent = GetEnemyParent(resolvedEnemy.Candidate);
                string monsterName = resolvedEnemy.MonsterName;
                int publishId = instanceId;
                if (PublishEnemyParentEncounter(enemyParent, ref publishId, ref monsterName))
                {
                    pendingEncounterIds.Remove(instanceId);
                }
            }
        }

        private static bool HasEnemyVision(Component enemyParent)
        {
            if (enemyParent == null) return false;
            Plugin plugin = instance;
            if (plugin != null)
            {
                int parentId = enemyParent.GetInstanceID();
                if (plugin.enemyHasVisionByParentId.TryGetValue(parentId, out bool cachedValue)) return cachedValue;
                bool value = ReadEnemyHasVision(enemyParent);
                plugin.enemyHasVisionByParentId[parentId] = value;
                return value;
            }

            return ReadEnemyHasVision(enemyParent);
        }

        private static bool ReadEnemyHasVision(Component enemyParent)
        {
            Component enemy = ReadMember(enemyParent, "Enemy") as Component;
            object hasVision = ReadMember(enemy, "HasVision");
            return hasVision is bool value && value;
        }

        private static bool HasEnemyOnScreen(Component enemyParent)
        {
            if (enemyParent == null) return false;
            Plugin plugin = instance;
            if (plugin != null)
            {
                int parentId = enemyParent.GetInstanceID();
                if (plugin.enemyHasOnScreenByParentId.TryGetValue(parentId, out bool cachedValue)) return cachedValue;
                bool value = ReadEnemyHasOnScreen(enemyParent);
                plugin.enemyHasOnScreenByParentId[parentId] = value;
                return value;
            }

            return ReadEnemyHasOnScreen(enemyParent);
        }

        private static bool ReadEnemyHasOnScreen(Component enemyParent)
        {
            Component enemy = ReadMember(enemyParent, "Enemy") as Component;
            object hasOnScreen = ReadMember(enemy, "HasOnScreen");
            if (hasOnScreen is bool value) return value;
            return enemy != null && enemy.GetComponent("EnemyOnScreen") != null;
        }

        private static Component GetEnemyParentFromOnScreen(Component enemyOnScreen)
        {
            if (enemyOnScreen == null) return null;
            Component enemy = ReadMember(enemyOnScreen, "Enemy") as Component;
            if (enemy == null) enemy = enemyOnScreen.GetComponent("Enemy");
            return ReadMember(enemy, "EnemyParent") as Component;
        }

        private static int ResolveEnemyInstanceId(Component enemyParent)
        {
            GameObject root = enemyParent == null ? null : GetEnemyRoot(enemyParent);
            return root == null ? 0 : root.GetInstanceID();
        }

        private int GetLocalPlayerViewId()
        {
            if (cachedLocalPlayerViewId != int.MinValue) return cachedLocalPlayerViewId;
            cachedLocalPlayerViewId = ResolveLocalPlayerViewId();
            return cachedLocalPlayerViewId;
        }

        private static int ResolveLocalPlayerViewId()
        {
            object localViewId = InvokeNoArgMethod(AccessTools.TypeByName("SemiFunc"), "PhotonViewIDPlayerAvatarLocal");
            if (localViewId != null)
            {
                try
                {
                    return Convert.ToInt32(localViewId, CultureInfo.InvariantCulture);
                }
                catch
                {
                }
            }

            object player = InvokeNoArgMethod(AccessTools.TypeByName("SemiFunc"), "PlayerAvatarLocal");
            if (player == null) player = ReadMember(AccessTools.TypeByName("PlayerAvatar"), "instance");
            object photonView = ReadMember(player, "photonView");
            return ReadIntMember(photonView, "ViewID");
        }

        private static object ReadMember(object source, string memberName)
        {
            if (source == null) return null;

            Type type = source as Type ?? source.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            string cacheKey = type.AssemblyQualifiedName + "\n" + memberName;
            MemberInfo member;

            lock (reflectionCacheLock)
            {
                if (missingMemberCache.Contains(cacheKey)) return null;
                memberCache.TryGetValue(cacheKey, out member);
            }

            if (member == null)
            {
                member = type.GetField(memberName, flags);
                if (member == null)
                {
                    PropertyInfo candidate = type.GetProperty(memberName, flags);
                    if (candidate != null && candidate.GetIndexParameters().Length == 0) member = candidate;
                }

                lock (reflectionCacheLock)
                {
                    if (member == null) missingMemberCache.Add(cacheKey);
                    else memberCache[cacheKey] = member;
                }
            }

            if (member is FieldInfo field)
            {
                if (!field.IsStatic && source is Type) return null;
                return field.GetValue(field.IsStatic ? null : source);
            }

            if (member is PropertyInfo property)
            {
                try
                {
                    MethodInfo getter = property.GetGetMethod(true);
                    if (getter == null || (!getter.IsStatic && source is Type)) return null;
                    return property.GetValue(getter.IsStatic ? null : source, null);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static object InvokeNoArgMethod(object source, string methodName)
        {
            if (source == null) return null;

            Type type = source as Type ?? source.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            string cacheKey = type.AssemblyQualifiedName + "\n" + methodName + "()";
            MethodInfo method;

            lock (reflectionCacheLock)
            {
                if (missingNoArgMethodCache.Contains(cacheKey)) return null;
                noArgMethodCache.TryGetValue(cacheKey, out method);
            }

            if (method == null)
            {
                method = type.GetMethod(methodName, flags, null, Type.EmptyTypes, null);
                lock (reflectionCacheLock)
                {
                    if (method == null) missingNoArgMethodCache.Add(cacheKey);
                    else noArgMethodCache[cacheKey] = method;
                }
            }

            if (method == null || (!method.IsStatic && source is Type)) return null;

            try
            {
                return method.Invoke(method.IsStatic ? null : source, null);
            }
            catch
            {
                return null;
            }
        }

        private static Task<string> QueueNetworkRequest(Func<string> request)
        {
            lock (networkQueueLock)
            {
                Task<string> queued = networkQueueTail.ContinueWith(
                    _ => request(),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
                networkQueueTail = queued;
                return queued;
            }
        }

        private IEnumerator PostSeenMonster(string monsterName, int instanceId)
        {
            string json = "{\"name\":\"" + EscapeJson(monsterName) + "\",\"id\":" + instanceId + "}";
            string endpointUrl = endpoint.Value;
            Task<string> request = QueueNetworkRequest(() => SendHttpPost(endpointUrl, json));
            while (!request.IsCompleted) yield return null;
            string result = request.IsFaulted ? "ERROR:" + request.Exception.GetBaseException().Message : request.Result;
            if (result.StartsWith("ERROR:", StringComparison.Ordinal))
            {
                Logger.LogWarning("Failed to send monster " + monsterName + " to " + endpointUrl + ": " + result.Substring(6));
            }
            else if (debugLogging.Value)
            {
                Logger.LogInfo("Sent seen monster " + monsterName + ": " + result);
            }

            yield break;
        }

        private IEnumerator PostMonsterRoster(string json)
        {
            string rosterEndpoint = BuildSiblingEndpoint(levelEndpoint.Value, "/api/roster");
            Task<string> request = QueueNetworkRequest(() => SendHttpPost(rosterEndpoint, json));
            while (!request.IsCompleted) yield return null;
            string result = request.IsFaulted ? "ERROR:" + request.Exception.GetBaseException().Message : request.Result;
            if (!IsHttpSuccess(result))
            {
                Logger.LogWarning("Failed to sync monster roster: " + TrimHttpResult(result));
            }
            else if (debugLogging.Value)
            {
                Logger.LogInfo("Synced monster roster.");
            }

            yield break;
        }

        private IEnumerator PostMonsterStatuses(string json)
        {
            string statusEndpoint = BuildSiblingEndpoint(levelEndpoint.Value, "/api/monster-status");
            Task<string> request = QueueNetworkRequest(() => SendHttpPost(statusEndpoint, json));
            while (!request.IsCompleted) yield return null;
            string result = request.IsFaulted ? "ERROR:" + request.Exception.GetBaseException().Message : request.Result;
            if (!IsHttpSuccess(result))
            {
                Logger.LogWarning("Failed to sync monster respawn status: " + TrimHttpResult(result));
            }

            yield break;
        }

        private IEnumerator PostLevel(int level, string levelName)
        {
            string json = "{\"level\":" + level + ",\"levelName\":\"" + EscapeJson(levelName ?? "") + "\"}";
            string endpointUrl = levelEndpoint.Value;
            Task<string> request = QueueNetworkRequest(() => SendHttpPost(endpointUrl, json));
            while (!request.IsCompleted) yield return null;
            string result = request.IsFaulted ? "ERROR:" + request.Exception.GetBaseException().Message : request.Result;
            if (IsHttpSuccess(result))
            {
                if (debugLogging.Value)
                {
                    Logger.LogInfo("Synced level " + level + " to overlay: " + result);
                }
            }
            else
            {
                Logger.LogWarning("Failed to sync level " + level + " to " + endpointUrl + ": " + TrimHttpResult(result) + ". Trying /api/state fallback.");
                Task<string> fallbackRequest = QueueNetworkRequest(() => SendStateLevelFallback(level, endpointUrl));
                while (!fallbackRequest.IsCompleted) yield return null;
                string fallbackResult = fallbackRequest.IsFaulted ? "ERROR:" + fallbackRequest.Exception.GetBaseException().Message : fallbackRequest.Result;
                if (IsHttpSuccess(fallbackResult))
                {
                    Logger.LogInfo("Synced level " + level + " through /api/state fallback: " + fallbackResult);
                }
                else
                {
                    Logger.LogWarning("Failed to sync level " + level + " through /api/state fallback: " + TrimHttpResult(fallbackResult));
                }
            }

            yield break;
        }

        private IEnumerator PostVisibility(bool visible)
        {
            string visibilityEndpoint = BuildSiblingEndpoint(levelEndpoint.Value, "/api/visibility");
            string json = "{\"visible\":" + (visible ? "true" : "false") + "}";
            Task<string> request = QueueNetworkRequest(() => SendHttpPost(visibilityEndpoint, json));
            while (!request.IsCompleted) yield return null;
            string result = request.IsFaulted ? "ERROR:" + request.Exception.GetBaseException().Message : request.Result;
            if (!IsHttpSuccess(result))
            {
                Logger.LogWarning("Failed to sync overlay visibility: " + TrimHttpResult(result));
            }
            else if (debugLogging.Value)
            {
                Logger.LogInfo("Synced overlay visibility " + (visible ? "on" : "off") + ": " + result);
            }

            yield break;
        }

        private IEnumerator PostTabHidden(bool hidden)
        {
            string tabEndpoint = BuildSiblingEndpoint(levelEndpoint.Value, "/api/tab-hidden");
            string json = "{\"hidden\":" + (hidden ? "true" : "false") + "}";
            Task<string> request = QueueNetworkRequest(() => SendHttpPost(tabEndpoint, json));
            while (!request.IsCompleted) yield return null;
            string result = request.IsFaulted ? "ERROR:" + request.Exception.GetBaseException().Message : request.Result;
            if (!IsHttpSuccess(result) && debugLogging.Value)
            {
                Logger.LogWarning("Failed to sync Tab visibility: " + TrimHttpResult(result));
            }

            yield break;
        }

        private IEnumerator PostPlayerUpgrades(string json, int changedCount)
        {
            string upgradesEndpoint = BuildSiblingEndpoint(levelEndpoint.Value, "/api/upgrades");
            Task<string> request = QueueNetworkRequest(() => SendHttpPost(upgradesEndpoint, json));
            while (!request.IsCompleted) yield return null;
            string result = request.IsFaulted ? "ERROR:" + request.Exception.GetBaseException().Message : request.Result;
            if (!IsHttpSuccess(result))
            {
                Logger.LogWarning("Failed to sync player upgrades: " + TrimHttpResult(result));
            }
            else if (debugLogging.Value)
            {
                Logger.LogInfo("Synced " + changedCount + " player upgrade value(s): " + result);
            }

            yield break;
        }

        private IEnumerator PostMapValue(int value, int initial, int lost, int? goal)
        {
            string mapValueEndpoint = BuildSiblingEndpoint(levelEndpoint.Value, "/api/map-value");
            string json = "{\"value\":" + value.ToString(CultureInfo.InvariantCulture)
                + ",\"initial\":" + initial.ToString(CultureInfo.InvariantCulture)
                + ",\"lost\":" + lost.ToString(CultureInfo.InvariantCulture);
            if (goal.HasValue) json += ",\"goal\":" + goal.Value.ToString(CultureInfo.InvariantCulture);
            json += "}";

            Task<string> request = QueueNetworkRequest(() => SendHttpPost(mapValueEndpoint, json));
            while (!request.IsCompleted) yield return null;
            string result = request.IsFaulted ? "ERROR:" + request.Exception.GetBaseException().Message : request.Result;
            if (!IsHttpSuccess(result))
            {
                Logger.LogWarning("Failed to sync map value: " + TrimHttpResult(result));
            }
            else if (debugLogging.Value)
            {
                Logger.LogInfo("Synced map value " + value + ": " + result);
            }

            yield break;
        }

        private static string SendStateLevelFallback(int level, string levelEndpointUrl)
        {
            string stateEndpoint = BuildSiblingEndpoint(levelEndpointUrl, "/api/state");
            string stateJson = BuildFallbackStateJson(level, SendHttpGet(stateEndpoint));
            return SendHttpPost(stateEndpoint, "{\"state\":" + stateJson + "}");
        }

        private static string BuildSiblingEndpoint(string endpointUrl, string path)
        {
            try
            {
                var uri = new Uri(endpointUrl);
                return uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port) + path;
            }
            catch
            {
                return endpointUrl.Replace("/api/level", path).Replace("/api/monster-seen", path);
            }
        }

        private static string BuildFallbackStateJson(int level, string getStateResult)
        {
            string body = ExtractHttpBody(getStateResult);
            string stateObject = ExtractJsonObjectProperty(body, "state");
            if (string.IsNullOrWhiteSpace(stateObject))
            {
                stateObject = "{}";
            }

            stateObject = SetJsonNumberProperty(stateObject, "level", level);
            stateObject = SetJsonBooleanProperty(stateObject, "gameplayVisible", true);
            stateObject = SetJsonNumberProperty(stateObject, "seconds", 0);
            stateObject = SetJsonBooleanProperty(stateObject, "running", true);
            long startedAt = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            stateObject = SetJsonRawProperty(stateObject, "startedAt", startedAt.ToString(CultureInfo.InvariantCulture));
            stateObject = SetJsonNumberProperty(stateObject, "strength", 0);
            stateObject = SetJsonNumberProperty(stateObject, "tumbleLaunch", 0);
            stateObject = SetJsonNumberProperty(stateObject, "range", 0);
            stateObject = SetJsonNumberProperty(stateObject, "sprintSpeed", 0);
            stateObject = SetJsonNumberProperty(stateObject, "tumbleWings", 0);
            stateObject = SetJsonNumberProperty(stateObject, "crouchRest", 0);
            stateObject = SetJsonNumberProperty(stateObject, "extraJump", 0);
            stateObject = SetJsonNumberProperty(stateObject, "tumbleClimb", 0);
            stateObject = SetJsonRawProperty(stateObject, "monsters", "[]");
            stateObject = SetJsonNumberProperty(stateObject, "mapValue", 0);
            stateObject = SetJsonNumberProperty(stateObject, "mapValueInitial", 0);
            stateObject = SetJsonRawProperty(stateObject, "mapValueGoal", "null");
            stateObject = SetJsonNumberProperty(stateObject, "lostValue", 0);
            return stateObject;
        }

        private static string SetJsonNumberProperty(string json, string name, int value)
        {
            return SetJsonRawProperty(json, name, value.ToString());
        }

        private static string SetJsonBooleanProperty(string json, string name, bool value)
        {
            return SetJsonRawProperty(json, name, value ? "true" : "false");
        }

        private static string SetJsonRawProperty(string json, string name, string value)
        {
            string pattern = "(\"" + Regex.Escape(name) + "\"\\s*:\\s*)(null|true|false|-?\\d+(?:\\.\\d+)?|\"(?:\\\\.|[^\"])*\"|\\[[\\s\\S]*?\\]|\\{[\\s\\S]*?\\})";
            var regex = new Regex(pattern);
            if (regex.IsMatch(json))
            {
                return regex.Replace(json, "$1" + value, 1);
            }

            string trimmed = string.IsNullOrWhiteSpace(json) ? "{}" : json.Trim();
            if (trimmed == "{}") return "{\"" + name + "\":" + value + "}";
            return trimmed.Substring(0, trimmed.Length - 1) + ",\"" + name + "\":" + value + "}";
        }

        private static string ExtractJsonObjectProperty(string json, string name)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            string marker = "\"" + name + "\"";
            int markerIndex = json.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0) return null;
            int colonIndex = json.IndexOf(':', markerIndex + marker.Length);
            if (colonIndex < 0) return null;
            int start = json.IndexOf('{', colonIndex + 1);
            if (start < 0) return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int index = start; index < json.Length; index++)
            {
                char ch = json[index];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (ch == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }
                if (ch == '"')
                {
                    inString = !inString;
                    continue;
                }
                if (inString) continue;
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0) return json.Substring(start, index - start + 1);
                }
            }

            return null;
        }

        private static bool IsHttpSuccess(string result)
        {
            return result != null && (result.StartsWith("HTTP/1.1 2", StringComparison.Ordinal) || result.StartsWith("HTTP/1.0 2", StringComparison.Ordinal));
        }

        private static string TrimHttpResult(string result)
        {
            if (string.IsNullOrEmpty(result)) return "empty response";
            int lineEnd = result.IndexOf('\n');
            string firstLine = lineEnd >= 0 ? result.Substring(0, lineEnd) : result;
            if (firstLine.StartsWith("ERROR:", StringComparison.Ordinal)) return firstLine.Substring(6);
            return firstLine.Trim();
        }

        private static string SendHttpPost(string endpointUrl, string json)
        {
            try
            {
                var uri = new Uri(endpointUrl);
                if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
                {
                    return "ERROR:Only http:// endpoints are supported by the raw bridge client.";
                }

                int port = uri.IsDefaultPort ? 80 : uri.Port;
                byte[] body = Encoding.UTF8.GetBytes(json);
                string path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;

                using (var client = new TcpClient())
                {
                    IAsyncResult connect = client.BeginConnect(uri.Host, port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                    {
                        return "ERROR:Connection timed out.";
                    }
                    client.EndConnect(connect);
                    client.ReceiveTimeout = 2000;
                    client.SendTimeout = 2000;

                    using (NetworkStream stream = client.GetStream())
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                    {
                        writer.NewLine = "\r\n";
                        writer.WriteLine("POST " + path + " HTTP/1.1");
                        writer.WriteLine("Host: " + uri.Host + ":" + port);
                        writer.WriteLine("Content-Type: application/json");
                        writer.WriteLine("Content-Length: " + body.Length);
                        writer.WriteLine("Connection: close");
                        writer.WriteLine();
                        writer.Flush();
                        stream.Write(body, 0, body.Length);
                        stream.Flush();

                        return ReadHttpResponse(reader);
                    }
                }
            }
            catch (Exception error)
            {
                return "ERROR:" + error.GetType().Name + ": " + error.Message;
            }
        }

        private static string SendHttpGet(string endpointUrl)
        {
            try
            {
                var uri = new Uri(endpointUrl);
                if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
                {
                    return "ERROR:Only http:// endpoints are supported by the raw bridge client.";
                }

                int port = uri.IsDefaultPort ? 80 : uri.Port;
                string path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;

                using (var client = new TcpClient())
                {
                    IAsyncResult connect = client.BeginConnect(uri.Host, port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                    {
                        return "ERROR:Connection timed out.";
                    }
                    client.EndConnect(connect);
                    client.ReceiveTimeout = 2000;
                    client.SendTimeout = 2000;

                    using (NetworkStream stream = client.GetStream())
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                    {
                        writer.NewLine = "\r\n";
                        writer.WriteLine("GET " + path + " HTTP/1.1");
                        writer.WriteLine("Host: " + uri.Host + ":" + port);
                        writer.WriteLine("Connection: close");
                        writer.WriteLine();
                        writer.Flush();

                        return ReadHttpResponse(reader);
                    }
                }
            }
            catch (Exception error)
            {
                return "ERROR:" + error.GetType().Name + ": " + error.Message;
            }
        }

        private static string ReadHttpResponse(StreamReader reader)
        {
            string status = reader.ReadLine();
            if (string.IsNullOrEmpty(status)) return "ERROR:Empty HTTP response.";

            var builder = new StringBuilder();
            builder.AppendLine(status);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                builder.AppendLine(line);
            }

            return builder.ToString();
        }

        private static string ExtractHttpBody(string response)
        {
            if (string.IsNullOrEmpty(response)) return "";
            int split = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (split >= 0) return response.Substring(split + 4);
            split = response.IndexOf("\n\n", StringComparison.Ordinal);
            return split >= 0 ? response.Substring(split + 2) : "";
        }

        private static string FindKnownMonster(string raw)
        {
            string key = Normalize(raw);
            foreach (KeyValuePair<string, string> pair in KnownMonsters)
            {
                if (key.Contains(pair.Key)) return pair.Value;
            }

            return null;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            var builder = new StringBuilder(value.Length);
            foreach (char ch in value.ToLowerInvariant())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private struct EnemyCandidate
        {
            public Component Component;
            public GameObject Root;
            public Vector3 Center;
        }

        private struct ResolvedEnemyCandidate
        {
            public EnemyCandidate Candidate;
            public string MonsterName;
        }

        private struct VisionEnemyCache
        {
            public Component EnemyParent;
            public int InstanceId;
        }

    }
}
