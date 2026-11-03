using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace OverlayHUD
{
    public sealed partial class Plugin
    {
        // Custom event codes are shared with other mods; validate our envelope as well.
        private const byte RespawnEventCode = 173;
        private const string RespawnProtocol = "OverlayHUD.Respawn.v1";
        private readonly Dictionary<Component, NetworkRespawnSample> networkRespawns = new Dictionary<Component, NetworkRespawnSample>();
        private string respawnSession = Guid.NewGuid().ToString("N");
        private Room respawnRoom;
        private int respawnMaster;
        private float nextRespawnNetworkAt;
        private bool respawnSubscribed;

        private sealed class NetworkRespawnSample
        {
            internal double SentAt;
            internal float Remaining;
            internal bool Alive;
        }

        private void ResetRespawnNetwork()
        {
            networkHealth.Clear();
            networkRespawns.Clear();
            respawnSession = Guid.NewGuid().ToString("N");
            nextRespawnNetworkAt = 0f;
        }

        private void StopRespawnNetwork()
        {
            if (respawnSubscribed) PhotonNetwork.NetworkingClient.EventReceived -= ReceiveRespawnEvent;
            respawnSubscribed = false;
            if (PhotonNetwork.InRoom)
            {
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { RespawnProtocol, null } });
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { HealthProtocol, null } });
            }
            networkHealth.Clear();
            networkRespawns.Clear();
        }

        private void TickRespawnNetwork()
        {
            if (!respawnSubscribed)
            {
                PhotonNetwork.NetworkingClient.EventReceived += ReceiveRespawnEvent;
                respawnSubscribed = true;
            }
            Room room = PhotonNetwork.CurrentRoom;
            int master = PhotonNetwork.MasterClient?.ActorNumber ?? 0;
            if (!ReferenceEquals(room, respawnRoom) || master != respawnMaster)
            {
                respawnRoom = room;
                respawnMaster = master;
                ResetRespawnNetwork();
            }
            if (!PhotonNetwork.InRoom || Time.realtimeSinceStartup < nextRespawnNetworkAt) return;
            nextRespawnNetworkAt = Time.realtimeSinceStartup + 1f;
            // A session token both advertises support and rejects queued packets from a previous level.
            if (!Equals(PhotonNetwork.LocalPlayer.CustomProperties[RespawnProtocol], respawnSession))
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { RespawnProtocol, respawnSession } });
            if (!Equals(PhotonNetwork.LocalPlayer.CustomProperties[HealthProtocol], respawnSession))
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { HealthProtocol, respawnSession } });
            if (!gameplayActive) return;
            if (!PhotonNetwork.IsMasterClient) return;

            SendHealthSnapshot();

            var recipients = new List<Player>();
            foreach (Player player in PhotonNetwork.PlayerListOthers)
                if (player.CustomProperties[RespawnProtocol] is string token && token.Length == 32)
                    recipients.Add(player);
            if (recipients.Count == 0) return;

            var rows = new List<object>();
            foreach (Component parent in GetKnownEnemyParentsSnapshot())
            {
                if (parent == null) continue;
                PhotonView view = parent.GetComponent<PhotonView>();
                if (view == null || view.ViewID <= 0) continue;
                if (!TryConvertFloat(ReadMember(parent, "DespawnedTimer"), out float remaining)
                    || float.IsNaN(remaining) || float.IsInfinity(remaining)) continue;
                rows.Add(new object[] { view.ViewID, ReadEnemySpawned(parent), Math.Max(0f, remaining) });
                if (rows.Count == 64)
                {
                    SendRespawnRows(recipients, rows.ToArray());
                    rows.Clear();
                }
            }
            if (rows.Count > 0) SendRespawnRows(recipients, rows.ToArray());
        }

        private static void SendRespawnRows(List<Player> recipients, object[] rows)
        {
            foreach (Player player in recipients)
                PhotonNetwork.RaiseEvent(RespawnEventCode,
                    new object[] { RespawnProtocol, player.CustomProperties[RespawnProtocol], PhotonNetwork.Time, rows },
                    new RaiseEventOptions { TargetActors = new[] { player.ActorNumber } },
                    SendOptions.SendUnreliable);
        }

        private void ReceiveRespawnEvent(EventData message)
        {
            ReceiveHealthEvent(message);
            if (message.Code != RespawnEventCode || !gameplayActive || !PhotonNetwork.InRoom
                || PhotonNetwork.IsMasterClient || message.Sender != PhotonNetwork.MasterClient?.ActorNumber) return;
            if (!(message.CustomData is object[] packet) || packet.Length != 4
                || !Equals(packet[0], RespawnProtocol) || !Equals(packet[1], respawnSession)
                || !(packet[2] is double sentAt) || double.IsNaN(sentAt) || double.IsInfinity(sentAt)
                || !(packet[3] is object[] rows) || rows.Length > 64) return;
            double age = PhotonNetwork.Time - sentAt;
            if (age < -1d || age > 5d) return;
            foreach (object value in rows)
            {
                if (!(value is object[] row) || row.Length != 3 || !(row[0] is int viewId)
                    || !(row[1] is bool alive) || !(row[2] is float remaining)
                    || float.IsNaN(remaining) || float.IsInfinity(remaining) || remaining < 0f) continue;
                PhotonView view = PhotonView.Find(viewId);
                Component parent = view != null ? view.GetComponent("EnemyParent") : null;
                if (parent == null) continue;
                if (networkRespawns.TryGetValue(parent, out NetworkRespawnSample previous) && previous.SentAt >= sentAt) continue;
                networkRespawns[parent] = new NetworkRespawnSample { SentAt = sentAt, Remaining = remaining, Alive = alive };
                // This path only updates already encountered enemies; it never reveals them.
                SyncEnemyParentTimerChanged(parent);
            }
        }

        private float ReadNetworkRespawnRemaining(Component parent, bool alive)
        {
            if (alive || respawnMaster != (PhotonNetwork.MasterClient?.ActorNumber ?? 0)
                || !networkRespawns.TryGetValue(parent, out NetworkRespawnSample sample) || sample.Alive) return 0f;
            double age = PhotonNetwork.Time - sample.SentAt;
            if (age < -1d || age > 5d) return 0f;
            return Math.Max(0f, sample.Remaining - (float)Math.Max(0d, age));
        }
    }
}
