using System;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;

namespace UseItemsAnywhere.Integration;

// This assembly deliberately has no Fika reference. Only the addon owns networking.
public static class CoopRuntime
{
    public static RuleSession Session { get; } = new();
    public static bool AddonPresent { get; set; }
    public static bool IsHeadless => Chainloader.PluginInfos.ContainsKey("com.fika.headless");
    private static bool IsCoopGame => Singleton<AbstractGame>.Instance is BaseLocalGame<EftGamePlayerOwner>
        && Singleton<AbstractGame>.Instance is not LocalGame;
    private static bool _warned;
    private static bool _missingAddonRaid;
    private static Player? _lastPlayer;
    public static event Action? Cleanup;

    public static bool FeaturesEnabled => Session.Status == RuleSessionStatus.Active
        || Session.Status == RuleSessionStatus.Standalone && !IsCoopGame;

    public static GameplayRules CaptureLocalRules() => Configuration.CaptureGameplayRules();

    public static void ClearLocalWork() => Cleanup?.Invoke();

    public static bool IsLocalPlayer(Player? player) => !IsHeadless && player != null && player
        && !player.IsAI && player.IsYourPlayer
        && Singleton<AbstractGame>.Instance is BaseLocalGame<EftGamePlayerOwner> game
        && game.PlayerOwner && ReferenceEquals(game.PlayerOwner.Player, player)
        && player.HealthController?.IsAlive == true;

    public static void BeginSession()
    {
        Cleanup?.Invoke();
        Session.Begin();
        _warned = false;
    }

    public static void Disable(string reason)
    {
        Session.Disable(reason);
        Cleanup?.Invoke();
    }

    public static void EndSession()
    {
        Cleanup?.Invoke();
        Session.Reset();
        _warned = false;
        _lastPlayer = null;
    }

    internal static void Tick()
    {
        if (!AddonPresent && IsCoopGame && !_missingAddonRaid)
        {
            _missingAddonRaid = true;
            BeginSession();
            Disable("The Use Items Anywhere Fika addon is missing. Mod features are disabled for this raid.");
        }
        else if (_missingAddonRaid && !IsCoopGame)
        {
            _missingAddonRaid = false;
            EndSession();
        }

        var player = (Singleton<AbstractGame>.Instance as BaseLocalGame<EftGamePlayerOwner>)?.PlayerOwner?.Player;
        if (!IsLocalPlayer(player)) player = null;
        if (!ReferenceEquals(player, _lastPlayer))
        {
            Cleanup?.Invoke();
            _lastPlayer = player;
        }
        if (Session.Status == RuleSessionStatus.Disabled && !_warned)
        {
            Plugin.LogSource?.LogWarning(Session.Failure);
            if (!IsHeadless) EFT.Communications.NotificationManager.DisplayWarningNotification("Use Items Anywhere: " + Session.Failure);
            _warned = true;
        }
    }
}
