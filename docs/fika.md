# Fika compatibility

Use Items Anywhere **2.1.6** has a separate optional **UseItemsAnywhereFika 1.0.0** addon, following Skills Extended's core/addon layout. This release targets SPT 4.1.x, EFT 40743, and Fika Core 2.4.3.

## Installation

- Standalone SPT: extract the core ZIP into the game directory.
- Fika: install both ZIPs on every player, the host, and the headless client when used. Their DLLs belong together in `BepInEx/plugins/UseItemsAnywhere/`.
- The core ZIP contains `UseItemsAnywhere.dll`, `quickusewheel`, `itemusedelaytimer`, and the license. The addon ZIP contains `UseItemsAnywhereFika.dll` and the license. Neither includes game, Unity, or Fika dependencies.
- Restart the affected clients after installation. No SPT server plugin or profile migration is required.

## Shared rules

The host captures one immutable rules snapshot when its raid network session is created. It supplies allowed item-category slots, delay enablement, each slot's delay, backpack nesting delay, and movement/damage cancellation. On a headless host, these values come from its own `BepInEx/config/com.cj.useFromAnywhere.cfg`.

Clients apply the snapshot in memory; their saved settings are never overwritten. Gameplay changes made during a raid take effect next raid. Rejoining players receive that raid's existing snapshot. An extracted host retains the snapshot while it continues hosting other players. Network teardown discards it; a new raid captures fresh rules.

Shortcuts, favorites, wheel layout, queue interaction preference, timer appearance, and backpack presentation stay personal. Shared settings displayed in the local configuration editor remain personal values for standalone play or a future hosted raid, not the effective current host rules.

Both wheels resolve Fika's local player. Only that player's item access receives a delay. Once it finishes, normal EFT/Fika controllers perform and replicate the action. The addon does not send duplicate consumption, inventory, reload, grenade, or device-control operations. Custom backpack props, hand animation, and search audio remain local; no custom third-person rummage replication is added.

## Missing or incompatible addon

While awaiting host rules, the mod uses native item access and disables both wheels, delays, and custom presentation. Requests retry once per second for up to 10 seconds after connection. Missing addons, incompatible core/protocol versions, invalid host rules, or lost connections disable features for that raid with a warning. Late replies cannot reactivate a failed session.

All expanded inventory paths honor disabled mode, including container/payment/grenade getters and reload queries. Defensive filtering of invalid bot slots remains active. The addon synchronizes configuration for cooperating clients; it is not an anti-cheat system and does not prevent an unmodded client joining a host.

## Build and packaging

From the repository root:

```powershell
dotnet build UseItemsAnywhere.sln -c Release -p:DeployOnBuild=false
dotnet run --project tests/FikaIntegration/FikaIntegration.Tests.csproj -c Release
dotnet run --project tests/InventorySlotQueries/InventorySlotQueries.Tests.csproj -c Release
dotnet run --project tests/QuickUseCategorySlots/QuickUseCategorySlots.Tests.csproj -c Release
dotnet run --project tests/BackpackHeldItemVisibility/BackpackHeldItemVisibility.Tests.csproj -c Release
dotnet run --project tests/BackpackAccessMotion/BackpackAccessMotion.Tests.csproj -c Release
```

Packages are written to `artifacts/packages/Release/`. Set `PackageUseItemsAnywhere=false` to suppress packaging independently of deployment. Building `UseItemsAnywhere/UseItemsAnywhere.csproj` alone does not require Fika. Override `FikaAssemblyPath` for a separate compatible reference DLL.

Deployment defaults to the normal installation two directories above this repository and `F:\SPT 4.1.x - Headless\`. Override `TarkovDir` and `TarkovHeadlessDir` as needed. The headless installation must already contain BepInEx; the addon only deploys where Fika is installed. `DeployOnBuild=false` disables all installation copies.

## Acceptance

Offline tests exercise the production rules/session logic, real installed LiteNetLib packet serialization, delayed-use coroutines with simulated engine boundaries, and installed EFT/Fika method contracts. They do not prove live Unity rendering or network behavior.

Manually verify standalone, ordinary host/joiner, and headless raids:

- Different personal settings converge to host slot/delay rules; shortcuts and layout remain personal. Change host settings mid-raid and verify they apply only next raid.
- Use backpack meds/food through hotkeys and the wheel; confirm one consumption and normal remote hands animation. Try queue, replacement, manual cancellation, movement, and damage.
- Test extended-slot magazines, loose ammunition, grenades, lights/lasers, and fire-mode selection. Observe another player using items without applying a second delay.
- Die, extract, disconnect, and start another raid with the wheel open or an access request queued. Verify normal input and held-item visibility return.
- Reconnect during a raid and verify the original host rules. Test host extraction while another player remains.
- Remove the addon from a test host or client, or use incompatible versions: expect one warning, native access, and disabled mod features for that raid.

Application restarts and these live raid checks are user-controlled.
