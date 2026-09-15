using System;
using System.Reflection;
using EFT;
using UnityEngine;
using UseItemsAnywhere.BackpackAccess;

var player = new Player { HandsController = new Controller() };
var weaponRoot = player.HandsController.ControllerGameObject;
var weapon = weaponRoot.AddComponent<Renderer>();
var attachment = new GameObject();
weaponRoot.Children.Add(attachment);
var hiddenAttachment = attachment.AddComponent<Renderer>();
hiddenAttachment.forceRenderingOff = true;
var disabledLod = attachment.AddComponent<Renderer>();
disabledLod.enabled = false;
var hands = new GameObject().AddComponent<Renderer>();

var visibility = BackpackHeldItemVisibility.Hide(player)!;
Check(weapon.forceRenderingOff && disabledLod.forceRenderingOff, "Hides weapon and all LODs");
Check(!hands.forceRenderingOff, "Leaves separate hand meshes visible");
disabledLod.enabled = true; // Simulate a native LOD change during suppression.
weapon.forceRenderingOff = false;
Tick(visibility);
Check(weapon.forceRenderingOff, "Maintains suppression during native updates");
visibility.Restore();
visibility.Restore();
Check(!weapon.forceRenderingOff && hiddenAttachment.forceRenderingOff && !disabledLod.forceRenderingOff,
    "Cancellation restores original suppression, including pre-hidden attachments");
Check(disabledLod.enabled, "Preserves native enabled/LOD changes");

visibility = BackpackHeldItemVisibility.Hide(player)!;
visibility.BeginHandoff();
Time.unscaledTime = 0.5f;
Tick(visibility);
Check(weapon.forceRenderingOff, "No old-weapon flash while native item use is starting");
player.HandsController = new Controller();
var retrievedItem = player.HandsController.ControllerGameObject.AddComponent<Renderer>();
Tick(visibility);
Check(!weapon.forceRenderingOff && !retrievedItem.forceRenderingOff, "Controller replacement restores old item without hiding new one");

player.HandsController = new Controller { ControllerGameObject = weaponRoot };
visibility = BackpackHeldItemVisibility.Hide(player)!;
Invoke(visibility, "OnDisable");
Check(!weapon.forceRenderingOff && hiddenAttachment.forceRenderingOff, "Pool disable restores renderer state immediately");
var nextVisibility = BackpackHeldItemVisibility.Hide(player)!;
visibility.Restore(); // Late callback belonging to the previous request.
Check(weapon.forceRenderingOff, "Stale restoration cannot unhide a newer request");
nextVisibility.Restore();

visibility = BackpackHeldItemVisibility.Hide(player)!;
visibility.BeginHandoff();
Time.unscaledTime = 3f;
Tick(visibility);
Check(!weapon.forceRenderingOff, "Missing native callback has a bounded recovery");

visibility = BackpackHeldItemVisibility.Hide(player)!;
player.HealthController!.IsAlive = false;
Tick(visibility);
Check(!weapon.forceRenderingOff, "Death restores visibility");
player.HealthController.IsAlive = true;
visibility = BackpackHeldItemVisibility.Hide(player)!;
weapon.IsDestroyed = true;
Invoke(visibility, "OnDestroy");
Check(hiddenAttachment.forceRenderingOff, "Destroyed renderers do not prevent remaining cleanup");

player.HandsController = null;
Check(BackpackHeldItemVisibility.Hide(player) == null, "Empty hands are harmless");
Console.WriteLine("All held-item visibility lifecycle checks passed.");

static void Tick(BackpackHeldItemVisibility component) => Invoke(component, "LateUpdate");
static void Invoke(BackpackHeldItemVisibility component, string name) =>
    typeof(BackpackHeldItemVisibility).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    Console.WriteLine("PASS: " + description);
}
