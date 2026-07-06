using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RepoMonsterBridge
{
    [BepInPlugin("local.overlay.repo_monster_bridge", "REPO Monster Bridge", "0.2.52")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private static Plugin instance;
        private static readonly List<string> seenMonsters = new List<string>();
        private static readonly List<Component> knownEnemyParents = new List<Component>();
        private static readonly HashSet<int> sentEnemyInstanceIds = new HashSet<int>();
        private static readonly object seenLock = new object();
        private static readonly object enemyLock = new object();
        private static readonly object reflectionCacheLock = new object();
        private static readonly object networkQueueLock = new object();
        private static readonly Dictionary<string, MemberInfo> memberCache = new Dictionary<string, MemberInfo>();
        private static readonly HashSet<string> missingMemberCache = new HashSet<string>();
        private static Task networkQueueTail = Task.CompletedTask;

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
            new KeyValuePair<string, string>("mapPlayerCount", "playerUpgradeMapPlayerCount"),
            new KeyValuePair<string, string>("tumbleWings", "playerUpgradeTumbleWings"),
            new KeyValuePair<string, string>("crouchRest", "playerUpgradeCrouchRest"),
            new KeyValuePair<string, string>("extraJump", "playerUpgradeExtraJump"),
            new KeyValuePair<string, string>("tumbleClimb", "playerUpgradeTumbleClimb")
        };

        private static readonly Dictionary<string, string> UpgradeStateKeyByItemType = new Dictionary<string, string>
        {
            { "ItemUpgradePlayerGrabStrength", "strength" },
            { "ItemUpgradePlayerTumbleLaunch", "tumbleLaunch" },
            { "ItemUpgradePlayerGrabRange", "range" },
            { "ItemUpgradePlayerSprintSpeed", "sprintSpeed" },
            { "ItemUpgradeMapPlayerCount", "mapPlayerCount" },
            { "ItemUpgradePlayerTumbleWings", "tumbleWings" },
            { "ItemUpgradePlayerCrouchRest", "crouchRest" },
            { "ItemUpgradePlayerExtraJump", "extraJump" },
            { "ItemUpgradePlayerTumbleClimb", "tumbleClimb" }
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
        private float nextScanAt;
        private float nextStatusSyncAt;
        private float nextUpgradeSyncAt;
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
        private bool rosterPublished;
        private int visibilityProbeIndex;
        private bool gameplayActive;
        private bool? lastTabHeld;

        private ConfigEntry<string> endpoint;
        private ConfigEntry<string> levelEndpoint;
        private ConfigEntry<float> scanInterval;
        private ConfigEntry<float> maxDistance;
        private ConfigEntry<float> peeperMaxDistance;
        private ConfigEntry<float> viewportPadding;
        private ConfigEntry<bool> preferGameOnScreen;
        private ConfigEntry<bool> requireLineOfSight;
        private ConfigEntry<bool> debugLogging;
        private ConfigEntry<bool> testSendOnStart;
        private ConfigEntry<bool> sendToWebOverlay;
        private Harmony harmony;

        private void Awake()
        {
            instance = this;
            endpoint = Config.Bind("Overlay", "Endpoint", "http://127.0.0.1:8787/api/monster-seen", "Monster endpoint on this PC.");
            levelEndpoint = Config.Bind("Overlay", "LevelEndpoint", "http://127.0.0.1:8787/api/level", "Level sync endpoint on this PC.");
            scanInterval = Config.Bind("Detection", "ScanIntervalSeconds", 1f, "How often visible enemies are scanned.");
            maxDistance = Config.Bind("Detection", "MaxDistance", 45f, "Maximum distance from camera to count an enemy as encountered.");
            peeperMaxDistance = Config.Bind("Detection", "PeeperMaxDistance", 120f, "Maximum distance for Peeper only when the game marks it as very close to the player.");
            viewportPadding = Config.Bind("Detection", "ViewportPadding", 0.03f, "Allowed viewport padding outside the screen edges.");
            preferGameOnScreen = Config.Bind("Detection", "PreferGameOnScreen", true, "Use the game's local on-screen state when line of sight is enabled.");
            requireLineOfSight = Config.Bind("Detection", "RequireLineOfSight", false, "Reveal only enemies marked on-screen for the local player by the game.");
            debugLogging = Config.Bind("Debug", "Logging", false, "Write periodic bridge debug logs.");
            testSendOnStart = Config.Bind("Debug", "TestSendOnStart", false, "Send one Spewer event when the plugin starts. Disable after network testing.");
            sendToWebOverlay = Config.Bind("Overlay", "SendToWebOverlay", true, "Also send encountered monsters to the HTML/OBS overlay endpoint.");
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
            if (!sendToWebOverlay.Value)
            {
                sendToWebOverlay.Value = true;
                configChanged = true;
            }
            if (configChanged) Config.Save();

            Logger.LogInfo("REPO Monster Bridge is running. MonsterEndpoint=" + endpoint.Value + ", LevelEndpoint=" + levelEndpoint.Value + ", Logging=" + debugLogging.Value + ", TestSendOnStart=" + testSendOnStart.Value + ", SendToWebOverlay=" + sendToWebOverlay.Value);
            if (testSendOnStart.Value)
            {
                StartCoroutine(PostSeenMonster("Spewer", 0));
            }

            PatchGameUpdates();
            if (sendToWebOverlay.Value)
            {
                StartCoroutine(PostVisibility(false));
                StartCoroutine(PostTabHidden(false));
            }
        }

        private void PatchGameUpdates()
        {
            try
            {
                harmony = new Harmony("local.overlay.repo_monster_bridge");
                MethodInfo enemyParentSpawnRpc = AccessTools.Method("EnemyParent:SpawnRPC");
                MethodInfo levelGeneratorGenerateDone = AccessTools.Method("LevelGenerator:GenerateDone");
                MethodInfo runManagerChangeLevel = AccessTools.Method("RunManager:ChangeLevel");
                HarmonyMethod levelGeneratedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(LevelGeneratedPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod enemySpawnedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(EnemyParentSpawnedPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod levelChangingPrefix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(LevelChangingPrefix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod playerUpgradeConsumedPostfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(PlayerUpgradeConsumedPostfix), BindingFlags.NonPublic | BindingFlags.Static));

                if (enemyParentSpawnRpc != null)
                {
                    harmony.Patch(enemyParentSpawnRpc, postfix: enemySpawnedPostfix);
                    Logger.LogInfo("Patched EnemyParent.SpawnRPC for enemy registration.");
                }

                if (levelGeneratorGenerateDone != null)
                {
                    harmony.Patch(levelGeneratorGenerateDone, postfix: levelGeneratedPostfix);
                    Logger.LogInfo("Patched LevelGenerator.GenerateDone for monster reset.");
                }

                if (runManagerChangeLevel != null)
                {
                    harmony.Patch(runManagerChangeLevel, prefix: levelChangingPrefix);
                    Logger.LogInfo("Patched RunManager.ChangeLevel for overlay visibility.");
                }

                int patchedUpgradeTypes = 0;
                foreach (string itemTypeName in UpgradeStateKeyByItemType.Keys)
                {
                    MethodInfo upgradeMethod = AccessTools.Method(itemTypeName + ":Upgrade");
                    if (upgradeMethod == null) continue;
                    harmony.Patch(upgradeMethod, postfix: playerUpgradeConsumedPostfix);
                    patchedUpgradeTypes++;
                }
                Logger.LogInfo("Patched " + patchedUpgradeTypes + " item upgrade methods for targeted sync.");

            }
            catch (Exception error)
            {
                Logger.LogWarning("Failed to patch game updates: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private void Update()
        {
            SyncTabStateIfChanged();
            TickScan();
        }

        private static void LevelGeneratedPostfix()
        {
            instance?.HandleLevelGenerated();
        }

        private static void LevelChangingPrefix()
        {
            instance?.HandleLevelChanging();
        }

        private static void PlayerUpgradeConsumedPostfix(MethodBase __originalMethod)
        {
            string itemTypeName = __originalMethod?.DeclaringType?.Name;
            if (itemTypeName != null && UpgradeStateKeyByItemType.TryGetValue(itemTypeName, out string stateKey))
            {
                instance?.MarkPlayerUpgradeDirty(stateKey);
            }
        }

        private static void EnemyParentSpawnedPostfix(object __instance)
        {
            if (__instance is Component component)
            {
                RegisterEnemyParent(component);
            }
        }

        private void TickScan()
        {
            if (!gameplayActive) return;
            float now = Time.realtimeSinceStartup;
            if (now < scanPausedUntil) return;

            if (pendingUpgradeKeys.Count > 0 && now >= nextUpgradeSyncAt)
            {
                var keys = new HashSet<string>(pendingUpgradeKeys);
                pendingUpgradeKeys.Clear();
                SyncPlayerUpgradesIfChanged(keys);
            }

            if (rosterPublished && now >= nextStatusSyncAt)
            {
                nextStatusSyncAt = now + 0.5f;
                SyncMonsterStatuses(new List<ResolvedEnemyCandidate>());
            }

            if (now < nextScanAt) return;
            nextScanAt = now + Math.Max(0.05f, scanInterval.Value);

            try
            {
                ScanVisibleEnemies();
            }
            catch (Exception error)
            {
                Logger.LogWarning("Scan failed: " + error.GetType().Name + ": " + error.Message + "\n" + error.StackTrace);
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            if (instance == this) instance = null;
        }

        private void HandleLevelGenerated()
        {
            ResetSeenMonsters("new level generated");
            gameplayActive = IsRegularGameplayLevel();
            if (!gameplayActive)
            {
                if (sendToWebOverlay.Value) StartCoroutine(PostVisibility(false));
                return;
            }

            int level = ResolveCurrentLevel();
            if (level > 0) SyncLevelToOverlay(level);
        }

        private void HandleLevelChanging()
        {
            gameplayActive = false;
            if (sendToWebOverlay.Value) StartCoroutine(PostVisibility(false));
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
            lastRosterFingerprint = "";
            lastStatusFingerprint = "";
            pendingRosterFingerprint = "";
            rosterStableScans = 0;
            rosterPublished = false;
            visibilityProbeIndex = 0;
            scanPausedUntil = Time.realtimeSinceStartup + 2f;
            nextScanAt = scanPausedUntil;
            nextStatusSyncAt = scanPausedUntil;
            nextUpgradeSyncAt = scanPausedUntil;
            nextDebugSummaryAt = 0f;
            nextBroadEnemyDiscoveryAt = scanPausedUntil;
            Logger.LogInfo("Cleared seen monsters: " + reason + ".");
        }

        private void ScanVisibleEnemies()
        {
            Camera camera = FindBestCamera();
            bool revealAll = !requireLineOfSight.Value;

            List<EnemyCandidate> found = FindEnemyCandidates();
            var resolvedEnemies = new List<ResolvedEnemyCandidate>();
            int candidates = found.Count;
            int visible = 0;

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
            var pendingEnemies = new List<ResolvedEnemyCandidate>();
            foreach (ResolvedEnemyCandidate resolvedEnemy in resolvedEnemies)
            {
                if (!IsEnemySent(resolvedEnemy.Candidate.Root.GetInstanceID())) pendingEnemies.Add(resolvedEnemy);
            }

            int probeIndex = pendingEnemies.Count == 0 ? -1 : visibilityProbeIndex % pendingEnemies.Count;
            for (int index = 0; index < pendingEnemies.Count; index++)
            {
                ResolvedEnemyCandidate resolvedEnemy = pendingEnemies[index];
                EnemyCandidate candidate = resolvedEnemy.Candidate;
                string monsterName = resolvedEnemy.MonsterName;

                if (!revealAll && !IsVisibleEncounter(camera, candidate, monsterName, index == probeIndex)) continue;
                visible++;

                if (!TryMarkEnemySent(candidate.Root.GetInstanceID())) continue;
                MarkMonsterSeen(monsterName, candidate);
                if (debugLogging.Value)
                {
                    Logger.LogInfo((revealAll ? "Revealed level monster: " : "Encountered visible monster: ") + monsterName + " from " + candidate.Component.GetType().Name + " / " + candidate.Root.name);
                }
                if (sendToWebOverlay.Value)
                {
                    StartCoroutine(PostSeenMonster(monsterName, candidate.Root.GetInstanceID()));
                }
            }
            if (pendingEnemies.Count > 0) visibilityProbeIndex = (probeIndex + 1) % pendingEnemies.Count;
            else nextScanAt = Time.realtimeSinceStartup + 5f;

            if (debugLogging.Value && Time.realtimeSinceStartup >= nextDebugSummaryAt)
            {
                nextDebugSummaryAt = Time.realtimeSinceStartup + 30f;
                Logger.LogInfo("Scan summary: camera=" + (camera == null ? "none" : camera.name) + ", candidates=" + candidates + ", resolved=" + resolvedEnemies.Count + ", visible=" + visible);
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
            if (sendToWebOverlay.Value)
            {
                var json = new StringBuilder("{\"monsters\":[");
                for (int index = 0; index < enemies.Count; index++)
                {
                    ResolvedEnemyCandidate enemy = enemies[index];
                    if (index > 0) json.Append(',');
                    json.Append("{\"id\":").Append(enemy.Candidate.Root.GetInstanceID()).Append(",\"name\":\"").Append(EscapeJson(enemy.MonsterName)).Append("\"}");
                }
                json.Append("]}");
                StartCoroutine(PostMonsterRoster(json.ToString()));
            }
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
                    continue;
                }

                statusCandidates[sourceId] = new EnemyCandidate
                {
                    Component = enemyParent,
                    Root = root,
                    Center = Vector3.zero
                };
            }

            var sourceIds = new List<int>(statusCandidates.Keys);
            sourceIds.Sort();
            for (int index = 0; index < sourceIds.Count; index++)
            {
                int instanceId = sourceIds[index];
                EnemyCandidate candidate = statusCandidates[instanceId];
                if (!TryGetEnemyRespawnStatus(candidate, out bool alive, out float remaining)) continue;
                bool hasHealth = TryGetEnemyHealth(candidate, out float health, out float maxHealth);

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

            bool timerModAvailable;
            if (TryGetTimerModRespawnStatus(enemyParent, out alive, out remaining, out timerModAvailable))
            {
                return true;
            }
            if (timerModAvailable)
            {
                alive = true;
                remaining = 0f;
                return true;
            }

            object timerValue = ReadMember(enemyParent, "DespawnedTimer");
            if (!TryConvertFloat(timerValue, out remaining)) return false;

            remaining = Math.Max(0f, remaining);
            alive = remaining <= 0.000001f;
            return true;
        }

        private static bool TryGetEnemyHealth(EnemyCandidate candidate, out float health, out float maxHealth)
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

        private static IEnumerable<object> GetEnemyHealthSources(EnemyCandidate candidate)
        {
            if (candidate.Root != null)
            {
                Component enemyHealth = candidate.Root.GetComponent("EnemyHealth");
                if (enemyHealth != null) yield return enemyHealth;

                Component[] components = candidate.Root.GetComponentsInChildren<Component>(true);
                foreach (Component component in components)
                {
                    if (component == null) continue;
                    string typeName = component.GetType().Name;
                    if (typeName.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        yield return component;
                    }
                }

                Component enemy = candidate.Root.GetComponent("Enemy");
                if (enemy != null) yield return enemy;
            }

            if (candidate.Component != null) yield return candidate.Component;

            Component enemyParent = GetEnemyParent(candidate);
            if (enemyParent != null) yield return enemyParent;

            if (candidate.Root == null) yield break;

            Component[] damageComponents = candidate.Root.GetComponentsInChildren<Component>(true);
            foreach (Component component in damageComponents)
            {
                if (component == null) continue;
                string typeName = component.GetType().Name;
                if (typeName.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    yield return component;
                }
            }
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

        private static bool TryGetTimerModRespawnStatus(Component enemyParent, out bool alive, out float remaining, out bool timerModAvailable)
        {
            alive = true;
            remaining = 0f;
            timerModAvailable = false;

            if (!TryGetEnemyViewId(enemyParent, out int viewId)) return false;

            Type timerUiType = Type.GetType("TimerPlugin.EnemyListUI, TimerPlugin");
            if (timerUiType == null) return false;

            foreach (IDictionary timers in GetTimerModEnemyDictionaries(timerUiType))
            {
                timerModAvailable = true;
                if (!TryReadDictionaryTimer(timers, viewId, out remaining)) continue;

                remaining = Math.Max(0f, remaining);
                alive = remaining <= 0.000001f;
                return true;
            }

            return false;
        }

        private static IEnumerable<IDictionary> GetTimerModEnemyDictionaries(Type timerUiType)
        {
            object staticTimers = ReadMember(timerUiType, "Enemies") ?? ReadMember(timerUiType, "_enemies");
            if (staticTimers is IDictionary staticDictionary) yield return staticDictionary;

            UnityEngine.Object[] timerUis;
            try
            {
                timerUis = Resources.FindObjectsOfTypeAll(timerUiType);
            }
            catch
            {
                yield break;
            }

            foreach (UnityEngine.Object timerUi in timerUis)
            {
                object timers = ReadMember(timerUi, "Enemies") ?? ReadMember(timerUi, "_enemies");
                if (timers is IDictionary dictionary) yield return dictionary;
            }
        }

        private static bool TryReadDictionaryTimer(IDictionary dictionary, int viewId, out float remaining)
        {
            remaining = 0f;
            object value = null;

            if (dictionary.Contains(viewId))
            {
                value = dictionary[viewId];
            }
            else if (dictionary.Contains(viewId.ToString(CultureInfo.InvariantCulture)))
            {
                value = dictionary[viewId.ToString(CultureInfo.InvariantCulture)];
            }
            else
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (KeysMatchViewId(entry.Key, viewId))
                    {
                        value = entry.Value;
                        break;
                    }
                }
            }

            if (value == null) return false;
            return TryConvertTimerValue(value, out remaining);
        }

        private static bool KeysMatchViewId(object key, int viewId)
        {
            if (key == null) return false;
            try
            {
                return Convert.ToInt32(key, CultureInfo.InvariantCulture) == viewId;
            }
            catch
            {
                return string.Equals(key.ToString(), viewId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            }
        }

        private static bool TryConvertTimerValue(object value, out float remaining)
        {
            if (TryConvertFloat(value, out remaining)) return true;

            object timerValue = ReadMember(value, "DespawnedTimer")
                ?? ReadMember(value, "despawnedTimer")
                ?? ReadMember(value, "Remaining")
                ?? ReadMember(value, "remaining")
                ?? ReadMember(value, "RespawnTime")
                ?? ReadMember(value, "respawnTime");
            return TryConvertFloat(timerValue, out remaining);
        }

        private static bool TryGetEnemyViewId(Component enemyParent, out int viewId)
        {
            viewId = 0;
            object photonView = InvokeNoArgMethod(enemyParent, "GetPhotonView")
                ?? ReadMember(enemyParent, "photonViewRef")
                ?? enemyParent.GetComponent("PhotonView");
            object viewIdValue = ReadMember(photonView, "ViewID") ?? ReadMember(photonView, "viewID");
            if (viewIdValue == null) return false;

            try
            {
                viewId = Convert.ToInt32(viewIdValue, CultureInfo.InvariantCulture);
                return viewId != 0;
            }
            catch
            {
                return false;
            }
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
            if (currentLevel == null || !(levelsValue is IList levels)) return false;
            return levels.Contains(currentLevel);
        }

        private void SyncLevelToOverlay(int level)
        {
            lastSyncedLevel = level;
            StartCoroutine(PostLevel(level));
            lastSyncedUpgrades.Clear();
            pendingUpgradeKeys.Clear();
            SyncPlayerUpgradesIfChanged();
        }

        private void MarkPlayerUpgradeDirty(string stateKey)
        {
            pendingUpgradeKeys.Add(stateKey);
            nextUpgradeSyncAt = Time.realtimeSinceStartup + 0.1f;
        }

        private void SyncTabStateIfChanged()
        {
            bool tabHeld = Input.GetKey(KeyCode.Tab);
            if (lastTabHeld.HasValue && lastTabHeld.Value == tabHeld) return;
            lastTabHeld = tabHeld;
            if (sendToWebOverlay.Value) StartCoroutine(PostTabHidden(tabHeld));
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

            if (changedCount == 0 || !sendToWebOverlay.Value) return;
            json.Append("}}");
            StartCoroutine(PostPlayerUpgrades(json.ToString(), changedCount));
        }

        private static int? ResolveLocalPlayerUpgrade(string memberName)
        {
            Type statsManagerType = AccessTools.TypeByName("StatsManager");
            object statsManager = ReadMember(statsManagerType, "instance");
            object upgradesValue = ReadMember(statsManager, memberName);
            if (!(upgradesValue is IDictionary upgrades)) return null;

            Type playerControllerType = AccessTools.TypeByName("PlayerController");
            object playerController = ReadMember(playerControllerType, "instance");
            string steamId = ReadMember(playerController, "playerSteamID") as string;
            if (string.IsNullOrEmpty(steamId))
            {
                object playerAvatar = ReadMember(playerController, "playerAvatarScript");
                steamId = ReadMember(playerAvatar, "steamID") as string;
            }

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

            float now = Time.realtimeSinceStartup;
            bool runBroadDiscovery = !rosterPublished || now >= nextBroadEnemyDiscoveryAt;
            if (runBroadDiscovery)
            {
                nextBroadEnemyDiscoveryAt = now + 2f;
            }

            if (runBroadDiscovery)
            {
                foreach (Component component in FindSpawnedEnemiesFromDirector())
                {
                    AddEnemyCandidate(result, seenRoots, component);
                }
            }

            if (runBroadDiscovery)
            {
                foreach (Component component in FindComponentsByTypeName("EnemyParent"))
                {
                    AddEnemyCandidate(result, seenRoots, component);
                }
            }

            if (runBroadDiscovery)
            {
                foreach (Component component in FindComponentsByTypeName("Enemy"))
                {
                    AddEnemyCandidate(result, seenRoots, component);
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
                    if (Plugin.instance != null) Plugin.instance.nextScanAt = 0f;
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

        private static IEnumerable<Component> FindSpawnedEnemiesFromDirector()
        {
            Type directorType = Type.GetType("EnemyDirector, Assembly-CSharp");
            if (directorType == null) yield break;

            UnityEngine.Object[] directors = Resources.FindObjectsOfTypeAll(directorType);
            foreach (UnityEngine.Object director in directors)
            {
                object spawned = ReadMember(director, "enemiesSpawned");
                if (!(spawned is IEnumerable enumerable)) continue;

                foreach (object item in enumerable)
                {
                    if (item is Component component)
                    {
                        yield return component;
                    }
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

        private string ResolveMonsterName(Component component)
        {
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

        private bool IsVisibleEncounter(Camera camera, EnemyCandidate candidate, string monsterName, bool refreshGameVisibility)
        {
            if (camera == null) return false;

            Transform root = candidate.Root.transform;
            Vector3 center = candidate.Center;
            float distance = Vector3.Distance(camera.transform.position, center);
            if (distance > maxDistance.Value && !IsPeeperVeryClose(candidate, monsterName, distance)) return false;

            if (preferGameOnScreen.Value)
            {
                if (IsGameOnScreen(candidate, refreshGameVisibility)) return true;
                return IsSmallMonsterFallbackVisible(camera, candidate, monsterName);
            }

            Vector3 viewport = camera.WorldToViewportPoint(center);
            float pad = viewportPadding.Value;
            if (viewport.z <= 0f) return false;
            return viewport.x >= -pad && viewport.x <= 1f + pad && viewport.y >= -pad && viewport.y <= 1f + pad;
        }

        private bool IsSmallMonsterFallbackVisible(Camera camera, EnemyCandidate candidate, string monsterName)
        {
            if (monsterName != "Peeper" && monsterName != "Headgrab" && monsterName != "Spewer" && monsterName != "Hidden" && monsterName != "Rugrat" && monsterName != "Upscream"
                && monsterName != "Gnomes" && monsterName != "Tick" && monsterName != "Banger") return false;

            if (IsPeeperVeryClose(candidate, monsterName, Vector3.Distance(camera.transform.position, candidate.Center))) return true;

            float fallbackDistance = Math.Min(maxDistance.Value, 8f);
            if (Vector3.Distance(camera.transform.position, candidate.Center) > fallbackDistance) return false;

            if (IsPointInViewport(camera, candidate.Center)) return true;
            Collider collider = candidate.Root.GetComponentInChildren<Collider>();
            if (collider != null && IsPointInViewport(camera, collider.bounds.center)) return true;
            Renderer renderer = candidate.Root.GetComponentInChildren<Renderer>();
            return renderer != null && IsPointInViewport(camera, renderer.bounds.center);
        }

        private bool IsPeeperVeryClose(EnemyCandidate candidate, string monsterName, float distance)
        {
            if (monsterName != "Peeper" || distance > Math.Max(maxDistance.Value, peeperMaxDistance.Value)) return false;

            Component enemy = candidate.Root.GetComponent("Enemy") ?? candidate.Component;
            object enemyParent = ReadMember(enemy, "EnemyParent");
            object playerVeryClose = ReadMember(enemyParent, "playerVeryClose");
            return playerVeryClose is bool isVeryClose && isVeryClose;
        }

        private bool IsPointInViewport(Camera camera, Vector3 point)
        {
            Vector3 viewport = camera.WorldToViewportPoint(point);
            float pad = viewportPadding.Value;
            if (viewport.z <= 0f) return false;
            return viewport.x >= -pad && viewport.x <= 1f + pad && viewport.y >= -pad && viewport.y <= 1f + pad;
        }

        private static bool IsGameOnScreen(EnemyCandidate candidate, bool refresh)
        {
            Component enemy = candidate.Root.GetComponent("Enemy");
            if (enemy == null && candidate.Component.GetType().Name == "Enemy")
            {
                enemy = candidate.Component;
            }
            if (enemy == null) return false;

            object onScreen = ReadMember(enemy, "OnScreen");
            if (onScreen == null) return false;

            object onScreenLocal = ReadMember(onScreen, "OnScreenLocal");
            if (onScreenLocal is bool value && value) return true;

            object onScreenLocalPrevious = ReadMember(onScreen, "OnScreenLocalPrevious");
            if (onScreenLocalPrevious is bool previousValue && previousValue) return true;
            if (!refresh) return false;

            Type playerControllerType = AccessTools.TypeByName("PlayerController");
            object playerController = ReadMember(playerControllerType, "instance");
            object playerAvatar = ReadMember(playerController, "playerAvatarScript");
            if (playerAvatar == null) return false;

            MethodInfo getOnScreen = onScreen.GetType().GetMethod("GetOnScreen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (getOnScreen == null) return false;
            try
            {
                object result = getOnScreen.Invoke(onScreen, new[] { playerAvatar });
                return result is bool refreshedValue && refreshedValue;
            }
            catch
            {
                return false;
            }
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
            MethodInfo method = type.GetMethod(methodName, flags, null, Type.EmptyTypes, null);
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

        private IEnumerator PostLevel(int level)
        {
            string json = "{\"level\":" + level + "}";
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
            stateObject = SetJsonNumberProperty(stateObject, "mapPlayerCount", 0);
            stateObject = SetJsonNumberProperty(stateObject, "tumbleWings", 0);
            stateObject = SetJsonNumberProperty(stateObject, "crouchRest", 0);
            stateObject = SetJsonNumberProperty(stateObject, "extraJump", 0);
            stateObject = SetJsonNumberProperty(stateObject, "tumbleClimb", 0);
            stateObject = SetJsonRawProperty(stateObject, "monsters", "[]");
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

    }
}
