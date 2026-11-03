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
        // Separate capability preserves cooldown compatibility with 26.11.2 clients.
        private const string HealthProtocol = "OverlayHUD.Health.v1";
        private readonly Dictionary<Component, NetworkHealthSample> networkHealth = new Dictionary<Component, NetworkHealthSample>();
        private long healthSequence;

        private sealed class NetworkHealthSample
        {
            internal long Sequence;
            internal int Master;
            internal float Health;
            internal float MaxHealth;
        }

        private static List<Player> HealthRecipients()
        {
            var recipients = new List<Player>();
            foreach (Player player in PhotonNetwork.PlayerListOthers)
                if (player.CustomProperties[HealthProtocol] is string token && token.Length == 32)
                    recipients.Add(player);
            return recipients;
        }

        private void SendEnemyHealth(Component parent)
        {
            if (!gameplayActive || parent == null || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
            List<Player> recipients = HealthRecipients();
            if (recipients.Count == 0) return;
            object[] row = BuildHealthRow(parent);
            if (row != null) SendHealthRows(recipients, new object[] { row });
        }

        private object[] BuildHealthRow(Component parent)
        {
            PhotonView view = parent.GetComponent<PhotonView>();
            if (view == null || view.ViewID <= 0 || !ReadHostHealth(parent, out float health, out float maxHealth)
                || !ValidHealth(health, maxHealth)) return null;
            return new object[] { view.ViewID, health, maxHealth, ++healthSequence };
        }

        private static bool ValidHealth(float health, float maxHealth)
            => !float.IsNaN(health) && !float.IsInfinity(health) && health >= 0f
                && !float.IsNaN(maxHealth) && !float.IsInfinity(maxHealth) && maxHealth > 0f;

        private void SendHealthSnapshot()
        {
            List<Player> recipients = HealthRecipients();
            if (recipients.Count == 0) return;
            var rows = new List<object>();
            foreach (Component parent in GetKnownEnemyParentsSnapshot())
            {
                if (parent == null) continue;
                object[] row = BuildHealthRow(parent);
                if (row == null) continue;
                rows.Add(row);
                if (rows.Count == 64)
                {
                    SendHealthRows(recipients, rows.ToArray());
                    rows.Clear();
                }
            }
            if (rows.Count > 0) SendHealthRows(recipients, rows.ToArray());
        }

        private static void SendHealthRows(List<Player> recipients, object[] rows)
        {
            foreach (Player player in recipients)
                PhotonNetwork.RaiseEvent(RespawnEventCode,
                    new object[] { HealthProtocol, player.CustomProperties[HealthProtocol], PhotonNetwork.Time, rows },
                    new RaiseEventOptions { TargetActors = new[] { player.ActorNumber } }, SendOptions.SendReliable);
        }

        private void ReceiveHealthEvent(EventData message)
        {
            if (message.Code != RespawnEventCode || !gameplayActive || !PhotonNetwork.InRoom
                || PhotonNetwork.IsMasterClient || message.Sender != PhotonNetwork.MasterClient?.ActorNumber) return;
            if (!(message.CustomData is object[] packet) || packet.Length != 4
                || !Equals(packet[0], HealthProtocol) || !Equals(packet[1], respawnSession)
                || !(packet[2] is double sentAt) || double.IsNaN(sentAt) || double.IsInfinity(sentAt)
                || !(packet[3] is object[] rows) || rows.Length > 64) return;
            double age = PhotonNetwork.Time - sentAt;
            if (age < -1d || age > 5d) return;
            foreach (object value in rows)
            {
                if (!(value is object[] row) || row.Length != 4 || !(row[0] is int viewId)
                    || !(row[1] is float health) || !(row[2] is float maxHealth)
                    || !(row[3] is long sequence) || sequence <= 0 || !ValidHealth(health, maxHealth)) continue;
                PhotonView view = PhotonView.Find(viewId);
                Component parent = view != null ? view.GetComponent("EnemyParent") : null;
                if (parent == null) continue;
                if (networkHealth.TryGetValue(parent, out NetworkHealthSample previous)
                    && previous.Master == message.Sender && previous.Sequence >= sequence) continue;
                networkHealth[parent] = new NetworkHealthSample { Sequence = sequence, Master = message.Sender, Health = health, MaxHealth = maxHealth };
                PublishNetworkHealth(parent);
            }
        }

        private bool TryReadNetworkHealth(Component parent, out float health, out float maxHealth)
        {
            health = maxHealth = 0f;
            if (parent == null || !networkHealth.TryGetValue(parent, out NetworkHealthSample sample)
                || sample.Master != (PhotonNetwork.MasterClient?.ActorNumber ?? 0)) return false;
            health = sample.Health;
            maxHealth = sample.MaxHealth;
            return true;
        }
    }
}
