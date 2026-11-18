using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace OverlayHUD
{
    public sealed partial class Plugin
    {
        private const string TeamEncounterProtocol = "OverlayHUD.TeamEncounter.v1";
        private readonly List<Component> localTeamEncounters = new List<Component>();
        private readonly HashSet<int> pendingTeamViews = new HashSet<int>();
        private readonly Dictionary<int, EncounterRecipient> teamRecipients = new Dictionary<int, EncounterRecipient>();

        private sealed class EncounterRecipient
        {
            internal string Session;
            internal readonly HashSet<int> SentViews = new HashSet<int>();
        }

        private void ResetTeamEncounters()
        {
            localTeamEncounters.Clear();
            pendingTeamViews.Clear();
            teamRecipients.Clear();
        }

        private void TickTeamEncounters()
        {
            string session = gameplayActive && teamMonsterDetection.Value ? respawnSession : null;
            if (!Equals(PhotonNetwork.LocalPlayer.CustomProperties[TeamEncounterProtocol], session))
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { TeamEncounterProtocol, session } });
            if (session == null)
            {
                ResetTeamEncounters();
                return;
            }

            // The event may arrive before the enemy's PhotonView or the local roster.
            pendingTeamViews.RemoveWhere(TryPublishTeamView);
            if (localTeamEncounters.Count == 0) return;
            foreach (Player player in PhotonNetwork.PlayerListOthers)
            {
                if (!(player.CustomProperties[TeamEncounterProtocol] is string token) || token.Length != 32) continue;
                if (!teamRecipients.TryGetValue(player.ActorNumber, out EncounterRecipient recipient) || recipient.Session != token)
                    teamRecipients[player.ActorNumber] = recipient = new EncounterRecipient { Session = token };

                foreach (Component parent in localTeamEncounters)
                {
                    if (parent == null) continue;
                    PhotonView view = parent.GetComponent<PhotonView>();
                    if (view == null || view.ViewID <= 0 || recipient.SentViews.Contains(view.ViewID)) continue;
                    if (PhotonNetwork.RaiseEvent(RespawnEventCode,
                        new object[] { TeamEncounterProtocol, token, session, view.ViewID },
                        new RaiseEventOptions { TargetActors = new[] { player.ActorNumber } }, SendOptions.SendReliable))
                        recipient.SentViews.Add(view.ViewID);
                }
            }
        }

        private void ReceiveTeamEncounter(EventData message)
        {
            if (message.Code != RespawnEventCode || !(message.CustomData is object[] packet) || packet.Length != 4
                || !Equals(packet[0], TeamEncounterProtocol) || !Equals(packet[1], respawnSession)
                || !(packet[2] is string senderSession) || senderSession.Length != 32
                || !(packet[3] is int viewId) || viewId <= 0 || !gameplayActive || !teamMonsterDetection.Value
                || !PhotonNetwork.InRoom || !ReferenceEquals(PhotonNetwork.CurrentRoom, respawnRoom)) return;
            Player sender = PhotonNetwork.CurrentRoom.GetPlayer(message.Sender);
            if (sender == null || !Equals(sender.CustomProperties[TeamEncounterProtocol], senderSession)) return;
            if (!TryPublishTeamView(viewId) && pendingTeamViews.Count < 256) pendingTeamViews.Add(viewId);
        }

        private bool TryPublishTeamView(int viewId)
        {
            PhotonView view = PhotonView.Find(viewId);
            Component parent = view != null ? view.GetComponent("EnemyParent") : null;
            if (parent == null) return false;
            int instanceId = 0;
            string name = null;
            return PublishEnemyParentEncounter(parent, ref instanceId, ref name, shareEncounter: false);
        }
    }
}
