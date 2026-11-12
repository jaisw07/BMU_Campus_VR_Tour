# BMU Campus VR Tour

Unity VR walkthrough of BML Munjal University, built to run standalone on the **Meta Quest 3S** (Android, OpenXR).
- Unity **6000.3.27f2**, URP 17.3, XR Interaction Toolkit 3.0.7, XR Hands, OpenXR + Meta OpenXR, glTFast, TextMeshPro, uGUI.
- All project code is in `Assets/Scripts/` (namespace `BMU.VRTour`) and `Assets/Scripts/Editor/` (namespace `BMU.VRTour.Editor`). There are no asmdefs, so everything compiles into Assembly-CSharp.
- Longer docs: `Assets/Documentation/PROJECT_DEVELOPER_GUIDE.md` (architecture, Blender→GLB pipeline, anchor table) and `PLAN.md` (the original Figma-driven phase plan). Their `file:///C:/Users/SHREY/...` links point to the original author's Windows machine; read them as repo-relative paths.

## Scenes are generated, so edit the builder
`Editor/BMUTourSceneBuilder.cs` (about 2k lines) builds all four scenes from code: the XR rig, the whole spatial UI, materials, placeholder room pods, teleport anchors, and Build Settings.
- Menu: **BMU VR Tour → Build Complete VR Tour Scene (All 4 Scenes)**. Rebuilding wipes anything you placed by hand in a scene.
- To make a change permanent, put it in the builder or in a prefab that the builder instantiates. Don't hand-edit the `.unity` YAML. The scene files are large, noisy diffs that the builder regenerates.
- The builder wires serialized private fields with reflection: `SetPrivateField(target, "m_FieldName", value)`, with about 60 call sites. **If you rename a serialized field in a runtime script, update the matching string in the builder too.** Nothing catches a mismatch at compile time.
- `BMUTourSceneBuilder.cs.bak_phase2` is a stale backup. Ignore it.

Other editor menus:
- `CampusEnvironmentSetup.cs` → *Setup Campus Exterior Prefab*. It takes `Assets/Models/Exterior/Campus_Exterior.glb` (49 MB, committed directly with no LFS), scales it to 0.5x, sets it static, puts it on layer 0, and adds MeshColliders to roads, lawns and walls (none on foliage). The result is `Assets/Prefabs/Environment/Prefab_Exterior_Campus.prefab`.
- `XRSetupInitializer.cs`: configure OpenXR, fix project validation issues, verify the XR config.
- `BMUPhase1/2AssetProcessor.cs`: import settings for the logo, icons and 9-slice glass UI sprites.

## Runtime architecture (additive multi-scene)
Build order: `[0] Master_Tour_Core` (always loaded: XR rig, spatial dashboard, ScreenFade, managers) → `[1] Scene_Campus_Exterior` → `[2] Scene_NorthBlock_Interiors` → `[3] Scene_Gateway_Interiors`. `SampleScene.unity` is unused.

The four singletons (`static Instance`) are:
- `CampusTourManager`: the tour state machine and all tour content. Slides, locations and rooms are hardcoded in `InitializeTourData()`. It also drives the carousel, hub cards, info panels and thumbstick input.
- `SceneTransitionManager`: fade → load/unload sub-scenes additively → move the rig to an anchor → fade back in. Holds the scene name constants.
- `TourAudioNarrator`: voiceover, typewriter captions, waveform bars, mute.
- `CampusEnvironmentManager`: photosphere/sky per zone (left over from the 360° plan, which has since moved to the real 3D GLB).

UI and comfort scripts: `LazyFollowUI`, `CurvedUILayout`, `MinimizedDockOrb`, `VRUIDraggableHandle`, `ScreenFade`, `VRButtonFeedback`, `VRPointerReticle`, `VRHeaderTooltip`. Safety and room scripts: `OutOfBoundsSafetyTrigger`, `RoomPodAnchorController` (exposes `PodKey` and `PrimarySpawnAnchor`).

### String keys hold the system together (keep these in sync)
| Location key | Resolves to |
|---|---|
| `front-gate` | `Anchor_FrontGate` (exterior) |
| `outside-north-block` | `Anchor_NorthBlock_Plaza` |
| `outside-gateway` | `Anchor_Gateway_Plaza` |
| `outside-workshop` | `Anchor_Workshop_Plaza` (no interiors) |
| `nb-classroom` / `nb-lab` / `nb-faculty` | `RoomPod_SmartClassroom` / `RoomPod_RoboticsLab` / `RoomPod_FacultyCabins` |
| `gw-classroom` / `gw-lab` | `RoomPod_ExecutiveMBASeminar` / `RoomPod_ACICStartupIncubator` |

- Building keys: `north-block`, `gateway`, `workshop`.
- Room keys get their building from the prefix: `nb-` or `gw-`.
- Lookups use `GameObject.Find` by name (`SceneTransitionManager.FindExteriorAnchor` and `FindRoomAnchor`), or match `RoomPodAnchorController.PodKey`. So renaming a GameObject or a key silently breaks teleporting.
- The pod names in the developer guide (e.g. `RoomPod_AdvancedRoboticsLab`) are out of date. The names in the code above are the real ones.

## Conventions and constraints
- Teleport anchors and pads go on **layer 31 (Teleport)**, which is the only layer teleport rays hit. All environment geometry stays on layer 0. Teleporting only to curated anchors is intentional.
- The exterior is at **0.5x scale**. Anchor coordinates are listed in §7 of the developer guide. The out-of-bounds floor sits at y = -5.
- Quest budget: fewer than 150 draw calls, 72 FPS minimum (90 is the target). Merge repeated meshes, use static batching, use MeshColliders only where they're needed.
- GLBs must not use WebP textures, because glTFast rejects `EXT_texture_webp`. Export PNG or JPEG.
- Brand colors: Navy `#002B49`, BMU Blue `#004B87`, Accent `#0066B3`, Red `#E31B23`.
- Spatial UI style (all set in the builder):
  - Frosted-glass sprites (`Assets/Textures/UI/ui_glass_*`) tinted by `GlassCard`/`GlassOrnament`/`GlassInner`. There's no full-screen backdrop and no full-width header or footer bars.
  - Brand/location, window controls and narration are small "ornaments" floating at `OrnamentDepth` in front of the stage card.
  - Dark text automatically gets a soft white underlay halo (`GetHaloMaterial`, stored in `Assets/Materials/UI/`).
  - Text blocks sit in `CreateTextStack` vertical layouts and pills auto-fit their labels (`MakePillFitText`). Use these rather than fixed y-offsets, which caused overlapping text.
  - Card rows curve around the viewer via `UICardArc`.
  - When minimized, the dock orb sits top-right of the view.
- `Assets/Samples/`, `Assets/TextMesh Pro/` and `Assets/TutorialInfo/` are vendor or template content. Don't edit them.

## Workflow notes
- Development happens on Linux here, while the original authoring was on Windows. The local, uncommitted `Packages/manifest.json` change adds `com.unity.toolchain.linux-x86_64-linux`. Ask before committing it.
- There are no tests yet (the test-framework package is installed but unused). Verify in the Editor with the XR Device Simulator, or with a Quest build (Android, ASTC).
- A Unity MCP skill (`unity-mcp-orchestrator`) is available for driving the Editor when it's connected.
