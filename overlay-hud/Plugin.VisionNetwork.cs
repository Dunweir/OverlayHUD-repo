using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace OverlayHUD
{
    public sealed partial class Plugin
    {
        private const string VisionProtocol = "OverlayHUD.Vision.v1";

        private static void SendVisionProgress(Component parent, Player player, int percent)
        {
            if (player == null || !(player.CustomProperties[VisionProtocol] is string session) || session.Length != 32) return;
            var view = parent.GetComponent<PhotonView>();
            if (view == null || view.ViewID <= 0) return;
            // A 26.11.7 packet can reach 100 before detection. Only the extended packet confirms sight.
            var packet = percent == 100
                ? new object[] { VisionProtocol, session, view.ViewID, percent, PhotonNetwork.Time, true }
                : new object[] { VisionProtocol, session, view.ViewID, percent, PhotonNetwork.Time };
            PhotonNetwork.RaiseEvent(RespawnEventCode, packet,
                new RaiseEventOptions { TargetActors = new[] { player.ActorNumber } }, SendOptions.SendReliable);
        }

        private void ReceiveVisionProgress(EventData message)
        {
            if (message.Code != RespawnEventCode || !gameplayActive || !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient
                || message.Sender != PhotonNetwork.MasterClient?.ActorNumber || !(message.CustomData is object[] packet)
                || (packet.Length != 5 && packet.Length != 6) || !Equals(packet[0], VisionProtocol) || !Equals(packet[1], respawnSession)
                || !(packet[2] is int id) || !(packet[3] is int percent) || percent < 0 || percent > 100
                || !(packet[4] is double sentAt) || !(PhotonNetwork.Time - sentAt >= -1 && PhotonNetwork.Time - sentAt <= 1)) return;
            if (packet.Length == 6 && (percent != 100 || !Equals(packet[5], true))) return;
            if (packet.Length == 5 && percent == 100) percent = 99;
            Component parent = PhotonView.Find(id)?.GetComponent("EnemyParent");
            if (parent != null) SetVisionProgress(parent, percent);
        }
    }
}
