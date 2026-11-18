using System.Reflection;
using OverlayHUD;
using UnityEngine;

Plugin.RunTests();

namespace OverlayHUD
{
    public partial class Plugin
    {
        static Plugin instance = new();
        static HashSet<int> destroyedValuablesAccounted = new();
        static bool runLevel = true;
        float mapValue = 10000, lostValue;
        int alerts;
        readonly Setting debugLogging = new();
        readonly Log Logger = new();
        static bool IsRunLevel() => runLevel;
        static Type ResolveGameType(string name) => name == "ValuableObject" ? typeof(ValuableObject) : typeof(ItemValuableBox);
        static Component GetValuableComponent(object o) => (o as Component)?.GetComponent("ValuableObject");
        static int GetValuableKey(object o) => ((Component)o).GetInstanceID();
        static object ReadMember(object o, string name) => o?.GetType().GetField(name)?.GetValue(o);
        static float ReadFloatMember(object o, string name) => Convert.ToSingle(ReadMember(o, name) ?? 0f);
        static float ReadValuableCurrentValue(object o) => ReadFloatMember(o, "dollarValueCurrent");
        static float ReadValuableOriginalValue(object o) => ReadFloatMember(o, "dollarValueOriginal");
        static bool IsValuableInDollarHaul(object o) => ((ValuableObject)o).inHaul;
        void AddMapValue(float value, string reason) => mapValue += value;
        void AddLostValue(float value, string reason) => lostValue += value;
        void ReportDestroyedItem(object o) => alerts++;
        void ScheduleMapValueRefresh(string reason) { }

        public static void RunTests()
        {
            int checks = 0;
            void Check(bool condition, string message)
            {
                checks++;
                if (!condition) throw new Exception(message);
            }
            var valuable = new ValuableObject { dollarValueCurrent = 1750, dollarValueOriginal = 2000 };
            var phys = new PhysGrabObject();
            phys.Components["ValuableObject"] = valuable;
            var box = new ItemValuableBox();
            UnityEngine.Object.World.Add(valuable);
            UnityEngine.Object.World.Add(box);
            Check(CalculateMapValue() == 1750, "Loose value missing");

            // StartAbsorbLocal runs on host and clients before the host removes the original.
            ValuableBoxAbsorbPrefix(phys);
            ValuableBoxAbsorbPrefix(phys);
            box.activeAbsorptions.Add(new Absorption { valueToAdd = 1750 });
            PhysGrabObjectDestroyingPrefix(phys);
            PhysGrabObjectDestroyingPrefix(phys); // RPC fallback must be idempotent.
            Check(instance.lostValue == 0 && instance.alerts == 0, "Transfer counted as destruction");
            Check(instance.mapValue == 10000, "Transfer reduced remaining value");
            Check(CalculateMapValue() == 1750, "Animation double-counted the original or lost pending value");
            UnityEngine.Object.World.Remove(valuable);
            Check(CalculateMapValue() == 1750, "Pending absorption not counted after original removal");
            box.activeAbsorptions.Clear();
            box.currentValue = 1750;
            Check(CalculateMapValue() == 1750, "Stored value lost after animation");

            // Ordinary damage and extraction must keep their original accounting behavior.
            var broken = new ValuableObject { dollarValueCurrent = 800, dollarValueOriginal = 1000, inHaul = true };
            var brokenPhys = new PhysGrabObject { dead = true };
            brokenPhys.Components["ValuableObject"] = broken;
            PhysGrabObjectDestroyingPrefix(brokenPhys);
            Check(instance.lostValue == 800 && instance.alerts == 1, "Real destruction suppressed");
            var submitted = new ValuableObject { dollarValueCurrent = 700, dollarValueOriginal = 700, inHaul = true };
            var submittedPhys = new PhysGrabObject();
            submittedPhys.Components["ValuableObject"] = submitted;
            PhysGrabObjectDestroyingPrefix(submittedPhys);
            Check(instance.lostValue == 800 && instance.alerts == 1, "Normal extraction counted as loss");

            // Destroying a loaded box really loses its contents, exactly once.
            float before = instance.mapValue;
            ValuableBoxExplodePrefix(box);
            AccountForDestroyedValuableBox(box);
            Check(instance.lostValue == 2550 && instance.mapValue == before - 1750, "Box loss missing or duplicated");
            var extracted = new ItemValuableBox { currentValue = 1000 };
            ValuableBoxExtractedPrefix(extracted);
            AccountForDestroyedValuableBox(extracted);
            Check(instance.lostValue == 2550, "Extracted box counted as lost");

            runLevel = false;
            var outside = new ValuableObject();
            ValuableBoxAbsorbPrefix(outside);
            Check(!IsValuableTransferredToBox(outside), "Transfer tracked outside run");
            Console.WriteLine($"Valuable box checks passed: {checks}.");
        }
    }
    class Setting { public bool Value = false; }
    class Log { public void LogInfo(string text) { } public void LogWarning(string text) { } }
    class ValuableObject : Component
    {
        public float dollarValueCurrent, dollarValueOriginal;
        public bool inHaul;
        public ValuableObject() { Components["ValuableObject"] = this; }
    }
    class PhysGrabObject : Component { public bool dead; }
    class Absorption { public float valueToAdd; }
    class ItemValuableBox : Component
    {
        public float currentValue;
        public List<Absorption> activeAbsorptions = new();
        public ItemValuableBox() { Components["ItemValuableBox"] = this; }
    }
}
namespace UnityEngine
{
    public class Object
    {
        static int nextId;
        readonly int id = ++nextId;
        public static List<Object> World = new();
        public int GetInstanceID() => id;
        public static Object[] FindObjectsOfType(Type type) => World.Where(type.IsInstanceOfType).ToArray();
    }
    public class Component : Object
    {
        public Dictionary<string, Component> Components = new();
        public Component GetComponent(string name) => Components.GetValueOrDefault(name);
    }
}
namespace HarmonyLib
{
    public class Harmony
    {
        public void Patch(MethodInfo method, HarmonyMethod prefix = null, HarmonyMethod postfix = null) { }
    }
    public class HarmonyMethod { public HarmonyMethod(MethodInfo method) { } }
    public static class AccessTools { public static MethodInfo Method(string name) => null; }
}
