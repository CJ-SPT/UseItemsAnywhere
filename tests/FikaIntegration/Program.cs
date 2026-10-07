using System.Reflection;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Fika.Core.Networking.LiteNetLib.Utils;
using Mono.Cecil;
using UseItemsAnywhere;
using UseItemsAnywhere.BackpackAccess;
using UseItemsAnywhere.Integration;
using UseItemsAnywhere.Patches;
using UseItemsAnywhereFika;

var checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
void Reject(Action action, string message)
{
    try { action(); } catch (ArgumentException) { checks++; return; }
    catch (Newtonsoft.Json.JsonException) { checks++; return; }
    throw new Exception(message);
}
GameplayRulesData Data(float delay = 1) => new()
{
    Slots = Enum.GetValues<ItemCategory>().ToDictionary(category => category, _ => new[] { EquipmentSlot.Backpack }),
    Delays = new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest, EquipmentSlot.ArmBand,
        EquipmentSlot.Backpack, EquipmentSlot.SecuredContainer }.ToDictionary(slot => slot, _ => delay),
    EnableDelays = true, NestingDelay = 0.5f, CancelOnMovement = true, CancelOnDamage = true,
};
var data = Data();
var rules = new GameplayRules(data);
data.Slots[ItemCategory.Meds][0] = EquipmentSlot.Pockets;
data.Delays[EquipmentSlot.Backpack] = 5;
var exported = rules.ToData();
exported.Slots[ItemCategory.Meds][0] = EquipmentSlot.Pockets;
Check(rules.Slots(ItemCategory.Meds)[0] == EquipmentSlot.Backpack && rules.Delays[EquipmentSlot.Backpack] == 1,
    "Snapshot owns both incoming and outgoing collections");
var json = RulesCodec.Serialize(rules);
Check(RulesCodec.Serialize(RulesCodec.Deserialize(json)) == json, "Rules JSON round trip");
foreach (var invalid in new[] { -1f, 5.1f, float.NaN, float.PositiveInfinity })
{
    var bad = Data(); bad.Delays[EquipmentSlot.Backpack] = invalid;
    Reject(() => new GameplayRules(bad), "Reject invalid delays");
}
var missing = Data(); missing.Slots.Remove(ItemCategory.Reload);
Reject(() => new GameplayRules(missing), "Reject missing category");
var invalidSlot = Data(); invalidSlot.Slots[ItemCategory.Other] = [(EquipmentSlot)99];
Reject(() => new GameplayRules(invalidSlot), "Reject undefined slots");
Reject(() => RulesCodec.Deserialize(new string('x', RulesCodec.MaximumJsonLength + 1)), "Reject oversized rules");
Reject(() => RulesCodec.Deserialize("{\"unexpected\":1}"), "Reject unknown schema");

// Exercise actual installed LiteNetLib serialization, not a substitute writer.
var request = new RulesRequestPacket { Protocol = 1, CoreVersion = "2.1.6", RequestId = Guid.NewGuid().ToString("N") };
var writer = new NetDataWriter();
request.Serialize(writer);
var requestCopy = new RulesRequestPacket();
requestCopy.Deserialize(new NetDataReader(writer.CopyData()));
Check(requestCopy.RequestId == request.RequestId && requestCopy.CoreVersion == request.CoreVersion && requestCopy.Protocol == 1,
    "Request packet round trip");
var reply = new RulesReplyPacket { Protocol = 1, CoreVersion = "2.1.6", RequestId = request.RequestId, RulesJson = json, Error = "" };
writer.Reset(); reply.Serialize(writer);
var replyCopy = new RulesReplyPacket(); replyCopy.Deserialize(new NetDataReader(writer.CopyData()));
Check(replyCopy.RequestId == request.RequestId && replyCopy.RulesJson == json && replyCopy.Error == "", "Reply packet round trip");
Check(RulesCodec.Serialize(RulesCodec.DecodeReply(replyCopy)) == json, "Matching reply accepted");
replyCopy.Protocol++;
Reject(() => RulesCodec.DecodeReply(replyCopy), "Protocol mismatch rejected");
replyCopy = reply; replyCopy.CoreVersion = "old";
Reject(() => RulesCodec.DecodeReply(replyCopy), "Core version mismatch rejected");
replyCopy = reply; replyCopy.Error = "Host unavailable";
Reject(() => RulesCodec.DecodeReply(replyCopy), "Host failure rejected");
Reject(() => RulesCodec.Deserialize(json.Replace("\"EnableDelays\":true,", "")), "Incomplete payload rejected");

var session = new RuleSession();
session.Begin(); session.Tick(1000);
Check(session.Status == RuleSessionStatus.Waiting, "Loading before connection does not consume timeout");
session.Connected(1000); session.Connected(1005); session.Tick(1009.99);
Check(session.Status == RuleSessionStatus.Waiting, "Retry does not restart deadline");
var oldRequest = session.RequestId;
session.Tick(1010);
Check(session.Status == RuleSessionStatus.Disabled && !session.Apply(oldRequest, rules), "Timeout is permanent for this raid");
session.Begin();
Check(!session.Apply(oldRequest, rules), "Prior raid reply rejected");
Check(session.Apply(session.RequestId, rules) && !session.Apply(session.RequestId, new GameplayRules(Data(5))), "First valid snapshot is frozen");
session.Reset();
Check(session.Rules == null && session.Status == RuleSessionStatus.Standalone, "Reset drops shared rules");

Configuration.LocalRules = rules;
var localPlayer = new Player();
Singleton<AbstractGame>.Instance = new LocalGame { PlayerOwner = new() { Player = localPlayer } };
Check(CoopRuntime.FeaturesEnabled && CoopRuntime.IsLocalPlayer(localPlayer), "Standalone local player enabled");
Check(!CoopRuntime.IsLocalPlayer(new Player()) && !CoopRuntime.IsLocalPlayer(new Player { IsAI = true }), "Remote players and bots excluded");
Chainloader.PluginInfos.Add("com.fika.headless", new());
Check(!CoopRuntime.IsLocalPlayer(localPlayer), "Headless never owns presentation");
Chainloader.PluginInfos.Clear();
Singleton<AbstractGame>.Instance = new BaseLocalGame<EftGamePlayerOwner> { PlayerOwner = new() { Player = localPlayer } };
Check(!CoopRuntime.FeaturesEnabled && CoopRuntime.IsLocalPlayer(localPlayer), "Co-op owner resolves while rules are inactive");
CoopRuntime.Tick(); CoopRuntime.Tick();
Check(CoopRuntime.Session.Status == RuleSessionStatus.Disabled && EFT.Communications.NotificationManager.Warnings == 1,
    "Missing addon disables features and warns once");
Singleton<AbstractGame>.Instance = new LocalGame { PlayerOwner = new() { Player = localPlayer } };
CoopRuntime.Tick();
Check(CoopRuntime.FeaturesEnabled, "Leaving missing-addon raid restores standalone");
CoopRuntime.BeginSession(); CoopRuntime.Session.Apply(CoopRuntime.Session.RequestId, new GameplayRules(Data(2)));
Check(Configuration.ActiveRules.Delays[EquipmentSlot.Backpack] == 2 && Configuration.LocalRules.Delays[EquipmentSlot.Backpack] == 1,
    "Host rules override without changing personal settings");
CoopRuntime.EndSession();
Check(ReferenceEquals(Configuration.ActiveRules, rules), "Personal rules restored after raid");

CoopRuntime.Cleanup += ItemAccessDelayPatch.ResetSession;
void Fresh()
{
    CoopRuntime.EndSession();
    UnityEngine.Time.time = 0;
    localPlayer = new Player();
    Singleton<AbstractGame>.Instance = new LocalGame { PlayerOwner = new() { Player = localPlayer } };
    CoopRuntime.Tick();
}
void Use(Player player) => player.TryProceed(new Meds(), _ => { }, false);
Fresh(); Use(localPlayer);
Check(localPlayer.NativeUses == 0 && BackpackAccessAnimation.Active == 1, "Delay precedes native use");
localPlayer.Step(2); localPlayer.Step(3);
Check(localPlayer.NativeUses == 1 && BackpackAccessAnimation.Active == 0 && localPlayer.HealthController.Subscribers == 0,
    "Completion enters native use exactly once and releases presentation/events");
Fresh(); Use(localPlayer); ItemAccessDelayPatch.ClearPendingItemAccess(); localPlayer.Step(2);
Check(localPlayer.NativeUses == 0, "Manual cancellation never uses item");
Fresh(); Use(localPlayer); localPlayer.HealthController.Damage(); localPlayer.Step(2);
Check(localPlayer.NativeUses == 0, "Damage cancellation never uses item");
Fresh(); Use(localPlayer); localPlayer.MovementContext.MovementDirection.sqrMagnitude = 1; localPlayer.Step(0.5f);
Check(localPlayer.NativeUses == 0 && localPlayer.Routines.Count == 0, "Movement cancellation releases coroutine");
Fresh(); Use(localPlayer); Use(localPlayer); localPlayer.HealthController.IsAlive = false; CoopRuntime.Tick(); localPlayer.Step(2);
Check(localPlayer.NativeUses == 0 && localPlayer.Routines.Count == 0 && localPlayer.HealthController.Subscribers == 0,
    "Death clears current and queued use immediately");
Fresh(); Use(localPlayer); Use(localPlayer); CoopRuntime.Disable("test disconnect"); localPlayer.Step(2);
Check(localPlayer.NativeUses == 0 && BackpackAccessAnimation.Active == 0, "Disconnect cleans presentation without executing queue");
Fresh(); Use(localPlayer); Use(localPlayer); localPlayer.Step(2); localPlayer.Controller.Finish(); localPlayer.Step(4);
Check(localPlayer.NativeUses == 2, "Queued item waits until first native item finishes");
Fresh(); localPlayer.DelayCallback = true;
var callbacks = 0;
localPlayer.TryProceed(new Meds(), _ => callbacks++, false); localPlayer.Step(2);
var callback = localPlayer.PendingCallback!;
CoopRuntime.EndSession(); callback(new(localPlayer.Controller));
Check(callbacks == 0, "Late native callback cannot revive ended session");
Fresh(); localPlayer.DelayCallback = true;
localPlayer.TryProceed(new Meds(), _ => callbacks++, false); localPlayer.Step(2);
localPlayer.PendingCallback!(new(localPlayer.Controller)); localPlayer.PendingCallback!(new(localPlayer.Controller));
Check(callbacks == 1 && localPlayer.NativeUses == 1, "Repeated native callback completes once");
Fresh(); var remote = new Player { IsYourPlayer = false }; Use(remote);
Check(remote.NativeUses == 1 && remote.Routines.Count == 0, "Observed use bypasses delay");
CoopRuntime.BeginSession(); Use(localPlayer);
Check(localPlayer.NativeUses == 1 && localPlayer.Routines.Count == 0, "Waiting for rules uses native behavior");
CoopRuntime.EndSession();

// Confirm the contracts on the installed binaries and the built core boundary.
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var game = Path.GetFullPath(Path.Combine(root, "../.."));
using var eft = AssemblyDefinition.ReadAssembly(Path.Combine(game, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"));
using var fika = AssemblyDefinition.ReadAssembly(Path.Combine(game, "BepInEx/plugins/Fika/Fika.Core.dll"));
Check(eft.MainModule.GetType("EFT.LocalGame").BaseType.FullName == fika.MainModule.GetType("Fika.Core.Main.GameMode.CoopGame").BaseType.FullName,
    "EFT and Fika share the local-game owner base");
Check(!fika.MainModule.GetType("Fika.Core.Main.GameMode.CoopGame").Interfaces.Any(i => i.InterfaceType.Name == "IBotGame")
    && fika.MainModule.GetType("Fika.Core.Main.GameMode.HostGameController").Interfaces.Any(i => i.InterfaceType.Name == "IBotGame"),
    "Fika bot singleton is a controller, never the playable game");
Check(fika.MainModule.GetType("Fika.Core.Main.Patches.LocalGame.TarkovApplication_LocalGameCreator_Patch").NestedTypes
    .SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
    .Any(i => i.Operand?.ToString()?.Contains("Singleton`1<EFT.AbstractGame>::Create") == true),
    "Fika publishes its playable game through the AbstractGame singleton");
Check(!fika.MainModule.GetType("Fika.Core.Main.Players.FikaPlayer").Methods.Any(m => m.Name == "TryProceed"), "Fika inherits patched TryProceed");
Check(fika.MainModule.GetType("Fika.Core.Main.Players.FikaPlayer").Methods.Count(m => m.Name == "Proceed") >= 10, "Fika owns native hands replication");
using var core = AssemblyDefinition.ReadAssembly(Path.Combine(root, "UseItemsAnywhere/bin/Release/netstandard2.1/UseItemsAnywhere.dll"));
Check(!core.MainModule.AssemblyReferences.Any(a => a.Name.StartsWith("Fika")), "Core loads without Fika assemblies");
Console.WriteLine($"PASS: {checks} Fika rules, wire packets, ownership, cancellation, lifecycle, and installed-contract assertions.");
