# BMU VR Campus Tour: Complete Architecture & Developer Guide

Welcome to the **BML Munjal University (BMU) VR Campus Tour** project! This document is designed as a one-stop, comprehensive, plain-English reference for developers, 3D artists, and newcomers. It explains how the project is structured, why decisions were made, how all scripts function, and how to import 3D models from Blender into Unity to replace placeholders without breaking existing features.

---

## Table of Contents
1. [Project Overview & Core Constraints](#1-project-overview--core-constraints)
2. [Multi-Scene Additive Architecture](#2-multi-scene-additive-architecture)
3. [The Power of Prefabs: Why and How We Use Them](#3-the-power-of-prefabs-why-and-how-we-use-them)
4. [Tour Mechanics & Teleportation Philosophy](#4-tour-mechanics--teleportation-philosophy)
5. [Complete Scripts Breakdown (Plain English)](#5-complete-scripts-breakdown-plain-english)
   - [Core Tour & Navigation](#core-tour--navigation)
   - [VR Spatial UI & Comfort](#vr-spatial-ui--comfort)
   - [Editor Automation & Scene Generation](#editor-automation--scene-generation)
6. [The Blender-to-Unity Pipeline: Step-by-Step Guide](#6-the-blender-to-unity-pipeline-step-by-step-guide)
   - [The Foliage / Duplicate Clone Trap](#the-foliage--duplicate-clone-trap)
   - [The WebP / Texture Trap](#the-webp--texture-trap)
   - [Exporting from Blender to .GLB](#exporting-from-blender-to-glb)
   - [Importing into Unity via glTFast](#importing-into-unity-via-gltfast)
   - [Replacing Placeholders with Final Models](#replacing-placeholders-with-final-models)
7. [Current Campus Calibration Reference (0.5x Scale)](#7-current-campus-calibration-reference-05x-scale)
8. [Step-by-Step Guide: Adding a New Interior Room Pod](#8-step-by-step-guide-adding-a-new-interior-room-pod)
9. [Developer FAQ & Best Practices](#9-developer-faq--best-practices)

---

## 1. Project Overview & Core Constraints

### What is this project?
The BMU VR Campus Tour is an immersive virtual reality walkthrough of BML Munjal University designed for prospective students, parents, and visitors. Users can explore the entire outer campus grounds and teleport into detailed interior spaces (classrooms, auditoriums, incubation centers, and labs) while listening to guided voice narrations and viewing photos on an interactive spatial UI dashboard.

### Target Hardware: Meta Quest 3S
The application runs standalone on the **Meta Quest 3S** (Android/OpenXR mobile chipset) without requiring a tethered PC. Because mobile VR chips have strict thermal and rendering budgets, every architectural decision follows these performance rules:
- **Draw Call Budget**: Kept below **150 draw calls per frame**.
- **Frame Rate**: Minimum steady **72 FPS** (target 90 FPS) to prevent VR motion sickness.
- **Layer Isolation**: Curated navigation pads are isolated on **Layer 31 (Teleport)**; all general environment models (roads, lawns, buildings) reside on **Layer 0 (Default)**.
- **Physics Efficiency**: Only non-convex static `MeshCollider` components are used for ground/walls; visual foliage has colliders removed completely.

---

## 2. Multi-Scene Additive Architecture

Instead of packing the entire 950-meter campus and all building interiors into one giant Unity scene, the project uses a **Multi-Scene Additive Architecture**.

```text
Build Settings Order:
[0] Master_Tour_Core               <-- Always loaded! Never unloads.
    ├── XR Origin (VR Rig, Head, Hands, Controllers)
    ├── Canvas_Spatial_Dashboard (Interactive UI, Audio Narrator)
    ├── ScreenFade_Overlay (Comfort blackout system)
    └── Core Managers (SceneTransitionManager, CampusTourManager)

[1] Scene_Campus_Exterior          <-- Loaded additively during exterior tour
    ├── Campus_Exterior_Environment (Prefab_Exterior_Campus)
    ├── Exterior_Navigation_Anchors (Curated teleport points)
    ├── Sun Directional Light & Reflection Probe
    └── OutOfBounds_ResetFloor (Safety catch net)

[2] Scene_NorthBlock_Interiors     <-- Loaded additively when visiting North Block
    ├── RoomPod_SmartClassroom
    ├── RoomPod_AdvancedRoboticsLab
    └── RoomPod_ExecutiveFacultyLounge

[3] Scene_Gateway_Interiors        <-- Loaded additively when visiting The Gateway
    ├── RoomPod_IncubationHub
    └── RoomPod_ExecutiveAuditorium
```

### Why do this?
1. **Zero Hitching & Smooth Transitions**: The player's headset (`XR Origin`), camera, controllers, and UI exist in `Master_Tour_Core`. When moving from the outdoor gate into a classroom, the player rig never gets destroyed and recreated.
2. **Instant Memory Reclamation**: The mobile VR headset cannot hold high-resolution interior room models in memory while simultaneously rendering the 950-meter outdoor campus. Using `SceneManager.LoadSceneAsync(..., LoadSceneMode.Additive)` and `SceneManager.UnloadSceneAsync(...)`, unused geometry and textures are purged from mobile VRAM immediately.
3. **Safe Fallback**: If an interior sub-scene takes an extra half-second to load, the screen remains comfortably faded to black in `Master_Tour_Core`, eliminating immersion-breaking geometry pops.

---

## 3. The Power of Prefabs: Why and How We Use Them

A **Prefab** in Unity is a saved, reusable game asset template that contains GameObjects, child hierarchies, components, colliders, and settings.

### Why we strictly use Prefabs for environments:
1. **Immunity to Code Rebuilds**: We maintain an automated 1-click scene builder (`BMUTourSceneBuilder.cs`). If 3D models were placed raw inside the scene hierarchy, clicking "Rebuild Scenes" would wipe them out. By saving the campus environment as [`Prefab_Exterior_Campus.prefab`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Prefabs/Environment/Prefab_Exterior_Campus.prefab), the builder script simply instantiates the prefab reference.
2. **Automated Collider & Optimization Baking**: Running our custom menu item `BMU VR Tour -> Setup Campus Exterior Prefab` automatically processes thousands of sub-meshes, assigns `MeshCollider` components to roads and paths, removes colliders from leaves, sets GPU static batching flags, scales everything by 0.5x, and saves it into the prefab.
3. **Effortless Asset Updating**: When a 3D artist exports an updated building model, updating the prefab automatically updates every scene where that building appears without touching camera rigs, UI, or navigation scripts.

---

## 4. Tour Mechanics & Teleportation Philosophy

A common mistake in VR is allowing free, unconstrained teleportation anywhere on the floor. In a massive campus model, this leads to:
- Users getting lost behind dumpsters or inside walls.
- Extreme raycast calculation overhead across millions of ground polygons.
- Vertigo and disorienting camera angles.

### Our Curated Anchor System:
- **Layer 31 (`Teleport`)**: Teleport rays emitted from VR controllers are configured to hit **only** objects on Layer 31.
- **Physical Environment is on Layer 0 (`Default`)**: You can walk around physically inside your guardian room scale, and the floor will catch you with its `MeshCollider`, but the teleport pointer will not snap to random road patches.
- **Curated Vantage Pads**: Users navigate by pointing at glowing, circular visual teleport pads placed at the front entrances of landmark buildings. Each pad has a preset rotation, ensuring that when the user lands, they are looking directly at the building facade with optimal architectural framing.

---

## 5. Complete Scripts Breakdown (Plain English)

All code is cleanly organized under `Assets/Scripts/` (runtime behavior) and `Assets/Scripts/Editor/` (editor-only tools and automation).

### Core Tour & Navigation

#### 1. [`CampusTourManager.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/CampusTourManager.cs)
- **Role**: The "Brain" of the tour experience.
- **What it does**: Holds all the data for campus landmarks (names, descriptions, student capacity, historical facts, audio voiceover scripts, and slide photos). It drives the state machine that knows whether the user is at the Front Gate, North Block, Gateway, or inside an interior pod. It also drives the 3D carousel UI and responds to next/previous slide controls.

#### 2. [`SceneTransitionManager.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/SceneTransitionManager.cs)
- **Role**: The "Conductor" for switching scenes and teleports.
- **What it does**: Handles the sequence for moving between areas:
  1. Triggers `ScreenFade` to smoothly blackout the player's view (preventing motion sickness).
  2. Unloads the old sub-scene (e.g., North Block Interiors) and loads the new sub-scene (e.g., Campus Exterior) additively in the background.
  3. Repositions the `XR Origin` player rig to the target destination anchor with the correct heading.
  4. Smoothly fades the view back in once the environment is loaded and ready.

#### 3. [`RoomPodAnchorController.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/RoomPodAnchorController.cs)
- **Role**: Interior room coordinator.
- **What it does**: Attached to the root of every interior room (e.g., `RoomPod_SmartClassroom`). It holds references to the primary spawn point, alternate viewing angles inside the room, and the recovery anchor if the user falls.

#### 4. [`OutOfBoundsSafetyTrigger.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/OutOfBoundsSafetyTrigger.cs)
- **Role**: The invisible safety net.
- **What it does**: Attached to a massive invisible trigger collider placed a few meters beneath the floor (`Y = -5m`). If a tracking glitch or physics seam causes the user to fall through the world, this script catches them, triggers a comfort blackout, and teleports them back to the active anchor immediately.

#### 5. [`CampusEnvironmentManager.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/CampusEnvironmentManager.cs)
- **Role**: Photosphere and panoramic sky backdrop controller.
- **What it does**: Manages 360° spherical horizon backdrops and sky gradient transitions for different times of day or tour zones.

---

### VR Spatial UI & Comfort

#### 6. [`ScreenFade.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/ScreenFade.cs)
- **Role**: VR comfort blackout.
- **What it does**: Renders a full-screen fade to black during camera hops, scene loading, and boundary resets to eliminate motion sickness.

#### 7. [`LazyFollowUI.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/LazyFollowUI.cs)
- **Role**: Smart HUD positioning.
- **What it does**: Keeps the floating dashboard within comfortable reach. If the user turns their head slightly, the UI stays still. If the user turns around completely, the UI smoothly floats and glides to face them, ensuring the menu never gets lost behind the player's back.

#### 8. [`CurvedUILayout.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/CurvedUILayout.cs)
- **Role**: Ergonomic curved UI panels.
- **What it does**: Arranges UI cards along a subtle radial cylinder centered on the user. This keeps edge text the same optical distance from the headset lenses, preventing distortion and eye strain.

#### 9. [`MinimizedDockOrb.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/MinimizedDockOrb.cs)
- **Role**: Floating minimize button.
- **What it does**: When the user wants to admire the campus architecture without a giant dashboard blocking their view, they click "Minimize". The large UI hides, leaving a subtle floating 3D orb. Pointing and clicking the orb restores the dashboard instantly.

#### 10. [`VRUIDraggableHandle.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/VRUIDraggableHandle.cs)
- **Role**: Hand positioning handle.
- **What it does**: Lets users point their ray at the dashboard header and freely drag, raise, lower, or reposition the menu anywhere in their 3D workspace.

#### 11. [`TourAudioNarrator.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/TourAudioNarrator.cs)
- **Role**: Voiceover, subtitles & animated equalizer.
- **What it does**: Plays voiceover clips for each landmark, renders synchronized typewriter captions/subtitles on the UI, provides a mute toggle, and drives an animated procedural waveform equalizer bar.

#### 12. [`VRButtonFeedback.cs`], [`VRPointerReticle.cs`], [`VRHeaderTooltip.cs`]
- **Role**: Micro-interactions & tactile polish.
- **What they do**: Provide visual button pops, hover glows, controller vibration (haptics), animated cursor rings, and tooltip hints when aiming at interactive elements.

---

### Editor Automation & Scene Generation

#### 13. [`BMUTourSceneBuilder.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/Editor/BMUTourSceneBuilder.cs)
- **Role**: 1-Click Master Generator (`BMU VR Tour -> Build Complete VR Tour Scene`).
- **What it does**: The entire multi-scene setup can be created or regenerated from scratch with a single click. It configures `Master_Tour_Core`, `Scene_Campus_Exterior`, `Scene_NorthBlock_Interiors`, and `Scene_Gateway_Interiors`, positions all navigation pads, instantiates environment prefabs, sets up lights and reflection probes, and updates the Unity Build Settings.

#### 14. [`CampusEnvironmentSetup.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/Editor/CampusEnvironmentSetup.cs)
- **Role**: Automated Prefab Creator (`BMU VR Tour -> Setup Campus Exterior Prefab`).
- **What it does**: Reads the raw `Campus_Exterior.glb` file, sets the scale to `(0.5, 0.5, 0.5)`, marks all meshes with GPU static batching flags, sets layers to 0, automatically detects road/lawn/wall meshes to add non-convex `MeshCollider` components (while omitting foliage), and saves the ready-to-use prefab.

#### 15. [`XRSetupInitializer.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/Editor/XRSetupInitializer.cs)
- **Role**: Automatic VR configuration validator.
- **What it does**: Automatically checks and configures OpenXR, Meta Quest features, XR Interaction Toolkit settings, hand visualizer assets, and fixes project validation issues on startup.

#### 16. [`BMUPhase1AssetProcessor.cs`] & [`BMUPhase2AssetProcessor.cs`]
- **Role**: Texture and UI Asset Importers.
- **What they do**: Automatically configure BMU logos, vector icons, and glassmorphism panel slices with 9-slice borders and alpha transparency settings.

---

## 6. The Blender-to-Unity Pipeline: Step-by-Step Guide

Follow this guide whenever you or an 3D artist want to update the campus model or introduce new interior buildings.

### The Foliage / Duplicate Clone Trap
> [!WARNING]
> **Why did the original campus FBX explode into 8 million vertices and thousands of draw calls?**
> In Blender, artists often use linked duplicates (`Alt+D`) to populate thousands of trees, lights, or grass blades (`GrassBlade.001` through `GrassBlade.3930`). In Blender viewport, this looks lightweight because mesh data is shared. However:
> - Standard exporters (FBX/GLB) create an individual object transform node for every single clone.
> - In Unity, 3,930 individual objects result in **3,930 separate CPU draw calls**, instantly reducing the Meta Quest 3S frame rate to a slideshow.

**The Fix in Blender**:
1. Select all repetitive objects (e.g., all 3,930 grass blade clones).
2. Press **`Ctrl + J`** (Join) to merge them into **1 single combined mesh** (named e.g. `GrassBlade_Combined`).
3. This bakes the positions into one vertex buffer, cutting draw calls from 3,930 to **exactly 1** while preserving 100% of the visual detail.

---

### The WebP / Texture Trap
> [!CAUTION]
> **Unity glTFast rejects WebP images by default.**
> Blender's glTF exporter can pack textures as WebP if the source image had a `.webp` extension, which adds `EXT_texture_webp` to the file header. Unity's standard glTF importer fails with an `Unsupported Extension` error.

**The Fix in Blender**:
- Ensure all textures in Blender's Image Editor are saved as standard **PNG** or **JPEG** before exporting.

---

### Exporting from Blender to .GLB
Always export environments as **glTF 2.0 Binary (`.glb`)** rather than FBX. GLB embeds all textures directly into one self-contained file and avoids coordinate axis flip bugs.

1. In Blender, select all meshes to export.
2. Go to **File -> Export -> glTF 2.0 (.glb/.gltf)**.
3. Configure the export settings:
   - **Format**: `glTF Binary (.glb)`
   - **Include**: Check `Selected Objects` (if exporting specific buildings) or leave unchecked for entire scene.
   - **Transform**: Check `+Y Up` (Standard Unity conversion).
   - **Geometry**:
     - Check `Apply Modifiers`.
     - Check `UVs`, `Normals`, `Tangents`.
   - **Materials**: Set to `Export` with `Images: Automatic` (or `JPEG`).
4. Save the file to:
   ```text
   BMU_Campus_VR_Tour/Assets/Models/Exterior/Campus_Exterior.glb
   ```

---

### Importing into Unity via glTFast
1. Open the Unity project.
2. Unity with `com.unity.cloud.gltfast` installed will detect the `.glb` and import it natively.
3. In the Unity top menu bar, click:
   ```text
   BMU VR Tour -> Setup Campus Exterior Prefab
   ```
4. This script automatically:
   - Scales the model to $0.5\times$ to match realistic VR proportions.
   - Configures Layer 0 (`Default`) on all child objects.
   - Attaches non-convex `MeshCollider` components to roads, walkways, lawns, and building facades.
   - Omits colliders from grass/foliage.
   - Bakes all Static Batching flags for VR performance.
   - Saves the result to [`Assets/Prefabs/Environment/Prefab_Exterior_Campus.prefab`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Prefabs/Environment/Prefab_Exterior_Campus.prefab).

---

### Replacing Placeholders with Final Models
When final models are delivered for interior rooms or building facades:
1. Export the room model from Blender as a `.glb` into `Assets/Models/Interiors/`.
2. Create a Prefab for the room pod (e.g. `Prefab_Room_SmartClassroom.prefab`).
3. Attach a [`RoomPodAnchorController.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/RoomPodAnchorController.cs) component to the root.
4. Add child transforms for `PrimarySpawnAnchor` and `AlternateAnchors` on Layer 31.
5. In [`BMUTourSceneBuilder.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/Editor/BMUTourSceneBuilder.cs), update the corresponding `Build...Scene()` method to instantiate your new prefab instead of the placeholder cube/walls.
6. Run `BMU VR Tour -> Build Complete VR Tour Scene` to regenerate the clean scenes.

---

## 7. Current Campus Calibration Reference (0.5x Scale)

Because Blender units were scaled $2\times$ relative to real-world meters, the exterior environment prefab is scaled to `Vector3(0.5, 0.5, 0.5)`. The teleportation anchors have been calibrated to place the user comfortably on walkways in front of main buildings:

| Landmark Anchor | Scene Hierarchy Name | Position $(X, Y, Z)$ | Rotation $(Y)$ | Description |
| :--- | :--- | :--- | :--- | :--- |
| **Main Campus Gate** | `Anchor_FrontGate` | `(14.0, 0.05, 0.0)` | $-90^\circ$ | Positioned back on the access road; faces west through the iconic BMU entrance arches into the campus. |
| **The Gateway** | `Anchor_Gateway_Plaza` | `(-116.5, 0.05, -87.33)` | $-90^\circ$ | Positioned cleanly on the paved plaza right outside the Gateway glass facade (clear of dirt patches). |
| **North Block** | `Anchor_NorthBlock_Plaza` | `(-480.23, 0.05, -127.5)` | $0^\circ$ | Centered on the `NBFrontPath` walkway facing north into the primary Academic Complex facade. |
| **Mechanical Workshop** | `Anchor_Workshop_Plaza` | `(-411.63, 0.05, -275.0)` | $180^\circ$ | Centered on the `WorkshopFront` walkway facing south directly toward the Workshop entrance. |

- **Out of Bounds Floor**: Centered at `(-250, -5, -175)` with size `1500m x 1500m` to cover the entire halved campus footprint.
- **Reflection Probe**: Centered at `(-250, 50, -175)` with size `800m x 200m x 800m`.

---

## 8. Step-by-Step Guide: Adding a New Interior Room Pod

To add a new interior room (e.g., "Advanced Chemistry Lab"):

1. **Model the Room in Blender**:
   - Model the interior floor, walls, ceiling, and furniture at $1.0\times$ real-world scale (1 Blender unit = 1 meter).
   - Ensure origin `(0, 0, 0)` is at the center of the room floor.
   - Join static props where possible to keep draw calls low.
   - Export as `.glb` to `Assets/Models/Interiors/ChemistryLab.glb`.

2. **Create the Prefab in Unity**:
   - Instantiate the model in a staging scene.
   - Set all visual objects to Layer 0 (`Default`) and mark as Static.
   - Add non-convex `MeshCollider` components or simple `BoxCollider` shapes to the floor and walls.
   - Create an empty GameObject named `Anchor_PrimarySpawn` at the room entrance. Set its layer to **31 (`Teleport`)**.
   - Create child visual pad (blue ring) or use our standard teleport pad prefab.
   - Create an empty GameObject underneath the floor at `Y = -2.0m` with a `BoxCollider` (`isTrigger = true`) and add the [`OutOfBoundsSafetyTrigger.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/OutOfBoundsSafetyTrigger.cs) component.
   - Add [`RoomPodAnchorController.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/RoomPodAnchorController.cs) to the room root and wire the anchors.
   - Save the GameObject as `Assets/Prefabs/Rooms/RoomPod_ChemistryLab.prefab`.

3. **Register in Tour Data**:
   - In [`CampusTourManager.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/CampusTourManager.cs), add a new `RoomData` entry with a unique key (e.g., `"nb-chemlab"`), title, description, and photo references.

4. **Add to Scene Builder**:
   - In [`BMUTourSceneBuilder.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/Editor/BMUTourSceneBuilder.cs), instantiate the new pod inside `BuildNorthBlockInteriorsScene()`.
   - Run `BMU VR Tour -> Build Complete VR Tour Scene`.

---

## 9. Developer FAQ & Best Practices

### Q: Why do my controllers pass through the floor or fail to teleport?
**A**: Ensure your teleport ray is set to hit **Layer 31 (`Teleport`)** and that you are aiming at a curated teleport anchor pad. The general environment is on Layer 0 (`Default`) and is intentionally non-teleportable to prevent users from teleporting onto building roofs or outside the campus fence.

### Q: Why do my materials look dark or metallic?
**A**: glTF PBR materials use metallic-roughness workflows. If a material looks overly reflective or black, verify that:
1. The directional light in `Scene_Campus_Exterior` is active (Intensity ~ 1.2, Color slightly warm sun).
2. The `Campus_Exterior_ReflectionProbe` has baked or updated realtime reflections.
3. The URP Asset in `Project Settings -> Graphics` has HDR and proper lighting settings enabled.

### Q: How do I test the tour in the Unity Editor without putting on the VR headset?
**A**: With the Unity XR Interaction Toolkit, you can use the XR Device Simulator (`Package Manager -> XR Interaction Toolkit -> Samples -> XR Device Simulator`). This allows moving with WASD and using the mouse/right-click to aim controller teleport rays in the Game view.

### Q: How do I make a standalone Meta Quest build?
1. Open **File -> Build Settings...**.
2. Select **Android** and click **Switch Platform** (Texture Compression: ASTC).
3. Ensure all 4 scenes are checked in the build list in order:
   - `0: Assets/Scenes/Master_Tour_Core.unity`
   - `1: Assets/Scenes/Scene_Campus_Exterior.unity`
   - `2: Assets/Scenes/Scene_NorthBlock_Interiors.unity`
   - `3: Assets/Scenes/Scene_Gateway_Interiors.unity`
4. Connect the Meta Quest via USB-C (ensure Developer Mode is enabled).
5. Click **Build and Run**.
