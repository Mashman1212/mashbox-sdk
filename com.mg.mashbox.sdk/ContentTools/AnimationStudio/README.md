# MashBox Animation Studio

Right-click an animation clip in the Project browser (including a clip inside an expanded FBX) and choose **Open Clip as Take in Animation Studio**. Right-clicking an FBX with multiple clips opens a clip picker. Choose where to save the new take; Studio samples and loads it automatically using the clip's frame rate. Humanoid clips use the current Studio character when available; generic FBX clips use their source model skeleton. The original clip is unchanged.

**MashBox → Animation Studio** opens the viewport directly. Select an Animation Take and click **Open in Animation Studio** in its asset inspector, or double-click the take, to load it in that viewport. The project remembers the last saved take locally for reopening. The viewport inspector's **Character & take** section contains character selection, body/outfit setup, take switching, new-take creation and the example entry point; Selection, Outfit, Clip and the workstation-private Generate tab hold the editing tools. The old standalone setup window is no longer required. Animation Takes use the supplied teal film/play icon, assigned persistently on their script importer.

Open **MashBox → Animation Studio** (also linked from the SDK Content Tools).

1. Click **Use MashBox SM base** to load `Skeleton.fbx` and `SM_Body_01.fbx`. For other characters, select the GameObject with the Humanoid Animator at its root. Optional body meshes are remapped by unique bone names, ignoring namespaces.
2. **New take…** saves an editable `.asset`. The separate Animation Viewport uses normal Unity orbit, pan, zoom and transform handles. It owns an isolated preview scene, so it does not dirty your gameplay scene or instantiate gameplay scripts.
3. Choose a hand or foot, then **Match → IK**. Move the target with **W**, rotate with **E**, or click a gold elbow/knee target to steer the bend. Increase **Control size** for larger screen-space pick areas. The IK blend goes from FK at 0 to full IK at 1.
4. **Pin both feet**, click the green hips ring, and move or rotate the pelvis. Green rings also select spine sections; blue rings select other FK joints. Clicking a skeleton segment selects its parent joint for rotation. Editing an IK-driven limb matches its solved pose and releases only that limb; the other pins remain active. Final IK solves the body against the foot targets. Spine, head and fingers are available through the FK bone picker.
5. Scrub the numbered timeline along the bottom of the Animation Viewport. Drag the playhead, use the frame/key step buttons, or type a frame number. Playback, Auto Key and Key [S] are beside the ruler; timing and Delete key remain in the Animation Studio settings window. **Auto Key** defaults OFF: move controls freely, then press **S** or **Key pose** to insert or replace a whole-pose key. Unkeyed changes are temporary; scrubbing, playback, closing or reloading the workspace discards them. **Save now** saves recorded keys and does not key a temporary pose. With **Auto Key** ON, dragging commits one key on release, with one Undo step. Key saving runs after a one-second idle debounce, outside the mouse event; closing, switching takes, baking and Play Mode transitions flush pending keyed edits. **Space** plays/pauses. Use previous/next key, delete, copy/paste and Unity Undo/Redo for keys. **Match all → FK** captures the solved skeleton and releases all IK targets without changing the pose.
6. **Bake Unity animation clip…** creates a uniquely named `.anim`; use Humanoid muscles for your Humanoid Animator/Animancer setup, or turn that option off for explicit transform curves on an identical Generic hierarchy. The output does not require the authoring rig or Final IK.

## Existing animations and Play Mode

Assign a source animation and **Import clip into a new take…** to sample it at the take's frame rate. The source remains unchanged. Import produces FK keys in Base motion. Use the Layers tab for corrections that preserve those imported keys.

The workspace can be opened during Play Mode. Select the live character's Animator root and **Capture selected live character pose** to copy its local bone pose into the current frame. Editing and playback occur on the isolated preview, while gameplay continues independently. Take assets and explicitly baked clips persist after leaving Play Mode. The tool does not override the live Animator or record a continuous gameplay stream.

## Scope and conventions

- Full Body Biped IK uses an existing licensed Final IK installation, discovered at editor runtime. No Final IK code is shipped with this tool. Without Final IK, FK authoring remains available; takes containing IK keys refuse to open instead of silently changing their motion.
- Takes require a persistent character prefab/model with an unoptimized, uniquely named transform hierarchy and a Humanoid Avatar for IK. Generic rigs support FK and transform export. Changing the source hierarchy requires a new take.
- All targets/poles use character-root coordinates. Moving the FK character root moves the entire rig and its targets. Move the hips to work against planted feet. To animate moving contact surfaces, key the target positions along with the motion.
- Smooth interpolation uses eased position interpolation and quaternion slerp between whole-pose keys. Linear and stepped modes are also available. Bake samples every frame; it does not preserve editable rig controls in the exported clip.
- Humanoid export writes muscle and body-root curves. Generic export writes local position, quaternion rotation and scale. Humanoid retargeting can introduce differences from the precise solved bone pose. Validate the resulting clip on the intended avatar and root-motion configuration.
- This release provides an IK/FK authoring workflow, not feature parity with Maya HumanIK: no editable tangent graph, animation events/blendshape authoring, continuous motion capture or FBX export.
- Files are editor-only. Take assets are authoring data; reference the baked `.anim` from gameplay.

## Rider, bike and additional actors

Open the viewport's **Actors** tab. **Add vehicle · BMX** loads the project's Bike Skeleton; **Actor / vehicle source** and **Add actor** accept additional character or generic model assets. Companion takes are saved as subassets of the primary take and reopen together.

Choose **Primary** in **Edit actor**, assign the Player FBX animation subclip, and click **Import clip on shared timeline**. Select the bike and repeat with the matching BMX subclip. Scrubbing and playback share time in seconds; each actor retains independent pose keys. Shorter motion holds its final pose. Select the actor before editing or pressing **S**; Auto Key applies to the selected actor. Import extends the shared timeline when needed.

The bike exposes the source Maya control names and sampled curve shapes for BMX, bars, frame, drivetrain and pedals. Bars/frame use their mapped rotation axes; rotating the crank counter-rotates the pedals, preserving their orientation. Wheels and PID balance also have controls. See **MayaBikeRigMapping.md** for the exact source mapping and limitations. This is a specific recreation of the BMX rig; it does not import arbitrary Maya constraints or automatically attach rider hands/feet to the bike.

**Export all actors as separate clips…** writes independent Unity `.anim` assets at the same frame rate and duration. Bike output contains the Joints hierarchy, excluding authoring controls. FBX files are supported as import sources; export produces Unity animation clips.

## Attach hands, props and other controls

In **Actors → Attachment Constraints**, set **Constrain actor / control** to the rider and **IK / Left hand** or **IK / Right hand**. Set **Follow actor / bone / IK** to the vehicle and its steering or door joint. For the BMX bars control, choose `Bars_Joint`; the Maya control shapes drive those joints. Generic bones and other actors' hand/foot IK targets are also available as endpoints.

Position the hand where you want it, then use **Attach with current offset**. Turn off **Keep current offset** to snap directly to the target. Offsets use the target's orientation, in metres, and follow its translation and rotation without inheriting its scale. Select the attached control in the viewport to move/rotate its attachment offset, or edit the numeric offset in Actors. Offset edits change the saved constraint, independently of Auto Key; pose keys remain separate.

Each attachment has Enabled, Weight, position/rotation switches, First/Last frame and Blend in/out frames. The default range starts at the current frame. Zero blend gives an immediate attachment/release; a nonzero blend fades influence to/from the underlying pose at the range edges. Match the underlying hand pose for a smooth release. Weight/offset settings are constant within this range, not individually animated channels. Circular dependencies and missing endpoints are reported; disable or remove the invalid constraint. Same-actor constraints cannot target their own driven limb or descendants.

Constraints evaluate after all actors sample the shared clock and while posing a target. Both normal clip baking and **Export all actors as separate clips** sample the resolved motion into the exported clips, so no Studio constraint component is needed in gameplay. Humanoid retargeting and IK reach limits still apply: an unreachable target cannot force a limb to stretch. Exported contacts assume the same relative actor alignment and synchronized playback. This is an authoring attachment system, not a runtime steering/grab interaction component.

## Manual key shortcuts

With the viewport focused, **Shift+W** keys the selected control's position and **Shift+E** keys its rotation. **W/E** still select Move/Rotate; **S** retains the Studio's full-pose key action. Manual shortcuts work with Auto Key off and on, and follow the selected actor. Text fields and Ctrl/Alt/Command combinations are ignored.

Channel keys preserve the animation of other channels and controls, including interpolation between existing full-pose keys. Unkeyed edits remain visible and marked unkeyed until you key them or scrub away. Hand/foot IK channel keys also retain the IK weight needed to activate the target. Knee/elbow pole targets accept position keys only. A BMX crank rotation key includes the linked pedal counter-rotations. Attachment offsets remain saved constraint settings; these shortcuts key the underlying actor pose, not animated attachment offsets.

## Vehicle parts and actor presets

Select the bike in **Actors**, then open **Outfit**. Assign `Proto_Bike`, `PJX_BMX` or `PJX_MTB` to **Vehicle prefab** and click **Read equipped defaults / anchors**. This reads the first/default child in each vehicle equip slot, builds isolated renderer copies, and matches named anchors where available. The result reports how many mounts matched; other parts retain their authored prefab rest placement. Runtime equip logic, physics and procedural MTB suspension are not executed.

Use **Add vehicle part** to assign a frame, fork, wheel or other part directly. **Driven by joint** chooses the animated joint. **Mount on part / Mount anchor** positions it from an earlier part's anchor at rest, then attaches it to the selected animated joint (so steering follows Bars_Joint). **Preserve model rest** retains model-space placement; disable it to use the joint's local space. Adjust position, rotation and scale, then **Apply vehicle outfit**. Part appearance never adds animation bones or modifies source prefabs.

**Save selected actor preset…** stores the skeleton, character outfit, vehicle parts and handle layout in a reusable asset. Choose that asset and **Add from preset** to create an independent actor. Presets store setup, not motion or inter-actor constraints.

The **Take** tab replaces the Character & take foldout. **Selection** shows the selected actor's transforms first; actor setup and attachments remain in **Actors**. For the bike, enable **Edit handle layout** to move/rotate control graphics without editing animation. Layout offsets save with takes and presets. The original curve extraction omitted the Maya joint root's driven 35 cm height; the display now removes that extra offset. Preview construction also preserves the source root transform. Already sampled takes keep their saved root keys; reimport matching rider/bike clips if those keys were created with the old root handling.

## Animation layers and human corrections

Select an actor and open **Layers**. **Add correction layer + selected control** creates a layer above Base motion and includes the selected bone or IK control. Select other controls in the viewport/body diagram and use **Add selected bone / IK control to layer** to include them. Bone memberships cover their transform channels; IK memberships cover the target and required IK/FK blend. Removing a membership disables its contribution without deleting stored keys.

Choose **Additive** for deltas relative to the underlying animation or **Override** for absolute values blended over it. Layer weight, Mute, Solo and Move earlier/later control evaluation. Solo keeps Base motion and the solo correction layers. The last enabled layer is the one to key; to edit a lower layer, mute the layers above it. Base motion keying requires correction layers to be muted. This avoids accidentally recording the visible correction stack back into the base or another layer.

**Key on layer** in Selection chooses the editing destination. S, Shift+W, Shift+E and Auto Key write to that layer's assigned controls. The timeline shows the selected actor/layer's keys. Keys hold their first/last value outside their keyed range; add neutral keys around a correction to localize it. Layer weights are constant settings, not animated weight curves. Switching layers or changing layer settings resamples the pose, so key wanted edits first. Additive/Override changes resample existing layer channels at the take frame rate to preserve the result at those frames.

**Human · Whole-clip rotation offsets** provides a mapped joint picker, Lift/lower or Bend, Forward/back or Side bend, and Twist sliders. Choose shoulders, upper/lower arms, spine, head or another mapped joint. The controls use axes derived from the character's rest orientation, with mirrored directions for left/right limbs. Offsets affect the entire clip, require no pose keys, and are additive rotations after that layer's keyed channels. Layer weight/mute also affect these offsets. Reset a joint to zero to remove its constant correction. Final IK and attachment constraints solve afterward and can restrict visible motion of pinned limbs.

The take stores Base motion and each layer separately; Undo, saving and reloading preserve them. Normal baking and paired actor export sample the composed pose once into ordinary animation clips. Foot cleanup and endpoint tools operate on the base motion while retaining correction layers; endpoint captures that already contain corrections may need adjustment when reused. Actor presets remain setup-only and do not include animation layers.

## Validation

The implementation is compiled against Unity 6000.4.12f1. The isolated validation fixture exercises the real MashBox skeleton/body, optional Final IK bridge, deterministic pose evaluation, pinned feet and baked clip playback. It lives outside the SDK's runtime assemblies.



## Dressing the preview

Keep the current skeleton and take loaded. Change **Preview body / bust**, then use **Add clothing / skinned mesh** for shirts, trousers and other compatible skinned prefab/model assets. Click **Apply outfit** to rebuild only the appearance while keeping the working pose, camera and pose keys. The outfit references save with the take. Base textures, tints, texture transforms and standard alpha cutouts use fixed studio preview shading automatically; the Neutral preview material toggle switches to grey shading. This preview does not reproduce custom shader effects, metallic response or full HDRP lighting.

Meshes must be skinned to compatible, uniquely named bones on this skeleton. Existing blendshape weights on the supplied prefabs are copied. Skinned pieces must fit the skeleton. Rigid hats and optional body cutouts are configured with Piece type as described below.

## Viewport inspector

The Animation Inspector is pinned to the viewport's top-right corner. Selection follows picked bones and IK controls and exposes local FK transforms or character-space IK targets, rig options and pose actions. Outfit and Clip tabs provide appearance, timing, import and bake controls beside the animation. Collapse the inspector with its header arrow for more viewport space. Numeric edits follow the same Auto Key setting as scene handles.



## Outfit roles and body cutouts

Each clothing slot has a **Piece type**:
- **Skinned Clothing** maps to the skeleton. Enable **Cut body underneath** to remove covered body triangles in the preview. **Coverage distance (m)** defaults to 0.03 m; increase carefully for loose clothing.
- **Body** marks extra skin meshes (for example a bust) as cutout targets. The main Preview body / bust slot is always a target.
- **Hat** attaches the prefab's renderers to the Humanoid Head bone, with local Head offset, Head rotation and Hat scale. Gameplay scripts and colliders are not copied.

Click **Apply outfit** after changing roles, offsets or coverage. Coverage is calculated once in the source pose by checking the clothing surface near body vertices, then conservatively removing triangles whose three vertices are covered. The displayed triangle count confirms the operation. Check cuffs/collars and test the animation: this geometric approximation can leave boundary slivers or hide exposed skin at excessive distances; it is not cloth simulation or a perfect automatic garment fit.

Cutouts modify temporary mesh copies only. Skin weights, blendshapes, vertices and source meshes are retained. Disabling cutout or removing a garment and applying again restores the body. Roles and cutout settings save in the take. Exported animation clips contain motion only; hats and body masking must be configured separately on the gameplay character.

Enable **Double-sided** on an outfit piece and click **Apply outfit** to show its inside faces in both textured and neutral preview shading. This setting saves with the take and uses temporary preview materials; source materials are unchanged.


## Local motion generation

Step transitions now include a two-foot anticipation phase before each lift, pelvis transfer toward the support foot, support-knee loading, a small torso counter-lean and a landing/settling phase. **Body weight shift** controls lateral pelvis/torso movement (default 0.8). These are kinematic animation cues, not a physical center-of-mass solver or learned transition model. Feet are solved after the body shift. Unchanged stances do not add artificial sway. Try 1.2–1.5 seconds per end for slower weight transfers. Use **Apply result to current take** again to rebuild from the raw generated result with the new transitions; older untracked transitions must be rebuilt from the raw result once.

**Update current take** defaults on. **Apply result to current take** replaces the active take's motion, frame rate and length (including endpoint transitions), retaining its asset identity, character and outfit. Cleanup and endpoint matching also update the active take in this mode. Changes are undoable and saved through the existing deferred save system with no Save As prompt. Turn the toggle off to save separate take assets. Results are fully prepared before the existing take is modified. New step transitions track their added frame ranges, so applying them again replaces those ranges instead of stacking them. Older takes have no transition metadata: use Apply result to current take once to rebuild those from the raw generation.

To source an endpoint directly from an animation asset, assign **Start animation clip** or **End animation clip**, choose **Sample time (seconds)**, then click **Use clip pose as start/end**. Embedded clips in FBX assets are supported. The sampled pose is stored as a snapshot; changing the clip or time requires clicking the button again. Humanoid clips use the selected character's avatar; generic clips require matching skeleton transform paths. Sampling runs in an isolated preview rig and does not replace the current take or working pose. The existing **Use start pose for end too** button also works with clip-sampled poses.

The Selection inspector's **Body picker** selects individual humanoid joints, hands, feet, toes and elbow/knee targets; finger buttons select each mapped finger segment. Click a limb joint or IK target to expose its arm/leg IK blend. The current solver has four limb blends, not independent blends for every bone; torso and fingers remain FK. **Show IK** and **Show FK** control viewport visibility, **Fade by IK/FK blend** dims inactive controls, and **Hide inactive controls** removes them at zero influence. The diagram stays selectable when viewport controls are hidden. Visibility changes do not key poses. Blend edits follow Auto Key / S key behavior. Enabling a fully FK limb's IK blend first matches the current end-effector pose.

**Start / end poses:** open an idle take and scrub to the desired pose, then use **Capture current as start** or **Capture current as end** in Generate. **Use start pose for end too** returns to the same idle. Captures are independent of the currently loaded take and include all local bone transforms, including hips position and orientation; different skeletons are rejected. **Step between stances** defaults on: adds a sequential two-foot transition at each captured end (default 0.9 seconds each), preserving all original dance frames. Adjust transition duration and foot lift. Use the original dance take with **Match current take to these poses…** to save a corrected copy; older untracked blends require rebuilding from the raw result. Turn stepping off for the original overlapping pose blends. Grounded, reachable poses on a flat floor are required; large ankle-height differences are rejected. Matching runs after import foot cleanup to preserve exact endpoints. Clear captures to import unconstrained motion. These are procedural steps or pose blends after generation, not AI pose conditioning: the installed HY-Motion sampler has no endpoint pose input. Large pose differences may require longer blends/manual cleanup, and exact pose matching does not guarantee matching idle velocity or foot contact through the transition.

On the private Generate panel, **Plant feet on import** is enabled by default. It detects sustained low, slow ankle motion after retargeting and bakes position corrections into FK keys, preserving root travel and authored foot rotations. **Clean up feet in current take…** saves a separate corrected copy of an existing take. **Contact sensitivity** controls the maximum speed treated as contact; reduce it for intentional shuffles or increase it cautiously for sliding. Contacts blend in and out; leg reach limits still apply. This heuristic is for a flat floor and does not infer stairs or guarantee perfect contact. Leave **Keep motion in place** off for travelling dances; removing root travel can introduce sliding.

The viewport inspector's Generate tab launches local text-to-motion (HY-Motion Lite, CUDA) or video pose capture (MediaPipe, CPU on Windows). Generate, then Import result as a new take; FK/IK editing and baking remain available. Install the separate Tools/MotionGeneration environment in the Unity project, or locate it with Local tools. No prompts or videos are uploaded. Cancellation and progress run without AssetDatabase refreshes.

HY-Motion has territorial restrictions on its model AND outputs (including the EU, UK and South Korea); read its upstream license before using generated content. The video option estimates grounded, in-place body motion only. See Tools/MotionGeneration/README.md for setup, licensing, model limitations and validation. Model weights and Python dependencies are not included in the SDK package.



All actor controls: the toggle above the viewport inspector tabs shows selectable rig controls for every actor. Clicking a control selects its actor for transforms and keying. Turn it off to show controls only for the selected actor. Existing IK/FK visibility and blend fading still apply.


Timeline key editing: Shift-drag selects an inclusive frame range. Right-click for Copy, Delete, or Paste at the clicked frame. With timeline focus, Delete removes selected keys, Ctrl+C copies, and Ctrl+V pastes at the playhead. Paste preserves spacing and replaces existing keys at matching frames. Clipboard is limited to the same actor and layer; base motion retains at least one key. Layer keys can be deleted completely. Undo is supported for edits.


AI in-betweens (private workstation): Shift-drag a timeline range, right-click Generate AI in-betweens, then choose Add result as override layer in Generate. Pose-only Kimodo runs locally on CUDA with endpoint/neighboring-pose conditioning and contact correction. Intervals are 0.2–7.8 seconds; no timeline duration is added. The generated layer preserves endpoints and affects only the selected interval. Source animation must remain unchanged until import. This first version requires a Humanoid actor without active attachment constraints or layer Solo; it does not generate vehicle motion or accept text prompts. Review feet after retargeting. Details and reproducible validation are in the local Tools/MotionGeneration/README.md.


Rig selection: Lock actor meshes (above the inspector tabs) defaults on. Drag empty viewport space to marquee-select visible rig controls; Shift-drag adds, Shift-click toggles controls, and clicking empty space clears. All actor controls enables selecting across actors. W/E manipulate the group (rotations use each control's own pivot; bike axes stay constrained). Shift+W/Shift+E key the selected channels on each actor's current layer. Add selected controls to layer uses members of that layer's actor. Unlock meshes to restore Unity mesh selection. Hidden controls are excluded from marquee selection.

