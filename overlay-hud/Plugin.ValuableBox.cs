using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace OverlayHUD
{
    public partial class Plugin
    {
        // Local instance IDs remain stable when a network ViewID is released on destruction.
        private static readonly HashSet<int> valuablesTransferredToBox = new HashSet<int>();
        private static readonly HashSet<int> extractedValuableBoxes = new HashSet<int>();
        private static readonly HashSet<int> destroyedValuableBoxes = new HashSet<int>();

        private void PatchValuableBoxEvents(Harmony harmony)
        {
            MethodInfo impact = AccessTools.Method("PhysGrabObjectImpactDetector:DestroyObjectRPC");
            if (impact != null)
                harmony.Patch(impact, prefix: new HarmonyMethod(typeof(Plugin), nameof(ValuableBoxImpactPrefix)));
            Patch("OnDestroy", nameof(ValuableBoxExplodePrefix), true);
            Patch("UpdateValueRPC", nameof(ValuableBoxValuePostfix), false);
            Patch("OnExtracted", nameof(ValuableBoxExtractedPrefix), true);
            Patch("ExplodeValuableBox", nameof(ValuableBoxExplodePrefix), true);

            void Patch(string methodName, string hookName, bool prefix)
            {
                MethodInfo method = AccessTools.Method("ItemValuableBox:" + methodName);
                if (method == null) return;
                var hook = new HarmonyMethod(typeof(Plugin).GetMethod(hookName, BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(method, prefix: prefix ? hook : null, postfix: prefix ? null : hook);
                Logger.LogInfo("Patched ItemValuableBox." + methodName + " for stored value tracking.");
            }
        }

        private static void ValuableBoxValuePostfix()
        {
            if (IsRunLevel()) instance?.ScheduleMapValueRefresh("valuable box value changed");
        }

        private static void ValuableBoxExtractedPrefix(object __instance)
        {
            if (IsRunLevel() && __instance is UnityEngine.Object box)
                extractedValuableBoxes.Add(box.GetInstanceID());
        }

        // Pits destroy via the impact detector. Its listeners may clear or disable the box.
        private static void ValuableBoxImpactPrefix(object __instance, PhotonMessageInfo _info)
        {
            if (PhotonNetwork.InRoom && _info.Sender != PhotonNetwork.MasterClient) return;
            ValuableBoxExplodePrefix(__instance);
        }

        private static void ValuableBoxExplodePrefix(object __instance)
        {
            if (instance != null && instance.gameplayActive && IsRunLevel()) AccountForDestroyedValuableBox(__instance);
        }

        private static void AccountForDestroyedValuableBox(object source)
        {
            Component box = (source as Component)?.GetComponent("ItemValuableBox");
            if (box == null || !destroyedValuableBoxes.Add(box.GetInstanceID())) return;
            float value = ReadValuableBoxValue(box);
            instance?.AddMapValue(-value, "valuable box removed");
            if (!extractedValuableBoxes.Contains(box.GetInstanceID()))
                instance?.AddLostValue(value, "valuable box destroyed");
        }

        private static void ValuableBoxAbsorbPrefix(object __instance, object target)
        {
            if (!IsRunLevel()) return;
            Component valuable = GetValuableComponent(target);
            if (valuable == null) return;
            valuablesTransferredToBox.Add(valuable.GetInstanceID());
            // Boxes can be refilled after extraction or reactivation.
            if (__instance is UnityEngine.Object box)
            {
                extractedValuableBoxes.Remove(box.GetInstanceID());
                destroyedValuableBoxes.Remove(box.GetInstanceID());
            }
        }

        private static bool IsValuableTransferredToBox(object valuable)
        {
            return valuable is UnityEngine.Object unityObject
                && valuablesTransferredToBox.Contains(unityObject.GetInstanceID());
        }

        private static float ReadValuableBoxValue(object box)
        {
            float total = ReadFloatMember(box, "currentValue");
            // The original is removed before the absorption animation credits currentValue.
            if (ReadMember(box, "activeAbsorptions") is IEnumerable absorptions)
                foreach (object absorption in absorptions)
                    total += ReadFloatMember(absorption, "valueToAdd");
            return total;
        }

        private static void PhysGrabObjectDestroyingPrefix(object __instance)
        {
            if (!IsRunLevel()) return;

            Component valuable = GetValuableComponent(__instance);
            if (valuable == null)
            {
                AccountForDestroyedValuableBox(__instance);
                return;
            }
            bool wasSubmittedForExtraction = false;
            if (valuable != null)
            {
                int valuableKey = GetValuableKey(valuable);
                if (valuableKey != 0 && !destroyedValuablesAccounted.Add(valuableKey)) return;
                // The box keeps the value; destroying the source object completes a transfer.
                if (IsValuableTransferredToBox(valuable)) return;

                float currentValue = ReadValuableCurrentValue(valuable);
                float originalValue = ReadValuableOriginalValue(valuable);
                bool alreadyAccountedFor = originalValue > 0f && currentValue < originalValue * 0.15f;
                // Haul membership only means the object is in the extraction room.
                // Damage destruction sets dead; normal extraction destroys it without doing so.
                wasSubmittedForExtraction = !(ReadMember(__instance, "dead") is bool dead && dead)
                    && IsValuableInDollarHaul(valuable);
                if (!alreadyAccountedFor)
                {
                    instance?.AddMapValue(-currentValue, "valuable destroyed");
                    if (!wasSubmittedForExtraction)
                    {
                        instance?.AddLostValue(currentValue, "valuable destroyed");
                    }
                }
            }

            if (valuable != null && !wasSubmittedForExtraction)
            {
                instance?.ReportDestroyedItem(__instance);
            }
        }

        private static float CalculateMapValue()
        {
            Type valuableType = ResolveGameType("ValuableObject");
            if (valuableType == null) return 0f;

            long startedAt = Stopwatch.GetTimestamp();
            float total = 0f;
            UnityEngine.Object[] valuables = UnityEngine.Object.FindObjectsOfType(valuableType);
            long discoveredAt = Stopwatch.GetTimestamp();
            for (int index = 0; index < valuables.Length; index++)
            {
                // The search already returns ValuableObject components.
                if (!IsValuableTransferredToBox(valuables[index]))
                    total += ReadFloatMember(valuables[index], "dollarValueCurrent");
            }
            Type boxType = ResolveGameType("ItemValuableBox");
            if (boxType != null)
                foreach (UnityEngine.Object box in UnityEngine.Object.FindObjectsOfType(boxType))
                    total += ReadValuableBoxValue(box);
            double elapsedMs = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs >= 8.0 && instance != null)
            {
                double searchMs = (discoveredAt - startedAt) * 1000.0 / Stopwatch.Frequency;
                instance.Logger.LogWarning("[Performance] Map value scan: "
                    + elapsedMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms, search="
                    + searchMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms, valuables=" + valuables.Length + ".");
            }
            return Math.Max(0f, total);
        }
    }
}
