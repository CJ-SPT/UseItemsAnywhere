using System;
using BepInEx;
using Comfort.Common;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using UnityEngine;
using UseItemsAnywhere;
using UseItemsAnywhere.Integration;

namespace UseItemsAnywhereFika;

[BepInPlugin("com.cj.UseItemsAnywhereFika", "Use Items Anywhere Fika", "1.0.0")]
[BepInDependency(Plugin.PluginGuid, Plugin.PluginVersion)]
[BepInDependency("com.fika.core", "2.4.3")]
[BepInDependency("com.fika.headless", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class FikaSyncPlugin : BaseUnityPlugin
{
    private FikaServer? _server;
    private FikaClient? _client;
    private GameplayRules? _hostRules;
    private bool _connected;
    private bool _ended;
    private float _nextRequest;

    private void Awake()
    {
        CoopRuntime.AddonPresent = true;
        FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnManagerCreated);
        FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnManagerDestroyed);
        FikaEventDispatcher.SubscribeEvent<FikaGameCreatedEvent>(OnGameCreated);
        FikaEventDispatcher.SubscribeEvent<FikaGameEndedEvent>(OnGameEnded);
        FikaEventDispatcher.SubscribeEvent<PeerConnectedEvent>(OnPeerConnected);
        FikaEventDispatcher.SubscribeEvent<PeerDisconnectedEvent>(OnPeerDisconnected);
        if (Singleton<IFikaNetworkManager>.Instance is { } manager) Connect(manager);
    }

    private void OnManagerCreated(FikaNetworkManagerCreatedEvent e) => Connect(e.Manager);

    private void Connect(IFikaNetworkManager manager)
    {
        Detach();
        _server = manager as FikaServer;
        _client = manager as FikaClient;
        _server?.RegisterPacket<RulesRequestPacket, NetPeer>(ReceiveRequest);
        _client?.RegisterPacket<RulesReplyPacket>(ReceiveReply);
        _connected = _client?.ServerConnection != null;
        BeginRaid();
    }

    private void BeginRaid()
    {
        _ended = false;
        _hostRules = null;
        _nextRequest = 0;
        CoopRuntime.BeginSession();
        if (_server != null)
        {
            try
            {
                _hostRules = CoopRuntime.CaptureLocalRules();
                CoopRuntime.Session.Apply(CoopRuntime.Session.RequestId, _hostRules);
                Logger.LogInfo("Captured host item-access rules for this raid.");
            }
            catch (Exception e)
            {
                CoopRuntime.Disable("The host's item-access configuration is invalid: " + e.Message);
            }
        }
    }

    private void OnGameCreated(FikaGameCreatedEvent e)
    {
        // A manager can survive a transit. Each new raid still gets fresh rules.
        if (_ended && (_server != null || _client != null)) BeginRaid();
    }

    private void OnGameEnded(FikaGameEndedEvent e)
    {
        _ended = true;
        // An extracted host may still service other players. Its inventory
        // validation and reconnect replies must retain this raid's rules.
        if (_server != null) CoopRuntime.ClearLocalWork();
        else CoopRuntime.EndSession();
    }

    private void OnManagerDestroyed(FikaNetworkManagerDestroyedEvent e)
    {
        if (!ReferenceEquals(e.Manager, _server) && !ReferenceEquals(e.Manager, _client)) return;
        Detach();
        CoopRuntime.EndSession();
    }

    private void OnPeerConnected(PeerConnectedEvent e)
    {
        if (ReferenceEquals(e.NetworkManager, _client)) _connected = true;
    }

    private void OnPeerDisconnected(PeerDisconnectedEvent e)
    {
        if (_client == null) return;
        _connected = false;
        CoopRuntime.Disable("Connection to the host was lost. Mod features are disabled for this raid.");
    }

    private void Update()
    {
        if (_ended || _client == null || !_connected || CoopRuntime.Session.Status != RuleSessionStatus.Waiting) return;
        var now = Time.realtimeSinceStartup;
        CoopRuntime.Session.Connected(now);
        CoopRuntime.Session.Tick(now);
        if (CoopRuntime.Session.Status != RuleSessionStatus.Waiting || now < _nextRequest) return;
        _nextRequest = now + 1f;
        var packet = new RulesRequestPacket
        {
            Protocol = RulesCodec.ProtocolVersion,
            CoreVersion = Plugin.PluginVersion,
            RequestId = CoopRuntime.Session.RequestId,
        };
        _client.SendData(ref packet, DeliveryMethod.ReliableOrdered, false);
    }

    private void ReceiveRequest(RulesRequestPacket request, NetPeer peer)
    {
        if (_server == null || !Guid.TryParseExact(request.RequestId, "N", out _)) return;
        // Rules are read-only host state. No client identity or supplied gameplay values are trusted.
        var compatible = request.Protocol == RulesCodec.ProtocolVersion && request.CoreVersion == Plugin.PluginVersion;
        var reply = new RulesReplyPacket
        {
            Protocol = RulesCodec.ProtocolVersion,
            CoreVersion = Plugin.PluginVersion,
            RequestId = request.RequestId,
            RulesJson = compatible && _hostRules != null ? RulesCodec.Serialize(_hostRules) : string.Empty,
            Error = !compatible ? "Use Items Anywhere core/addon versions do not match the host."
                : _hostRules == null ? "The host's item-access rules are unavailable." : string.Empty,
        };
        _server.SendDataToPeer(ref reply, DeliveryMethod.ReliableOrdered, peer);
    }

    private void ReceiveReply(RulesReplyPacket reply)
    {
        if (CoopRuntime.Session.Status != RuleSessionStatus.Waiting || reply.RequestId != CoopRuntime.Session.RequestId) return;
        try
        {
            var rules = RulesCodec.DecodeReply(reply);
            if (CoopRuntime.Session.Apply(reply.RequestId, rules)) Logger.LogInfo("Applied host item-access rules for this raid; personal configuration is unchanged.");
        }
        catch (Exception e)
        {
            CoopRuntime.Disable("Host rules were rejected: " + e.Message);
        }
    }

    private void Detach()
    {
        _server?.UnregisterPacket<RulesRequestPacket>();
        _client?.UnregisterPacket<RulesReplyPacket>();
        _server = null;
        _client = null;
        _hostRules = null;
        _connected = false;
        _ended = true;
    }

    private void OnDestroy()
    {
        FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerCreatedEvent>(OnManagerCreated);
        FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnManagerDestroyed);
        FikaEventDispatcher.UnsubscribeEvent<FikaGameCreatedEvent>(OnGameCreated);
        FikaEventDispatcher.UnsubscribeEvent<FikaGameEndedEvent>(OnGameEnded);
        FikaEventDispatcher.UnsubscribeEvent<PeerConnectedEvent>(OnPeerConnected);
        FikaEventDispatcher.UnsubscribeEvent<PeerDisconnectedEvent>(OnPeerDisconnected);
        Detach();
        CoopRuntime.EndSession();
        CoopRuntime.AddonPresent = false;
    }
}
