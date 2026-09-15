# Fixed category slots

In the mod settings, expand **Quick Use Wheel Category Slots** and assign a category
to any of the eight compass positions. For example, set **Bottom** to **Medkits**.
The category page opens first; scroll to reach the ordinary item pages. Assignments
persist in the BepInEx configuration. Set every position to **Unassigned** to hide
the category page and restore the original opening page.

## Selection rules

- The category page always has eight positions, regardless of **Items Per Page**.
- Each slot keeps its chosen item while it remains usable. After depletion,
  removal, or queuing, it prefers a usable favorite, then the shortest access
  delay. Equal candidates are ordered by template and item identifiers.
- Selections survive closing and reopening the wheel in the same raid. Changing
  a slot's category, leaving the raid, or changing player clears its reservation.
- An empty assigned position keeps its category label and shows **No available
  item**. Unassigned positions are blank and cannot activate.
- Medical treatment slots match native treatment capabilities and check resource
  costs. A Salewa can appear in both a medkit slot and a bleeding-treatment slot;
  it stops qualifying for that treatment when it lacks the necessary resources.
- Duplicate assignments are allowed. All represented items also remain on the
  ordinary pages, with their existing grouping and favorite controls.
- Queued items remain on ordinary pages for the existing queue controls. Category
  slots offer usable items and refill when their current item is queued.
- Existing visibility and inventory source settings still apply. Tap-to-reuse
  still selects the last used item template, rather than a category.
- Confirmation rechecks the exact displayed item. If it is no longer eligible,
  that activation is cancelled; it does not silently use another item. A refill
  occurring in the release/click frame also cancels that activation.

This feature uses the existing UI assets and does not change text styling.

## Offline validation

From the repository root:

```powershell
dotnet run --project tests/QuickUseCategorySlots/QuickUseCategorySlots.Tests.csproj -c Release
dotnet build UseItemsAnywhere.sln -c Release -p:DeployOnBuild=false
```

`DeployOnBuild=false` skips the existing post-build installation and release
packaging target. The compiled DLL remains under
`UseItemsAnywhere/bin/Release/netstandard2.1/`. Default build behavior is unchanged.

The test executable links the production inventory, classification, reservation,
entry, and pagination code with substitutes for the native game boundary. The
main project compiles against installed EFT/SPT assemblies. These checks do not
prove live Unity rendering or input behavior.

## In-game acceptance checks

After installing the build and manually restarting the game:

1. Set Bottom to Medkits. Carry two different medkits and verify the category page
   opens first with Medkits at the bottom. Close and reopen it.
2. Consume or discard the selected kit. Verify another eligible kit appears in
   the same position. Remove every kit: the bottom position must remain visible
   and must not activate anything. Acquire another kit and check automatic refill.
3. Set another position to Heavy-Bleed Treatment. Check a multifunction medkit
   appears there only while it has enough resources for bleeding treatment.
4. Check favorite preference and fastest access when choosing a replacement;
   adding a better candidate must not displace a still-usable selected item.
5. Change Items Per Page to 4 and then 12. The category page stays at eight slots;
   scrolling must reach every normal item without selecting the wrong item.
6. Queue a selected item. Its category slot refills or becomes empty; scroll to
   the ordinary page and verify the existing remove-next-item control still works.
7. Check blank positions, empty assigned positions, middle-click favorites,
   release-to-use, pending-request click confirmation, and tap-to-reuse.
8. Clear all assignments, verify the ordinary opening page returns, and test the
   weapon-device wheel. Enter another raid and verify assignments persist while
   item reservations are rebuilt.

Installation should back up existing files and verify copied hashes. Do not stop
or restart client/server processes automatically to work around file locks.
