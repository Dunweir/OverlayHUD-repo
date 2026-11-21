using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace OverlayHUD
{
    public sealed partial class Plugin
    {
        private readonly Dictionary<Component, int> detectionByParent = new Dictionary<Component, int>();

        private readonly Dictionary<Component, float> visionExpires = new Dictionary<Component, float>();
        private readonly List<Component> expiredVision = new List<Component>();

        private void ExpireVisionProgress()
        {
            foreach (var sample in visionExpires)
                if (!showDetectionBar.Value || sample.Key == null || Time.time >= sample.Value) expiredVision.Add(sample.Key);
            foreach (var parent in expiredVision) SetVisionProgress(parent, 0);
            expiredVision.Clear();
        }

        private static void PatchVisionProgress(Harmony harmony)
        {
            var vision = AccessTools.Method("EnemyVision:Vision");
            if (vision != null)
                harmony.Patch(vision, postfix: new HarmonyMethod(typeof(Plugin), nameof(VisionProgressPostfix)));
        }

        private static void VisionProgressPostfix(Component __instance, ref IEnumerator __result)
        {
            __result = ObserveVision(__instance, __result);
        }

        private static IEnumerator ObserveVision(Component vision, IEnumerator original)
        {
            var previous = new Dictionary<int, int>();
            var active = new HashSet<int>();
            var confirmed = new HashSet<int>();
            while (true)
            {
                bool standOverride = ReadMember(vision, "StandOverrideTimer") is float timer && timer > 0;
                if (!original.MoveNext()) yield break;
                if (instance != null && instance.gameplayActive &&
                    ReadMember(vision, "VisionsTriggered") is Dictionary<int, int> counts &&
                    ReadMember(vision, "VisionTriggered") is Dictionary<int, bool> triggered &&
                    instance.TryGetVisionEnemyCache(vision, out var source))
                {
                    var enemy = ReadMember(vision, "Enemy");
                    string state = ReadMember(enemy, "CurrentState")?.ToString();
                    var target = state == "Chase" ? ReadMember(enemy, "TargetPlayerAvatar") : null;
                    bool trackingTarget = target != null && !(ReadMember(target, "isDisabled") is true)
                        && ReadMember(ReadMember(enemy, "StateChase"), "VisionTimer") is float remaining && remaining > 0;
                    var targetView = trackingTarget ? ReadMember(target, "photonView") as PhotonView : null;
                    foreach (var pair in counts)
                    {
                        previous.TryGetValue(pair.Key, out int last);
                        // Out-of-range and frozen players may retain a nonzero counter.
                        bool detected = triggered.TryGetValue(pair.Key, out bool sawPlayer) && sawPlayer;
                        bool tracked = targetView != null && targetView.ViewID == pair.Key && confirmed.Contains(pair.Key);
                        int percent = 0;
                        if (state != "Despawn" && (detected || tracked))
                        {
                            confirmed.Add(pair.Key);
                            percent = 100;
                        }
                        else
                        {
                            confirmed.Remove(pair.Key);
                            if (pair.Value > last) percent = DetectionPercent(vision, pair.Key, pair.Value, state, standOverride);
                        }
                        if (percent > 0 || active.Contains(pair.Key))
                        {
                            instance.RouteVisionProgress(source.EnemyParent, pair.Key, percent);
                            if (percent > 0) active.Add(pair.Key);
                            else active.Remove(pair.Key);
                        }
                        previous[pair.Key] = pair.Value;
                    }
                }
                yield return original.Current;
            }
        }

        private static int DetectionPercent(Component vision, int avatarId, int count, string state, bool standOverride)
        {
            if (state == "Despawn") return 0;

            string threshold = "VisionsToTrigger";
            if (!standOverride && state != "LookUnder")
            {
                var avatar = PhotonNetwork.InRoom ? PhotonView.Find(avatarId)?.GetComponent("PlayerAvatar")
                    : ReadMember(ResolveGameType("PlayerAvatar"), "instance");
                bool tumble = ReadMember(avatar, "isTumbling") is true;
                if (!tumble && ReadMember(avatar, "isCrawling") is true) threshold = "VisionsToTriggerCrawl";
                else if (tumble || ReadMember(avatar, "isCrouching") is true) threshold = "VisionsToTriggerCrouch";
            }
            int limit = ReadIntMember(vision, threshold);
            return limit > 0 ? Mathf.Clamp(Mathf.RoundToInt(100f * count / limit), 1, 99) : 0;
        }

        private void RouteVisionProgress(Component parent, int avatarId, int percent)
        {
            if (!PhotonNetwork.InRoom || PhotonView.Find(avatarId)?.IsMine == true)
                SetVisionProgress(parent, percent);
            else
                SendVisionProgress(parent, PhotonView.Find(avatarId)?.Owner, percent);
        }

        private int GetVisionProgress(Component parent)
            => parent != null && detectionByParent.TryGetValue(parent, out int percent) ? percent : 0;

        private void SetVisionProgress(Component parent, int percent)
        {
            if (!showDetectionBar.Value) percent = 0;
            if (percent > 0) visionExpires[parent] = Time.time + (percent == 100 ? 2f : .8f);
            else visionExpires.Remove(parent);
            if (parent != null && GetVisionProgress(parent) == percent) return;
            if (percent == 0) detectionByParent.Remove(parent);
            else detectionByParent[parent] = percent;
            SyncEnemyParentStatusChanged(parent);
        }
    }
}
