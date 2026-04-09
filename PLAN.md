# World Map And 28-Map Grid

This file is an implementation checklist derived from the revised multi-scene plan. Tasks are intentionally small and sequential so they can be completed, reviewed, and tested incrementally.

## Phase 1: Project Structure
- [x] Create an `Assets/Scenes/WorldMaps` folder for additive map scenes.
- [x] Decide whether `MainScene` will remain the shared persistent gameplay scene for v1.
- [x] Document `MainScene` as the owner of shared networking, HUD, water, lighting, and persistent managers.
- [x] Create a naming convention for map scenes using `Map_<mapId>.unity`.
- [x] Create empty scene files for all 28 maps from `Map_1-1.unity` through `Map_14-2.unity`.
- [x] Confirm all new map scenes are added to the project in a consistent location and naming format.
- [ ] Verify `MainScene` can be opened together with any map scene without missing-reference errors.

## Phase 2: World Map Data
- [x] Create a `WorldMapCatalog` `ScriptableObject` type.
- [x] Create a `WorldMapDefinition` data type used by `WorldMapCatalog`.
- [x] Add a `mapId` field to `WorldMapDefinition`.
- [x] Add a `row` field to `WorldMapDefinition`.
- [x] Add a `column` field to `WorldMapDefinition`.
- [x] Add a scene reference field to `WorldMapDefinition`.
- [x] Add an optional header label field to `WorldMapDefinition`.
- [x] Add an optional tile icon field to `WorldMapDefinition`.
- [x] Add an optional minimap texture override field to `WorldMapDefinition`.
- [x] Create a `WorldMapCatalog` asset for the 28-map grid.
- [x] Populate the catalog with 28 entries in screenshot order from `1-1` to `14-2`.
- [x] Confirm `1-1` is stored as the starting map in the catalog or manager defaults.
- [x] Implement adjacency lookup from `row` and `column`.
- [x] Remove any need for manually authored neighbor references in the catalog.

## Phase 3: Map Scene Authoring
- [x] Create a `WorldMapSceneAuthoring` `MonoBehaviour`.
- [x] Add a serialized `mapId` field to `WorldMapSceneAuthoring`.
- [x] Add playable bounds data to `WorldMapSceneAuthoring`.
- [x] Add serialized north edge travel zone data to `WorldMapSceneAuthoring`.
- [x] Add serialized east edge travel zone data to `WorldMapSceneAuthoring`.
- [x] Add serialized south edge travel zone data to `WorldMapSceneAuthoring`.
- [x] Add serialized west edge travel zone data to `WorldMapSceneAuthoring`.
- [x] Add a north arrival anchor reference to `WorldMapSceneAuthoring`.
- [x] Add an east arrival anchor reference to `WorldMapSceneAuthoring`.
- [x] Add a south arrival anchor reference to `WorldMapSceneAuthoring`.
- [x] Add a west arrival anchor reference to `WorldMapSceneAuthoring`.
- [x] Add a respawn anchor reference to `WorldMapSceneAuthoring`.
- [x] Add an optional minimap override reference to `WorldMapSceneAuthoring`.
- [x] Add local NPC spawner references to `WorldMapSceneAuthoring`.
- [x] Add local monster spawner references to `WorldMapSceneAuthoring`.
- [x] Add local reward-box spawner references to `WorldMapSceneAuthoring`.
- [x] Add gizmos for playable bounds to `WorldMapSceneAuthoring`.
- [x] Add gizmos for travel zones to `WorldMapSceneAuthoring`.
- [x] Add gizmos for arrival anchors to `WorldMapSceneAuthoring`.
- [x] Add a gizmo label that shows the map ID in the Scene view.
- [x] Place exactly one `WorldMapSceneAuthoring` root in each map scene.
- [x] Match each scene root `mapId` to its `WorldMapCatalog` entry.

## Phase 4: Base Map Content Setup
- [ ] Build one base map scene that can act as the initial template for all 28 maps.
- [ ] Add terrain and environment content to the base map scene.
- [ ] Add local props and decoration to the base map scene.
- [ ] Add local spawn points and spawners to the base map scene.
- [ ] Add travel anchors to the base map scene.
- [ ] Add a respawn anchor to the base map scene.
- [ ] Duplicate the base scene to the remaining 27 map scenes.
- [ ] Update each duplicated scene so its `WorldMapSceneAuthoring.mapId` matches the scene name.
- [ ] Confirm all 28 map scenes can be opened individually and still resolve their authoring root.

## Phase 5: Shared Runtime Manager
- [x] Create a `WorldMapManager` `MonoBehaviour` in `MainScene`.
- [x] Add a serialized `WorldMapCatalog` reference to `WorldMapManager`.
- [x] Add any required HUD controller references to `WorldMapManager`.
- [x] Add any required world-map overlay controller references to `WorldMapManager`.
- [x] Add a registration path so each loaded `WorldMapSceneAuthoring` can register itself with `WorldMapManager`.
- [x] Cache registered map-scene authoring roots in `WorldMapManager`.
- [x] Add a lookup from `mapId` to `WorldMapDefinition` in `WorldMapManager`.
- [x] Add a lookup from `mapId` to loaded `WorldMapSceneAuthoring` in `WorldMapManager`.
- [x] Add validation that only one loaded authoring root exists per `mapId`.

## Phase 6: Player Map State
- [x] Add `CurrentWorldMapId` as a networked field on `Player`.
- [x] Initialize `CurrentWorldMapId` to `1-1` for a fresh spawn.
- [x] Ensure `CurrentWorldMapId` is updated on map transition.
- [x] Ensure `CurrentWorldMapId` remains correct after death and respawn.
- [x] Expose a reliable way for local HUD systems to read the player's current map.

## Phase 7: Map Travel
- [x] Create a `MapTransitionDirection` enum with `North`, `East`, `South`, and `West`.
- [x] Add `RequestMapTransitionServerRpc(MapTransitionDirection direction)` to `Player`.
- [x] Add server validation that the player is alive before accepting a transition request.
- [x] Add server validation that the player's current map resolves to a loaded map scene.
- [x] Add server validation that the requested direction has a valid adjacent map.
- [x] Add server validation that the player is inside the correct serialized travel zone.
- [x] Resolve the destination map from the catalog adjacency lookup.
- [x] Resolve the destination arrival point from the opposite-side arrival anchor.
- [x] Preserve the player's normalized orthogonal edge position during travel where practical.
- [x] Add serialized clamp settings to avoid corner and overlap arrivals.
- [x] Apply the destination transform on the server after validation succeeds.
- [x] Update `CurrentWorldMapId` on successful travel.
- [x] Keep travel available during combat.
- [x] Block travel while dead.
- [x] Block travel while respawning.

## Phase 8: Respawn
- [x] Update respawn logic to use `CurrentWorldMapId`.
- [x] Resolve the respawn destination from the current map scene's respawn anchor.
- [x] Keep the old `PlayerSpawnPoint` path as a fallback only when map-scene authoring is invalid.
- [ ] Verify the fallback path is not used when the current map scene is configured correctly.

## Phase 9: Multiplayer Isolation
- [x] Update `FogOfWarNetworkVisibilityController` to require matching `CurrentWorldMapId` values.
- [x] Keep the existing reveal logic in addition to the same-map requirement.
- [x] Update minimap player marker collection to ignore players from other maps.
- [x] Update minimap NPC marker collection to ignore NPCs from other maps.
- [x] Update targeting queries to ignore entities from other maps.
- [x] Update attack validation to reject cross-map targets if any server-side combat checks are needed.
- [ ] Verify map membership is treated as a gameplay partition and not only as a distance rule.

## Phase 10: Scene Residency And Runtime Efficiency
- [x] Decide the initial v1 additive scene loading strategy that is compatible with NGO scene management.
- [x] Ensure map scenes are loaded through the project's NGO-supported scene flow.
- [x] Avoid per-frame scene scans for map lookups.
- [x] Keep map-scene references cached after registration.
- [x] Add occupancy tracking for players per map.
- [x] Pause or reduce transient encounter spawning when a map becomes empty.
- [x] Avoid mutating another map's authored content when a player changes maps.
- [x] Treat per-client map-scene streaming as a later optimization, not a v1 requirement.

## Phase 11: HUD Travel Prompt
- [x] Add a persistent HUD fragment for the travel prompt using the current UI Toolkit architecture.
- [x] Mount the travel prompt under `GameHUD`.
- [x] Position the travel prompt centered below the top bar.
- [x] Show the prompt only when the player is near a valid travel edge.
- [x] Choose the closest valid edge when the player is near a corner.
- [x] Format the prompt text as `Sail to <mapId>`.
- [x] Hide the prompt when the player is dead.
- [x] Hide the prompt when no adjacent map exists on that edge.
- [x] Hide the prompt when the player leaves the travel zone.
- [x] Trigger `RequestMapTransitionServerRpc` when the prompt button is pressed.

## Phase 12: World Map HUD Button
- [x] Add a new HUD button near the minimap for opening the world map.
- [x] Place the button to the left of the minimap by default.
- [x] Style the button as part of the minimap cluster.
- [x] Do not reuse `TopMenuCompassButton` for v1.
- [x] Wire the new button into the current `GameHUD` setup.

## Phase 13: World Map Overlay
- [x] Create a `WorldMapController` overlay using the current `MetaRoot` screen architecture.
- [x] Open the overlay from the new minimap-adjacent HUD button.
- [x] Build a parchment-style panel for the world map overlay.
- [x] Build a 4x7 tile grid that matches the screenshot layout.
- [x] Render tile IDs in screenshot order from the `WorldMapCatalog`.
- [x] Highlight the player's current map tile.
- [x] Support optional header text from the catalog.
- [x] Support optional tile icons from the catalog.
- [x] Keep the overlay informational only for v1.
- [x] Do not allow tile-click travel in v1.

## Phase 14: Editor Tooling
- [x] Add a catalog generation tool for the default 28-map grid.
- [x] Add validation for duplicate `mapId` values.
- [x] Add validation for duplicate `row` and `column` coordinates.
- [x] Add validation for missing map-scene references in the catalog.
- [x] Add validation for missing `WorldMapSceneAuthoring` roots in map scenes.
- [x] Add validation for duplicate `WorldMapSceneAuthoring` roots in a single map scene.
- [x] Add validation for mismatched `mapId` values between catalog entries and scene roots.
- [x] Add validation for missing travel anchors.
- [x] Add validation for missing respawn anchors.
- [x] Add a tool to open a selected map scene together with `MainScene`.
- [x] Add a tool to open a selected map scene together with adjacent map scenes.
- [x] Add a tool to validate the catalog and currently loaded map scenes.
- [x] Add a tool to ping the selected catalog entry or map scene authoring root.

## Phase 15: Editor Workflow Documentation
- [x] Document that designers should normally edit one map scene at a time together with `MainScene`.
- [x] Document that neighboring map scenes can be opened temporarily for transition alignment work.
- [x] Document that unloading map scenes is preferred over disabling hierarchy roots.
- [x] Document that Scene Visibility and Isolation are preferred when multiple scenes are open.
- [x] Document when terrain neighbor workflows should be used and when maps should stay visually independent.
- [x] Document the base-scene duplication workflow for creating early map variants.

## Phase 16: Verification
- [x] Verify the generated catalog contains all 28 maps with correct numbering and coordinates.
- [ ] Verify `MainScene` plus one map scene opens cleanly in the Editor.
- [ ] Verify `MainScene` plus adjacent map scenes is practical for transition alignment.
- [ ] Verify the map-scene authoring root registers correctly when its scene is loaded.
- [ ] Verify a fresh session starts the local player on `1-1`.
- [ ] Verify the world map opens from the minimap-adjacent HUD button.
- [ ] Verify the world map shows 28 tiles in a 4x7 grid.
- [ ] Verify `1-1` is highlighted at startup.
- [ ] Verify approaching the east edge of `1-1` shows `Sail to 1-2`.
- [ ] Verify approaching the north edge of `1-1` shows `Sail to 3-1`.
- [ ] Verify the south and west edges of `1-1` do not show a travel prompt.
- [ ] Verify successful travel updates `CurrentWorldMapId`.
- [ ] Verify successful travel lands the player at the adjacent map's authored arrival anchor.
- [ ] Verify death after switching maps respawns the player in the current map.
- [ ] Verify the travel prompt is hidden while dead.
- [ ] Verify two players in different maps cannot see each other.
- [ ] Verify two players in different maps do not appear on each other's minimap.
- [ ] Verify two players in different maps cannot target or attack each other.
- [ ] Verify one player's map switch does not mutate another map's spawners or map content.
- [ ] Verify empty maps do not keep full transient encounter populations active.
- [ ] Verify map switching does not repeatedly rebuild expensive scene references.
- [ ] Verify outer maps such as `14-2` do not expose prompts on edges with no neighbors.
