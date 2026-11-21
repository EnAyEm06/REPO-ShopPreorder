using System;
using System.Collections;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace ShopPreorder
{
    /// <summary>
    /// Talks between players with Photon custom events, so only players who have the mod send or show anything.
    /// Other players send requests to the host; the host does the work and sends results back.
    /// </summary>
    internal static class Net
    {
        private const byte RequestEvent = 147;        // client -> host: a /preorder command
        private const byte MessageEvent = 148;        // host -> client(s): messages to show
        private const byte CatalogRequestEvent = 149; // client -> host: "send me the catalog"
        private const byte CatalogDataEvent = 150;    // host -> client: the catalog
        private const byte ShopStateEvent = 151;      // host -> everyone: preorder state for hints and tags

        private static bool _awaitingReply;
        private static LoadBalancingClient _hookedClient;

        internal static int LocalActor => PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : 0;

        internal static string LocalName =>
            PhotonNetwork.LocalPlayer != null && !string.IsNullOrEmpty(PhotonNetwork.LocalPlayer.NickName)
                ? PhotonNetwork.LocalPlayer.NickName : "Host";

        internal static Requester LocalRequester() => new Requester
        {
            IsHost = true,
            ActorNumber = LocalActor,
            Name = LocalName,
            Reply = Plugin.ShowMessages,
        };

        private static Requester RemoteRequester(int actor)
        {
            var player = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.GetPlayer(actor) : null;
            return new Requester
            {
                IsHost = false,
                ActorNumber = actor,
                Name = player != null && !string.IsNullOrEmpty(player.NickName) ? player.NickName : "Someone",
                Reply = msgs => SendMessages(actor, msgs),
            };
        }

        /// <summary>
        /// Starts listening for preorder messages. Called once the game itself has set up
        /// Photon (when a level loads), never while mods are still loading, because touching
        /// Photon that early forces it to start before the game is ready.
        /// </summary>
        internal static void EnsureHooked()
        {
            try
            {
                var client = PhotonNetwork.NetworkingClient;
                if (client == null || ReferenceEquals(client, _hookedClient)) return;
                if (_hookedClient != null) _hookedClient.EventReceived -= OnEvent;
                client.EventReceived += OnEvent;
                _hookedClient = client;
                Plugin.Log.LogInfo("Listening for preorder messages.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not hook Photon events: " + e);
            }
        }

        private static readonly RaiseEventOptions ToHost = new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient };
        private static readonly RaiseEventOptions ToOthers = new RaiseEventOptions { Receivers = ReceiverGroup.Others };

        private static void Raise(byte code, object data, RaiseEventOptions options)
        {
            EnsureHooked();
            PhotonNetwork.RaiseEvent(code, data, options, SendOptions.SendReliable);
        }

        private static void SendTo(int actor, byte code, object data) =>
            Raise(code, data, new RaiseEventOptions { TargetActors = new[] { actor } });

        // ---------- client -> host ----------

        internal static void SendRequest(string args)
        {
            _awaitingReply = true;
            Raise(RequestEvent, args, ToHost);
            Runner.Ensure().StartCoroutine(ReplyTimeout());
        }

        internal static void RequestCatalog()
        {
            _awaitingReply = true;
            Raise(CatalogRequestEvent, "", ToHost);
            Runner.Ensure().StartCoroutine(ReplyTimeout());
        }

        private static IEnumerator ReplyTimeout()
        {
            yield return new WaitForSeconds(3f);
            if (_awaitingReply)
            {
                _awaitingReply = false;
                Plugin.ShowMessages(new[] { "No answer - the host needs ShopPreorder installed to preorder." });
            }
        }

        // ---------- host -> clients ----------

        internal static void SendMessages(int actor, string[] messages) => SendTo(actor, MessageEvent, messages);

        /// <summary>Sends messages to every other player except one (pass 0 to exclude nobody).</summary>
        internal static void SendToOthers(string[] messages, int exceptActor)
        {
            if (!SemiFunc.IsMultiplayer() || PhotonNetwork.CurrentRoom == null) return;
            foreach (var player in PhotonNetwork.PlayerListOthers)
            {
                if (player.ActorNumber != exceptActor) SendMessages(player.ActorNumber, messages);
            }
        }

        internal static void BroadcastShopState()
        {
            if (!SemiFunc.IsMultiplayer() || PhotonNetwork.CurrentRoom == null) return;
            try { Raise(ShopStateEvent, ShopState.ToNetwork(), ToOthers); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not send shop state: " + e.Message); }
        }

        // ---------- receiving ----------

        private static void OnEvent(EventData e)
        {
            try
            {
                switch (e.Code)
                {
                    case RequestEvent:
                        if (!PhotonNetwork.IsMasterClient) return;
                        Preorders.HandleOnHost(e.CustomData as string ?? "", RemoteRequester(e.Sender));
                        break;

                    case CatalogRequestEvent:
                        if (!PhotonNetwork.IsMasterClient) return;
                        SendTo(e.Sender, CatalogDataEvent, Preorders.BuildCatalog(RemoteRequester(e.Sender)).ToNetwork());
                        SendTo(e.Sender, ShopStateEvent, ShopState.ToNetwork());
                        break;

                    case MessageEvent:
                        _awaitingReply = false;
                        if (e.CustomData is string[] msgs) Plugin.ShowMessages(msgs);
                        break;

                    case CatalogDataEvent:
                        _awaitingReply = false;
                        Plugin.OpenCatalog(CatalogInfo.FromNetwork(e.CustomData as object[]));
                        break;

                    case ShopStateEvent:
                        if (PhotonNetwork.IsMasterClient) return;
                        ShopState.FromNetwork(e.CustomData as object[]);
                        break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Preorder network event failed: " + ex);
            }
        }
    }
}
