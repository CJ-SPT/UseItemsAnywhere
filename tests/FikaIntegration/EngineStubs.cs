using System.Collections;
using System.Reflection;
using EFT.InventoryLogic;
using UseItemsAnywhere.Integration;

namespace UnityEngine
{
    public class Object { public bool Destroyed; public static implicit operator bool(Object? obj) => obj != null && !obj.Destroyed; }
    public static class Time { public static float time; }
    public struct Vector2 { public float sqrMagnitude; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static Dictionary<string, object> PluginInfos = new(); }
}
namespace Comfort.Common
{
    public static class Singleton<T> { public static T? Instance; }
    public delegate void Callback<T>(Result<T> result);
    public sealed class Result<T>(T value, bool succeed = true)
    { public T Value = value; public bool Succeed = succeed; public bool Failed => !Succeed; }
}
namespace EFT.Communications
{
    public static class NotificationManager
    { public static int Warnings; public static void DisplayWarningNotification(string message) => Warnings++; }
}
namespace EFT.UI { }
namespace EFT.Ballistics { public class DamageInfo { } }
public enum EBodyPart { Head }
namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster, Scabbard, Backpack,
        SecuredContainer, TacticalVest, Pockets = 8, Dogtag = 13, ArmBand = 14 }
    public class Item { public object? UsePrefab; public T? GetItemComponent<T>() where T : class => null; }
    public class Meds : Item { }
    public class FoodDrink : Item { }
    public class Weapon : Item { }
    public class ThrowWeap : Item { }
    public class PortableRangeFinder : Item { }
    public class RadioTransmitter : Item { }
    public class KnifeComponent { }
    public class Inventory { }
    public class InventoryController { public Inventory Inventory = new(); }
}
namespace EFT
{
    public interface IBotGame { }
    public class AbstractGame { }
    public class GamePlayerOwner : UnityEngine.Object { public Player Player = null!; }
    public class EftGamePlayerOwner : GamePlayerOwner { }
    public class BaseLocalGame<T> : AbstractGame where T : GamePlayerOwner { public T PlayerOwner = null!; }
    public class LocalGame : BaseLocalGame<EftGamePlayerOwner>, IBotGame { }
    public interface IHandsController { }
    public interface IQuickUseItem : IHandsController
    {
        Comfort.Common.Callback<IQuickUseItem>? GetOnUsedCallback();
        void SetOnUsedCallback(Comfort.Common.Callback<IQuickUseItem> callback);
    }
    public sealed class QuickItem : IQuickUseItem
    {
        public Comfort.Common.Callback<IQuickUseItem>? Callback;
        public Comfort.Common.Callback<IQuickUseItem>? GetOnUsedCallback() => Callback;
        public void SetOnUsedCallback(Comfort.Common.Callback<IQuickUseItem> callback) => Callback = callback;
        public void Finish() => Callback?.Invoke(new(this));
    }
    public sealed class Health
    {
        public bool IsAlive = true;
        public event Action<EBodyPart, float, Ballistics.DamageInfo>? ApplyDamageEvent;
        public int Subscribers => ApplyDamageEvent?.GetInvocationList().Length ?? 0;
        public void Damage() => ApplyDamageEvent?.Invoke(EBodyPart.Head, 1, new());
    }
    public sealed class Movement { public UnityEngine.Vector2 MovementDirection; }
    public class Player : UnityEngine.Object
    {
        public bool IsAI, IsYourPlayer = true;
        public Health HealthController = new();
        public InventoryController InventoryController = new();
        public Movement MovementContext = new();
        public int NativeUses;
        public bool DelayCallback;
        public Comfort.Common.Callback<IHandsController>? PendingCallback;
        public QuickItem Controller = new();
        public List<IEnumerator> Routines = new();
        public void StartCoroutine(IEnumerator routine) { Routines.Add(routine); routine.MoveNext(); }
        public void StopCoroutine(IEnumerator routine) => Routines.Remove(routine);
        public void Step(float time)
        {
            UnityEngine.Time.time = time;
            foreach (var routine in Routines.ToArray()) if (!routine.MoveNext()) Routines.Remove(routine);
        }
        public void TryProceed(Item item, Comfort.Common.Callback<IHandsController> completeCallback, bool scheduled)
        {
            if (!(bool)typeof(UseItemsAnywhere.Patches.ItemAccessDelayPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { this, item, completeCallback, scheduled })!) return;
            NativeUses++;
            if (DelayCallback) PendingCallback = completeCallback;
            else completeCallback?.Invoke(new(Controller));
        }
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    { public static MethodInfo Method(Type type, string name, Type[] parameters) => type.GetMethod(name, parameters)!; }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    public class PatchPrefixAttribute : Attribute { }
}
namespace UseItemsAnywhere
{
    public sealed class Setting<T>(T value) { public T Value = value; }
    public static class Configuration
    {
        public enum PendingUseMode { Ignore, CancelAndReplace, QueueOne, OpenWheel }
        public static Setting<PendingUseMode> PendingItemUseBehavior = new(PendingUseMode.QueueOne);
        public static Setting<bool> ShowTimerPanel = new(false);
        public static GameplayRules LocalRules = null!;
        public static GameplayRules ActiveRules => CoopRuntime.Session.Rules ?? LocalRules;
        public static GameplayRules CaptureGameplayRules() => LocalRules;
        public readonly record struct ItemAccessDelayInfo(float TotalDelay);
        public static bool TryGetItemAccessDelay(Inventory inventory, Item item, out ItemAccessDelayInfo info)
        { info = new(ActiveRules.Delays[EquipmentSlot.Backpack]); return true; }
    }
    public static class Plugin
    {
        public const string PluginVersion = "2.1.6";
        public static ItemUseDelayTimer.ItemUseDelayTimerController? DelayTimer;
        public static Log? LogSource;
        public sealed class Log { public void LogWarning(object? message) { } }
    }
}
namespace UseItemsAnywhere.BackpackAccess
{
    public sealed class BackpackAccessAnimation
    {
        public static int Active;
        private bool _finished;
        public static BackpackAccessAnimation Begin(EFT.Player player, Item item, Configuration.ItemAccessDelayInfo delay)
        { Active++; return new(); }
        public void Update(float remaining) { }
        public void Finish(bool handoffHeldItem = false) { if (!_finished) Active--; _finished = true; }
        public void RestoreHeldItem() { }
    }
}
namespace UseItemsAnywhere.ItemUseDelayTimer
{
    public sealed class ItemUseDelayPresentation { public void Finish(bool completed) { } public void SetRemaining(float remaining) { } }
    public sealed class ItemUseDelayTimerController
    {
        public void EndWaitingForCurrentUse(EFT.Player player) { }
        public void SetQueuedItem(EFT.Player player, Item? item) { }
        public ItemUseDelayPresentation Begin(EFT.Player player, Item item, Configuration.ItemAccessDelayInfo info, Item? next) => new();
        public void ShowWaitingForCurrentUse(EFT.Player player, Item item, Item? next) { }
    }
}
namespace UseItemsAnywhere.QuickUseWheel
{
    public static class QuickUseWheelController { public static void RequestPendingOpen(EFT.Player player) { } }
    public static class QuickUseWheelInventory { public static bool IsItemStillUsable(EFT.Player player, Item item) => true; }
}
