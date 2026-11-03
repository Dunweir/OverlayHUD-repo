using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

// Minimal transport/Unity substitutes: execute the production protocol without a running game.
namespace UnityEngine
{
    public class Component
    {
        public PhotonView View;
        public bool Alive;
        public float Remaining;
        public T GetComponent<T>() where T : class => View as T;
    }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace ExitGames.Client.Photon
{
    public class Hashtable : System.Collections.Hashtable { }
    public class EventData { public byte Code; public int Sender; public object CustomData; }
    public struct SendOptions { public static SendOptions SendUnreliable => default; public static SendOptions SendReliable => default; }
}
namespace Photon.Realtime
{
    public class Room { }
    public class Player
    {
        public int ActorNumber;
        public Hashtable CustomProperties = new Hashtable();
        public void SetCustomProperties(Hashtable properties)
        {
            foreach (System.Collections.DictionaryEntry entry in properties) CustomProperties[entry.Key] = entry.Value;
        }
    }
    public class RaiseEventOptions { public int[] TargetActors; }
}
namespace Photon.Pun
{
    public class PhotonView
    {
        public int ViewID;
        public Component Parent;
        public static Dictionary<int, PhotonView> Views = new Dictionary<int, PhotonView>();
        public static PhotonView Find(int id) => Views.TryGetValue(id, out var view) ? view : null;
        public Component GetComponent(string name) => Parent;
    }
    public class Client { public event Action<EventData> EventReceived; public void Receive(EventData data) => EventReceived?.Invoke(data); }
    public static class PhotonNetwork
    {
        public static Client NetworkingClient = new Client();
        public static Room CurrentRoom = new Room();
        public static bool InRoom => CurrentRoom != null;
        public static bool IsMasterClient;
        public static Player MasterClient = new Player { ActorNumber = 1 };
        public static Player LocalPlayer = new Player { ActorNumber = 2 };
        public static Player[] PlayerListOthers = Array.Empty<Player>();
        public static double Time = 100;
        public static List<(object[] Packet, int Target)> Sent = new List<(object[], int)>();
        public static void RaiseEvent(byte code, object data, RaiseEventOptions options, SendOptions send)
            => Sent.Add(((object[])data, options.TargetActors[0]));
    }
}
namespace OverlayHUD
{
    public sealed partial class Plugin
    {
        private bool gameplayActive = true;
        private int updated;
        private readonly List<float> publishedHealth = new List<float>();
        private bool ReadHostHealth(Component parent, out float health, out float maxHealth) { health = 70; maxHealth = 100; return true; }
        private void PublishNetworkHealth(Component parent) { if (TryReadNetworkHealth(parent, out float health, out _)) publishedHealth.Add(health); }
        private static List<Component> parents = new List<Component>();
        private static List<Component> GetKnownEnemyParentsSnapshot() => parents;
        private static object ReadMember(Component parent, string name) => parent.Remaining;
        private static bool ReadEnemySpawned(Component parent) => parent.Alive;
        private static bool TryConvertFloat(object value, out float result) { result = (float)value; return true; }
        private void SyncEnemyParentTimerChanged(Component parent) { updated++; }
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

        public static void Main()
        {
            var plugin = new Plugin();
            var parent = new Component { Remaining = 30 };
            parent.View = new PhotonView { ViewID = 7, Parent = parent };
            parents.Add(parent);
            PhotonView.Views[7] = parent.View;
            plugin.TickRespawnNetwork();
            Check(Equals(PhotonNetwork.LocalPlayer.CustomProperties[RespawnProtocol], plugin.respawnSession), "Capability advertised");
            object[] Packet(double at, float remaining) => new object[] { RespawnProtocol, plugin.respawnSession, at, new object[] { new object[] { 7, false, remaining } } };
            void Receive(object packet, int sender = 1) => plugin.ReceiveRespawnEvent(new EventData { Code = RespawnEventCode, Sender = sender, CustomData = packet });
            Receive(Packet(99, 30));
            Check(plugin.ReadNetworkRespawnRemaining(parent, false) == 29, "Subtract network delay");
            Receive(Packet(100, 90), 3);
            Receive(Packet(98, 90));
            Receive(Packet(100, float.NaN));
            Receive(new object[] { "another mod" });
            Check(plugin.updated == 1, "Reject non-host, old, invalid and foreign packets");
            Check(plugin.ReadNetworkRespawnRemaining(parent, true) == 0, "Local spawn takes precedence");
            PhotonNetwork.Time = 106;
            Check(plugin.ReadNetworkRespawnRemaining(parent, false) == 0, "Stale timer expires");
            object[] oldSession = Packet(106, 20);
            plugin.ResetRespawnNetwork();
            Receive(oldSession);
            Check(plugin.networkRespawns.Count == 0, "Reject previous level session");
            Receive(Packet(106, 20));
            PhotonNetwork.MasterClient = new Player { ActorNumber = 3 };
            Check(plugin.ReadNetworkRespawnRemaining(parent, false) == 0, "Old master data invalid immediately");
            plugin.TickRespawnNetwork();
            Check(plugin.networkRespawns.Count == 0, "Master change resets cache");

            object[] HealthPacket(float hp, long sequence) => new object[] { HealthProtocol, plugin.respawnSession, PhotonNetwork.Time,
                new object[] { new object[] { 7, hp, 100f, sequence } } };
            Receive(HealthPacket(100, 1), 3);
            Receive(HealthPacket(70, 2), 3);
            Receive(HealthPacket(40, 3), 3);
            Check(plugin.publishedHealth.Count == 3 && plugin.publishedHealth[1] == 70 && plugin.publishedHealth[2] == 40,
                "Same-timestamp hits retain successive HP values for damage feedback");
            Receive(HealthPacket(90, 2), 3);
            Receive(HealthPacket(90, 4), 1);
            Receive(HealthPacket(float.NaN, 4), 3);
            Check(plugin.publishedHealth.Count == 3, "Reject reordered, non-host and invalid HP");
            Receive(HealthPacket(0, 4), 3);
            Check(plugin.TryReadNetworkHealth(parent, out float hp, out float maxHp) && hp == 0 && maxHp == 100,
                "Lethal damage retains zero HP");
            Receive(HealthPacket(100, 5), 3);
            Check(plugin.TryReadNetworkHealth(parent, out hp, out _) && hp == 100, "Respawn/healing restores HP");
            object[] oldHealth = HealthPacket(10, 6);
            plugin.ResetRespawnNetwork();
            Receive(oldHealth, 3);
            Check(!plugin.TryReadNetworkHealth(parent, out _, out _), "Old level cannot restore HP cache");

            PhotonNetwork.IsMasterClient = true;
            var supported = new Player { ActorNumber = 4 };
            supported.CustomProperties[RespawnProtocol] = new string('a', 32);
            PhotonNetwork.PlayerListOthers = new[] { supported, new Player { ActorNumber = 5 } };
            UnityEngine.Time.realtimeSinceStartup = 10;
            plugin.TickRespawnNetwork();
            Check(PhotonNetwork.Sent.Count == 1 && PhotonNetwork.Sent[0].Target == 4, "Send only to supported client");
            Check(Equals(PhotonNetwork.Sent[0].Packet[1], supported.CustomProperties[RespawnProtocol]), "Echo recipient session");
            plugin.TickRespawnNetwork();
            Check(PhotonNetwork.Sent.Count == 1, "Throttle snapshots");
            supported.CustomProperties[HealthProtocol] = new string('b', 32);
            plugin.SendEnemyHealth(parent);
            Check(PhotonNetwork.Sent.Count == 2 && Equals(PhotonNetwork.Sent[1].Packet[0], HealthProtocol), "Send health immediately to capable client");
            UnityEngine.Time.realtimeSinceStartup = 12;
            plugin.TickRespawnNetwork();
            Check(PhotonNetwork.Sent.Count == 4, "Periodic health and cooldown snapshots recover state");
            plugin.StopRespawnNetwork();
            Check(PhotonNetwork.LocalPlayer.CustomProperties[RespawnProtocol] == null, "Remove capability on unload");
            Console.WriteLine("Respawn network protocol tests passed.");
        }
    }
}
