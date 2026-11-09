# GLB Environment Integration Guide: BMU VR Campus Tour

**Target Platform**: Meta Quest 3S (OpenXR / Android Standalone)  
**Engine & Pipeline**: Unity 6000.3.27f2 (Universal Render Pipeline - URP)  
**File Format**: `.glb` (glTF 2.0 Binary)  
**Primary Project Directory**: [`BMU_Campus_VR_Tour`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/)  

---

## 1. Overview & Architecture Summary

The BMU VR Campus Tour utilizes an **additive multi-scene architecture** divided across four distinct scenes:
1. [`Master_Tour_Core.unity`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scenes/Master_Tour_Core.unity): Persistent core scene hosting the `XR Origin`, HUD spatial UI, audio narrator, and scene transition controllers. **Do not place environment meshes here.**
2. [`Scene_Campus_Exterior.unity`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scenes/Scene_Campus_Exterior.unity): Loaded additively for exterior exploration (Gate, North Block, Gateway, Workshop).
3. [`Scene_NorthBlock_Interiors.unity`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scenes/Scene_NorthBlock_Interiors.unity): Contains 3 interior room pods spaced $60\text{m}$ apart.
4. [`Scene_Gateway_Interiors.unity`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scenes/Scene_Gateway_Interiors.unity): Contains 2 interior room pods spaced $60\text{m}$ apart.

### Why `.glb` Over `.fbx`?
- **Zero Coordinate Discrepancies**: Blender's `.glb` exporter strictly complies with the glTF standard ($+Y$ Up, $1\text{ unit} = 1\text{ meter}$). It completely eliminates the $-90^\circ$ $X$-axis rotation bug and scale multiplier issues common in `.fbx`.
- **Self-Contained PBR**: Textures (Base Color, Normal, Metallic-Roughness) are packed directly into the binary container and auto-map to URP Lit shaders upon import.

---

## 2. Project Prerequisite: `glTFast` Package

To enable native, high-performance `.glb` import in Unity 6, the project requires **Unity glTFast**.

### Installation
1. Open the project in Unity Editor.
2. Go to **Window $\rightarrow$ Package Manager**.
3. Click the **`+`** icon in the top-left corner and select **Add package by name...**
4. Enter:
   ```text
   com.unity.cloud.gltfast
   ```
5. Click **Add**.

*(Alternatively, add `"com.unity.cloud.gltfast": "6.10.1"` directly to `BMU_Campus_VR_Tour/Packages/manifest.json` under `"dependencies"` while the editor is closed).*

---

## 3. Blender Modeling & Export Standards

### A. Units and Transformations
- **Unit System**: Metric
- **Unit Scale**: `1.0` (Length: Meters)
- **Scale Application**: Before exporting, select all objects in Object Mode and press:
  $$\text{Ctrl} + \text{A} \longrightarrow \text{Apply All Transforms}$$
- **Normals**: In Edit Mode, press `Shift + N` to recalculate outside normals and avoid backface culling in VR.

### B. World Origins & Pivot Rules
| Asset Type | Pivot / Origin Placement in Blender | Placement in Unity |
| :--- | :--- | :--- |
| **Full Exterior Campus** | Origin $(0, 0, 0)$ at center of **Front Gate Entrance Plaza** | Position at $(0, 0, 0)$ in `Scene_Campus_Exterior` |
| **Individual Buildings** | Origin $(0, 0, 0)$ at building ground-floor entrance or facade center | Position inside `Architecture_Exteriors` matching facade anchors |
| **Interior Rooms (Pods)** | Origin $(0, 0, 0)$ at **center of room floor** | Parent under `RoomPod_...` at **local** $(0, 0, 0)$ |

> [!IMPORTANT]
> **Do not offset interior rooms by $+60\text{m}$ in Blender.** Model every room around $(0, 0, 0)$. In Unity, each room is parented to a `RoomPod` GameObject that already provides the world-space offset.

### C. Secondary UV Map for Lightmapping
For standalone Quest 3S performance, static lighting should be baked:
1. In Blender, select your environment mesh $\rightarrow$ **Object Data Properties (Green Triangle) $\rightarrow$ UV Maps**.
2. Click **`+`** to create a second UV channel (name it `UVMap2` or `Lightmap`).
3. In Edit Mode, select all faces $\rightarrow$ `U` $\rightarrow$ **Lightmap Pack** (Margin: `0.02`) or **Smart UV Project**.

### D. Export Settings Preset (`.glb`)
Go to **File $\rightarrow$ Export $\rightarrow$ glTF 2.0 (.glb/.gltf)**:
- **Format**: `glTF Binary (.glb)`
- **Include**:
  - [x] `Selected Objects` (or `Visible Objects`)
  - Object Types: `Mesh` only (uncheck Camera, Light)
- **Transform**:
  - [x] `+Y Up`
- **Geometry**:
  - [x] `Apply Modifiers`
  - [x] `UVs`
  - [x] `Normals`
  - [ ] `Tangents` (optional, keep off unless custom anisotropic shaders are needed)
- **Animation**:
  - [ ] Uncheck `Animation` and `Shape Keys` (unless the model has animated doors/fans)

---

## 4. Hierarchy & Coordinate Contracts

To prevent breaking navigation scripts, teleports, and out-of-bounds safety systems, keep these scene contracts intact:

### A. Exterior Scene Hierarchy (`Scene_Campus_Exterior.unity`)
```text
Scene_Campus_Exterior
├── Exterior_Directional_Light
├── Campus_Terrain_And_Roads           <-- Replace with Blender terrain (Layer 0, MeshCollider)
├── Architecture_Exteriors            <-- Place Blender buildings here
│   ├── FrontGate_Monument            <-- (35m forward from center: 0, 0, 18)
│   ├── NorthBlock_Exterior_Facade    <-- (35, 0, 25)
│   ├── Gateway_Exterior_Facade       <-- (-35, 0, 25)
│   └── Workshop_Exterior_Facade      <-- (35, 0, -25)
├── Exterior_Navigation_Anchors       <-- LAYER 31 (DO NOT RENAME)
│   ├── Anchor_FrontGate              <-- (0, 0, 0)
│   ├── Anchor_NorthBlock_Plaza       <-- (25, 0, 15)
│   ├── Anchor_Gateway_Plaza          <-- (-25, 0, 15)
│   └── Anchor_Workshop_Plaza         <-- (25, 0, -15)
└── OutOfBounds_ResetFloor             <-- BoxCollider isTrigger at Y = -5m with OutOfBoundsSafetyTrigger
```

### B. Interior Scenes Layout & $+60\text{m}$ Offset
In interior scenes, rooms are spaced $60\text{m}$ apart along the $X$-axis to keep them isolated:

#### `Scene_NorthBlock_Interiors.unity`:
- **`RoomPod_SmartClassroom`**: World Position $(0, 0, 0)$  | Key: `nb-classroom`
- **`RoomPod_RoboticsLab`**: World Position $(60, 0, 0)$   | Key: `nb-lab`
- **`RoomPod_FacultyCabins`**: World Position $(120, 0, 0)$ | Key: `nb-faculty`

#### `Scene_Gateway_Interiors.unity`:
- **`RoomPod_ExecutiveMBASeminar`**: World Position $(0, 0, 0)$ | Key: `gw-classroom`
- **`RoomPod_ACICStartupIncubator`**: World Position $(60, 0, 0)$ | Key: `gw-lab`

#### Interior Pod Structure:
```text
RoomPod_RoboticsLab [RoomPodAnchorController] (Pos: 60, 0, 0)
├── Geometry
│   ├── [Your_Blender_Room.glb]        <-- Local Pos (0, 0, 0)! (Layer 0)
│   └── Room_Props                     <-- Replace primitive props with Blender props
├── Anchors
│   ├── Anchor_Lab_Center              <-- Curated teleport pad (Layer 31)
│   └── Anchor_Lab_Workstation         <-- Curated teleport pad (Layer 31)
└── OutOfBounds_ResetFloor             <-- Trigger at Y = -2m with OutOfBoundsSafetyTrigger
```

---

## 5. Step-by-Step Environment Replacement

### Step 1: Import the GLB into Unity
1. Copy exported `.glb` files into `Assets/Models/Exterior/` or `Assets/Models/Interiors/`.
2. Select the `.glb` file in the Project view.
3. In the Inspector, ensure materials are converted to **Universal Render Pipeline/Lit** or **Universal Render Pipeline/Simple Lit**.

### Step 2: Replacing Campus Exterior
1. Open `Assets/Scenes/Scene_Campus_Exterior.unity`.
2. Drag your campus exterior `.glb` into the scene hierarchy under `Architecture_Exteriors`.
3. If using separate buildings, position North Block near $(35, 0, 25)$, Gateway near $(-35, 0, 25)$, and Workshop near $(35, 0, -25)$.
4. Disable or delete the primitive placeholder cubes.
5. Replace `Campus_Terrain_And_Roads` with your road/ground model.
6. **Assign Colliders**:
   - Add a `MeshCollider` to the ground mesh.
   - Set the GameObject layer to **`0 (Default)`**.
7. **Verify Teleport Pads**:
   - Verify that `Anchor_FrontGate`, `Anchor_NorthBlock_Plaza`, `Anchor_Gateway_Plaza`, and `Anchor_Workshop_Plaza` sit slightly above the pavement ($Y \approx +0.02\text{m}$).
   - Do not change their GameObject names.

### Step 3: Replacing Interior Rooms
1. Open `Assets/Scenes/Scene_NorthBlock_Interiors.unity` (or `Scene_Gateway_Interiors.unity`).
2. Select the target room pod (e.g. `RoomPod_SmartClassroom`).
3. Drag your room `.glb` into `Geometry`.
4. Ensure the `.glb` has **Local Position $(0, 0, 0)$** and **Local Rotation $(0, 0, 0)$**.
5. Add a `MeshCollider` or `BoxCollider` to the floor on **Layer 0 (`Default`)**.
6. Disable the primitive placeholder cubes (`Floor`, `Ceiling`, `Wall_North/South/East/West`).
7. Adjust the local position of the child `Anchors` (`Anchor_Classroom_Center`, `Anchor_Classroom_Podium`) so they rest cleanly on the new floor surface.

---

## 6. Physics, Layers & Interaction Rules

| Component / Object | Layer | Collider Type | Function |
| :--- | :---: | :--- | :--- |
| **Walkable Floors / Roads** | `0 (Default)` | `MeshCollider` (convex off) or `BoxCollider` | Headset physical floor support. |
| **Perimeter Boundary Walls** | `0 (Default)` | `MeshCollider` or `BoxCollider` | Prevents walking through walls into void. |
| **Teleportation Pads (`AnchorVisualPad`)** | `31 (Teleport)` | `CylinderCollider` / `MeshCollider` | Ray interactor target for XRI teleportation. |
| **`OutOfBounds_ResetFloor`** | Any | `BoxCollider` (`isTrigger = true`) | Catches tracking loss or clipping; triggers comfort fade reset. |

> [!CAUTION]
> **Never set the entire terrain or room floor to Layer 31 (`Teleport`).**  
> The BMU tour relies on *curated anchors* rather than free-form continuous teleportation. Setting the floor to Layer 31 will allow players to teleport anywhere, bypassing guided tour staging and UI placement.

---

## 7. Safeguarding Against Scene Builder Overwrites

[`BMUTourSceneBuilder.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/Editor/BMUTourSceneBuilder.cs) contains an automated menu command:
$$\text{BMU VR Tour} \longrightarrow \text{Build Complete VR Tour Scene (All 4 Scenes)}$$

Executing this command reconstructs all four scenes from C# primitives and **will overwrite your manual hierarchy changes**.

### Prevention / Long-Term Integration
To make custom GLB environments permanent and compatible with 1-click builds:
1. Save each environment as a Unity Prefab inside `Assets/Prefabs/Environment/`:
   - `Assets/Prefabs/Environment/Prefab_Exterior_Campus.prefab`
   - `Assets/Prefabs/Environment/Prefab_Room_SmartClassroom.prefab`
   - `Assets/Prefabs/Environment/Prefab_Room_RoboticsLab.prefab`, etc.
2. In [`BMUTourSceneBuilder.cs`](file:///C:/Users/SHREY/Desktop/Projects/ARVR/BMU_Campus_VR_Tour/Assets/Scripts/Editor/BMUTourSceneBuilder.cs), replace the primitive cube generation with prefab instantiation:
   ```csharp
   GameObject customPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/" + podName + ".prefab");
   if (customPrefab != null)
   {
       GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(customPrefab, podObj.transform);
       instance.transform.localPosition = Vector3.zero;
   }
   ```
3. If you do not plan to use automated code generation, disable the menu item in `BMUTourSceneBuilder.cs` by commenting out lines 78–99.

---

## 8. Meta Quest 3S Performance Checklist

Before making a standalone Android build:
- [ ] **Draw Calls**: Ensure visible draw calls remain under 150 for exterior and under 100 for interiors.
- [ ] **Static Flag**: Select all static environment meshes and check the **`Static`** box in the Inspector (enables Static Batching and Occlusion Culling).
- [ ] **Texture Sizes**: Cap exterior diffuse/normal textures at $2048 \times 2048$ and interior prop textures at $1024 \times 1024$ (ASTC format on Android).
- [ ] **Lightmapping**: Bake lighting using the **Universal Render Pipeline GPU Lightmapper** (`Window -> Rendering -> Lighting`). Set resolution to 16–32 texels/unit.
- [ ] **Test Playmode**: Open `Assets/Scenes/Master_Tour_Core.unity`, press Play, and verify:
  1. Spawning at Front Gate anchor $(0, 0, 0)$.
  2. Teleporting to North Block plaza $(25, 0, 15)$.
  3. Clicking `"Go Inside"` $\rightarrow$ smooth fade into `RoomPod_SmartClassroom`.
  4. Clicking `"Switch Room"` $\rightarrow$ smooth fade into `RoomPod_RoboticsLab` at $+60\text{m}$.
