# World Map Editor Workflow

## Scene Model
- `Assets/Scenes/MainScene.unity` remains the shared persistent gameplay scene for v1.
- `MainScene` owns shared networking, the runtime HUD, water, lighting, and persistent managers.
- Each authored gameplay map should live in its own additive scene using the naming convention `Map_<mapId>.unity`.
- Keep all additive world-map scenes under `Assets/Scenes/WorldMaps`.
- The generated starter scenes are laid out on a 512-unit world grid so adjacent maps can coexist without overlapping while still lining up edge-to-edge.

## Runtime Strategy
- The v1 runtime should treat `MainScene` as the shared multiplayer scene and the map scenes as additive authored content.
- Keep the first pass simple: stabilize map membership, travel, respawn, and UI before attempting per-client map-scene streaming.
- When runtime scene loading is active in a multiplayer session, route it through the project's NGO-supported scene flow rather than local-only scene loads.
- `WorldMapManager` now configures NGO additive client synchronization and loads the catalog scenes additively from the server or host. The local `SceneManager` fallback remains only for non-network preview or recovery cases.

## Day-To-Day Editing
- Open `MainScene` together with one map scene for normal editing.
- Open neighboring map scenes only when you need to align travel edges, arrival anchors, or shoreline silhouettes across a boundary.
- Prefer unloading other map scenes over disabling hierarchy roots. This keeps scene state closer to runtime behavior and avoids accidental prefab or lighting drift.
- If multiple scenes are open, use Unity Scene Visibility and Isolation tools to focus on the map you are actively editing.

## Terrain And Layout
- Use Unity terrain neighbor workflows only when adjacent maps are meant to feel physically continuous across the seam.
- If two maps should read as separate spaces, keep their terrain and horizon treatment visually independent instead of forcing stitched terrain edges.
- Travel anchors, respawn anchors, and map-local spawners should stay inside the map scene they belong to.

## Early Content Production
- Build one base map scene first, then duplicate it to create the initial 28-map set.
- After duplicating a scene, immediately update the `WorldMapSceneAuthoring.mapId` value so it matches the scene name.
- Validate the catalog and the loaded scenes after each new batch of duplicated map scenes.
- Use the `Populate Loaded Map Scenes With Starter Content` button on the `WorldMapCatalog` inspector after opening `MainScene` plus one or more map scenes. It stamps in starter environment meshes, local spawners, authored NPC spawn points, and fallback player spawn anchors without hand-editing each scene.
- Treat the stamped starter content as a baseline, not final art. Designers can replace or delete the generated cubes, props, and spawn-center helpers once a map gets bespoke terrain and dressing.
