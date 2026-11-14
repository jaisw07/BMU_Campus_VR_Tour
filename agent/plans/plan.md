# Implementation Plan: UI & UX Overhaul

**Target Project**: `BMU_Campus_VR_Tour` (Unity 6000.3.27f2, OpenXR, Meta Quest 3S)  
**Status**: Approved via Grill-Me Alignment  
**Date**: November 14, 2026  

---

## 1. High-Level Summary of Goals

1. **Darker Bold High-Contrast Typography Across the Board**:
   - Set all headings, subtitles, body copy, bullets, captions, and pill text to Pitch Black (`#000000` / `Color.black`).
   - Upgrade font styles to `FontStyles.Bold` across all body copy and descriptions (replacing `FontStyles.Normal`).
   - Retain 72% frosted acrylic card opacity (`GlassCard = (1f, 1f, 1f, 0.72f)`) while tuning the TextMeshPro soft white underlay halo for razor-sharp legibility against exterior 3D environments on Quest 3S LCD displays.

2. **Replace Campus Hub Button on Main Screen with "Skip Intro"**:
   - In `Stage1_IntroCarouselPanel`, replace button text `"Explore Campus Hub →"` with `"Skip Intro"`.
   - Update button background color from `ColorRed` to BMU Primary Blue (`#004B87` / `ColorPrimary`).
   - Maintain the click wiring to `CampusTourManager.OpenCampusHub()` (transitioning directly to Stage 2 Destination Hub).

3. **Remove Bottom Narration Panel & Integrate Sound Toggle in Top-Right Panel**:
   - Remove `NarrationOrnament` (waveform equalizer bars, subtitle text, and bottom sound button).
   - Rebalance vertical stage panel margins (`offsetMin = Vector2(20f, 24f)`, `offsetMax = Vector2(-20f, -96f)`).
   - Elongate `ControlsOrnament` (top-right pill) from `276f` to `330f`.
   - Add a 38x38 circular frosted glass sound toggle button to the left of `[Recenter UI]`, establishing the layout:
     `[Sound Toggle]  [Recenter UI]  [Restart Tour]  [Drag to Move]  [Minimize]`
   - Include dynamic `VRHeaderTooltip` ("Mute Audio" / "Unmute Audio") and interactive Speaker On / Speaker Off icon switching.

4. **Unified Building & Room Selector Option Cards (Whole Card is the Button)**:
   - Remove separate `"Teleport Outside →"` and `"Enter 360 Room →"` bottom buttons.
   - The entire card GameObject becomes the interactive `Button`.
   - Standardize all 3 building cards and room cards to the same style & BMU Primary Blue (`#004B87`) badge theme.
   - Attach enhanced `VRButtonFeedback` to the cards:
     - High-intensity electric cyan glow border outline (`#00D4FF`).
     - Smooth 25mm forward translation towards the viewer (`anchoredPosition3D.z = -25f`).
     - Subtle 1.03x scale punch.
     - Smooth return lerp to resting state on ray exit.

---

## 2. File-by-File Technical Blueprint

### A. `Assets/Scripts/VRButtonFeedback.cs`
- **Objective**: Support 3D depth translation (`anchoredPosition3D.z`) on hover alongside scale punch and glow border.
- **Fields to Add**:
  ```csharp
  [Header("3D Hover Translation")]
  [SerializeField] private float m_HoverZOffset = -25f; // Local -Z moves forward toward VR viewer
  private Vector3 m_OriginalAnchoredPos3D;
  private Vector3 m_TargetAnchoredPos3D;
  ```
- **Lifecycle & Event Handling**:
  - In `Awake()`: Cache `m_OriginalAnchoredPos3D = m_RectTransform.anchoredPosition3D; m_TargetAnchoredPos3D = m_OriginalAnchoredPos3D;`.
  - In `ResetVisualState()`: Reset `m_TargetAnchoredPos3D = m_OriginalAnchoredPos3D; m_RectTransform.anchoredPosition3D = m_OriginalAnchoredPos3D;`.
  - In `OnPointerEnter(PointerEventData)`:
    - `m_TargetAnchoredPos3D = new Vector3(m_OriginalAnchoredPos3D.x, m_OriginalAnchoredPos3D.y, m_OriginalAnchoredPos3D.z + m_HoverZOffset);`
    - `m_TargetScale = m_OriginalScale * m_HoverScale;`
    - Activate `m_HighlightOutlineImage` with electric cyan (`#00D4FF`).
  - In `OnPointerExit(PointerEventData)`:
    - `m_TargetAnchoredPos3D = m_OriginalAnchoredPos3D;`
    - `m_TargetScale = m_OriginalScale;`
    - Deactivate `m_HighlightOutlineImage`.
  - In `Update()`:
    - Lerp `m_RectTransform.anchoredPosition3D` towards `m_TargetAnchoredPos3D` using `Time.unscaledDeltaTime * m_LerpSpeed`.

### B. `Assets/Scripts/TourAudioNarrator.cs`
- **Objective**: Remove dependency on subtitle text/waveform HUD while supporting dynamic tooltip toggling on the header sound button.
- **Adjustments**:
  - Make `m_CaptionText` and `m_WaveformBars` optional (safely check for null).
  - In `ToggleMute()` / `UpdateMuteVisuals()`:
    - Toggle `m_SpeakerIconOn` and `m_SpeakerIconOff` gameObjects.
    - Check if `m_AudioToggleButton` contains a `VRHeaderTooltip`:
      ```csharp
      var tooltip = m_AudioToggleButton != null ? m_AudioToggleButton.GetComponent<VRHeaderTooltip>() : null;
      if (tooltip != null)
      {
          tooltip.TooltipText = m_IsMuted ? "Unmute Audio" : "Mute Audio";
      }
      ```

### C. `Assets/Scripts/CampusTourManager.cs`
- **Objective**: Adapt button listeners to full-card button objects.
- **Adjustments**:
  - Ensure `m_BtnSelectNorthBlock`, `m_BtnSelectGateway`, and `m_BtnSelectWorkshop` point directly to the `Button` component attached to `Card_NorthBlock`, `Card_Gateway`, and `Card_Workshop`.
  - In `PopulateInternalBuildingHub(string buildingKey)`:
    - Grab `Button btn = cardObj.GetComponent<Button>() ?? cardObj.GetComponentInChildren<Button>();` and wire `btn.onClick.AddListener(() => TeleportToLocation(room.key));`.

### D. `Assets/Scripts/Editor/BMUTourSceneBuilder.cs`
- **Objective**: Procedural scene generator changes.
- **Typography Overhaul**:
  - Change all heading/body text colors to `Color.black`.
  - Set `FontStyles.Bold` for all text blocks (`slideTitle`, `slideDesc`, `slideStats`, `slideIndex`, `hubTitle`, `hubSubtitle`, `locHeading`, `locSummary`, `locFeatures`, `inTitle`, etc.).
  - Set `GetHaloMaterial` underlay properties to enhance contrast (`_UnderlayColor` = `(1,1,1,0.7)`, `_UnderlayDilate` = `0.12`, `_UnderlaySoftness` = `0.25`).
- **Stage 1 Intro Panel**:
  - Button `BtnSkipIntro`: Label `"Skip Intro"`, color `ColorPrimary`, bold white text.
- **Controls Ornament (Top-Right)**:
  - Width expanded: `330f` (was `276f`).
  - Add `BtnAudioToggle`: 38x38 circle with `s_SpriteGlassCircle`, `VRHeaderTooltip` ("Mute Audio"), containing `s_SpriteSpeakerOn` and `s_SpriteSpeakerOff`.
  - Position: `anchoredPosition = new Vector2(-274f, 0f)`.
  - Recenter button at `x = -146f` (width 120f).
- **Remove Narration Ornament**:
  - Remove instantiation of `NarrationOrnament` GameObject.
  - Wire `TourAudioNarrator` with `btnAudioToggle`, `speakerOn`, `speakerOff`.
  - Adjust stage panels vertical bounds (`offsetMin = new Vector2(20f, 24f)`, `offsetMax = new Vector2(-20f, -96f)`).
- **Stage 2 & Stage 4 Option Cards**:
  - Modify `CreateBuildingCard(...)`:
    - Add `Button` and `VRButtonFeedback` directly to the `card` GameObject.
    - Remove `actionBtn` creation at the bottom.
    - Expand `cardStack` offset bounds (`offsetMin = new Vector2(24f, 28f)`, `offsetMax = new Vector2(-24f, -60f)`) to comfortably fill the card.
    - Theme color unified to `ColorPrimary` (`#004B87`) for all cards.
    - Return `card.GetComponent<Button>()`.

---

## 3. Step-by-Step Implementation Sequence

1. **Phase 1: Update Runtime Components**
   - Apply 3D translation & cyan outline modifications in `VRButtonFeedback.cs`.
   - Apply tooltip and headless narration null-safety in `TourAudioNarrator.cs`.
   - Verify card button handling in `CampusTourManager.cs`.

2. **Phase 2: Update Scene Builder (`BMUTourSceneBuilder.cs`)**
   - Implement typography changes (Pitch Black, bold styles, halo adjustments).
   - Implement "Skip Intro" button styling in Stage 1.
   - Implement Controls Ornament expansion and add Sound Toggle icon button.
   - Remove Narration Ornament and adjust stage panel layout offsets.
   - Refactor `CreateBuildingCard` so card is the button and styles are unified.

3. **Phase 3: Execute Procedural Scene Rebuild**
   - Run the builder via Unity MCP or Editor menu: `BMU VR Tour → Build Complete VR Tour Scene (All 4 Scenes)`.
   - Ensure all 4 scenes (`Master_Tour_Core`, `Scene_Campus_Exterior`, `Scene_NorthBlock_Interiors`, `Scene_Gateway_Interiors`) are cleanly regenerated.

4. **Phase 4: Verification & Polish**
   - Verify compile clean status (no compile errors or warnings).
   - Verify in Unity Editor XR Device Simulator:
     - Main screen displays "Skip Intro" in blue, leading directly to Stage 2.
     - Text legibility is high-contrast black and bold across all panels.
     - Controls ornament has sound button on the left of Recenter, toggling mute and showing tooltip.
     - Building cards and room cards have matching BMU Blue badges and styles.
     - Hovering on any card activates electric cyan border and smoothly steps the card forward by 25mm.
     - Clicking anywhere on a card triggers teleportation.
