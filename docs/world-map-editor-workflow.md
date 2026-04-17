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
- Use `Tools > World Map > Map Editor` for normal editing. Pick a map and click `Open Selected Map For Editing`.
- The Map Editor keeps all catalog maps visible in a persistent board, with status for closed, loaded, and active map scenes.
- Use `Open All Map Scenes For Editing` when you want every authored map scene loaded in the Hierarchy at once.
- The editor tool opens the map scene additively, makes that map scene the active scene, selects `WorldMapSceneRoot`, and frames it in Scene View.
- Add or move map-specific terrain, props, spawners, spawn points, and encounter objects into the active `Map_<mapId>.unity` scene, preferably under `WorldMapSceneRoot` children such as `EnvironmentRoot`, `PropsRoot`, and `SpawnRoot`.
- Keep `MainScene` for shared networking, water, lighting, HUD, and persistent managers only. Do not author per-map gameplay content in `MainScene` unless it is temporary legacy `WorldMapContentScope` content.
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
- Use the `Populate Loaded Map Scenes From MainScene Template` button after opening `MainScene` plus one or more map scenes. It clones the scoped authored map content from `MainScene` into each loaded map scene through additive scene editing.
- The population pass replaces the old placeholder `EnvironmentRoot` / `PropsRoot` / `SpawnRoot` setup with the real template roots from `MainScene`, including the NPC spawner prefab instance, terrain hierarchy, reward/monster spawners, player spawn point, and marker objects.
- Each populated map gets its own duplicated `TerrainData` assets inside a scene-specific `Map_<id>_TerrainData` folder so terrain sculpting in one map does not bleed into the others.
- Treat the populated scene as a starting authored copy of the `MainScene` template. Designers can then edit the copied hierarchy directly inside each additive `Map_<mapId>.unity` scene.
