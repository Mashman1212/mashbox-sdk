# Bike rig mapping

## Anchor-driven SDK rig sheet (2026-11-04)

The Maya source connects `Bars_Joint` to `Forks_Anchor_parentConstraint1` and `Headset_Anchor_parentConstraint1`, `DriveTrain_Joint` to `BB_Anchor_parentConstraint1`, and the left/right pedal joints to `Pedal_Anchor_parentConstraint3/4`. These are Maya parent constraints, with target joint orientation and parent matrices connected; they are not simple mesh parenting. Studio's SDK mapping below implements the requested local channels using the SDK anchor placements rather than copying Maya's world-space constraint offsets.

| SDK part | Driver | Channel applied after anchor placement |
| --- | --- | --- |
| Frame | Frame_Joint | Rest-offset root motion |
| Headset and Forks | Bars_Joint | Local yaw relative to joint rest |
| BB | DriveTrain_Joint | Pitch alignment in socket-parent space, compensating SDK frame/socket rest tilt |
| Left pedal | LeftPedal_Joint | Local pitch relative to joint rest |
| Right pedal | RightPedal_Joint | Local pitch relative to joint rest |
| Other mounted parts | SDK parent anchor | Inherit parent motion once |

Evaluation order: solve the Full BMX live anchor assembly (including IDs, sides, scale and pedal orientation), preserve its attachment hierarchy, apply animated skeleton poses, then apply the local rotation constraints. Each evaluation starts from the stored snapped rotation, avoiding accumulated offsets. Forks carry the front wheel assembly; the headset carries stem/bars/grips; BB carries crank arms and their pedal anchors. Pedal channels retain the existing drivetrain counter-rotation behavior.

BB alignment uses the drivetrain orientation converted into the socket parent's coordinates and applies its pitch twist after snapping. It keeps socket position and scale while removing the inherited rest-pitch mismatch. Native validation covers rest alignment, animated pitch through 180 degrees, and an additional 13-degree socket tilt.

`Bars_Joint` follows the headset part's position and tilt (pitch/roll), converted into the joint parent's coordinates. Its yaw remains driven by the bars control. The tilt calculation uses the headset's unsteered basis and separates tilt from yaw twist, avoiding a yaw feedback loop through the headset mesh. Curve dragging preserves this tilt while changing steering. Native checks move and tilt the headset socket and repeat capture/evaluation 20 times to check alignment and yaw stability.

The displayed Bars_Curve is centered on that driven joint. Its extracted Maya points are recentered and their plane aligned to local Y before applying the current joint transform, avoiding the original Maya placement/tilt being applied again. Rendering and picking share these points; existing explicit handle-layout offsets remain supported. The bars shape keeps its original size and no longer uses the generic 35 cm curve correction.

DriveTrain_Curve, LeftPedal_Curve and RightPedal_Curve likewise use centered shapes, aligned to the bones' local X rotation planes, without the legacy translation correction. Their centers follow the driven bones as the crank moves. `StudioBikeCurveValidation` checks the actual extracted shapes at 0, 45, 90, 180 and 270 degrees with a translated and rotated bone basis; their centers remain on the bone position.

When the BMX root rotation gizmo is selected, its screen area takes picking priority over overlapping vehicle curves, human control rings and skeleton segments. Those controls remain visible but do not compete for clicks inside the gizmo. Outside it, picking is unchanged; Shift allows deliberate selection of overlapping controls. Alt navigation remains unchanged.

After mesh rotation constraints finish, `DriveTrain_Joint` takes the BB part's world position. Then `LeftPedal_Joint` and `RightPedal_Joint` take the positions of the actual pedal mounts resolved for their respective parts. Both anchors may be named `Pedal_Anchor`; side selection comes from the saved anchor chain, not a scene-wide name search. These are position-only constraints: joint rotation and scale are preserved. Parent-before-child evaluation prevents the drivetrain move from displacing the already-constrained pedal joints. Targets beneath their driven joint are excluded to prevent positional feedback. This applies to anchor-preserving SDK outfits.

Empty/default BMX outfits use this automatically. Previously saved explicit outfits retain their authored bindings; use **Outfit / Load Full BMX from SDK** to replace those entries with the new anchor-preserving layout. Legacy frame and wheel bones remain available to imported clips, but their unused controls are hidden. Vehicle wheel spin is not separately applied by this SDK mapping.

Inspected source: `D:/Character/Vehicles/BMX/BMX Rigged.ma`, referenced by `Moto_Left.ma`. The source files are read only; no Maya scene edits or scripts were executed.

| Maya control | Unity joint behavior |
| --- | --- |
| BMX_Curve | Joints translation and rotation |
| Bars_Curve.rotateY | Bars_Joint local Y rotation relative to its imported joint orientation |
| Frame_Curve.rotateY | Frame_Joint local Y rotation relative to its imported joint orientation |
| DriveTrain_Curve.rotateX | DriveTrain_Joint local X rotation; add the change to left pedal local X and subtract it from right pedal local X |
| LeftPedal_Curve.rotateX | LeftPedal_Joint local X rotation |
| RightPedal_Curve.rotateX | RightPedal_Joint local X rotation |
| CTRL_PIDBalanceControl_X/ctrl.translateY | PIDBalanceControl local Y; source expression multiplies centimeters by 100, so the numeric control value equals the output in Unity meters |
| CTRL_PIDBalanceControl_Z/ctrl.translateY | PIDBalanceControl local Z, same conversion |

Studio's left pedal drivetrain compensation is intentionally inverted relative to the original shared subtraction, matching the requested left-side direction. Independent pedal control edits are unchanged. The native `StudioPedalDirectionValidation` fixture checks incremental forward/reverse turns, crossing 180 degrees and returning to rest with the production control setter.

LeftPedal_Curve mouse dragging reverses the projected drag delta to match the requested direction. This input-only correction does not alter typed rotation values, imported keys, right-pedal dragging or drivetrain compensation.

`BikeCurves` follows the BMX control through a Maya parent constraint. `BackEndCurves` follows the frame. Unity displays the control curves on the driven joint hierarchy, so they follow the same root/frame movement without running Maya constraints. Six NURBS curves were sampled from the Maya file at 65 points each, converted from centimeters to meters and reflected into Unity coordinates. This is a specific mapping for this BMX skeleton, not a general Maya rig/expression importer. Wheel FK controls are additions, not copied Maya controls. Scooter mesh constraints and rider hand/foot constraints are not automatically recreated.

The source BMX_Curve has translateY = 35 cm, driving the Joints root. The source mesh parent constraint separately uses a -35 cm translation offset. The original curve extraction used the undriven Joints translation and therefore added an extra 35 cm in the displayed curves. The viewer now removes that offset in each joint's rest coordinate frame. Per-control handle layout offsets are saved separately from animated joints. Model-rest part attachment preserves the model origin while following the chosen joint, rather than snapping a whole frame mesh to the elevated joint pivot.

The rider and vehicle remain independent actor tracks. Import the separately baked rider and bike clips to reproduce their authored interaction, or use Actors / Attachment Constraints for explicit hand/foot attachments. Generic vehicle export filters out the Controls hierarchy and retains Joints animation.

## SDK meshes and data channels

An empty BMX outfit now previews the modeler's Full BMX assembly. The shared builder solves live anchors, paired parts, six stem bolts and anchor scale at rest. Those placements bind rigidly to the frame, steering, drivetrain, left/right pedal and front/rear wheel joints. Existing custom outfits remain in use. **Outfit / Load Full BMX from SDK** replaces the outfit with editable part entries; Undo restores the previous outfit. This uses the SDK models, not unsaved mesh edits in a modeling workspace.

PID X and Z appear in the animation viewport UI when the BMX actor is selected. The balance joint has no 3D picker. For the human, expand **Hand / foot target channels**, or select an IK limb, to edit its target weight and numeric ID. These are separate from the existing Studio IK/FK blend. Each data row has a **K** button; changes use Auto Key or remain an unkeyed preview when Auto Key is off.

Legacy names come from `D:/Character/Character Pipe/Skeleton.ma`: `LeftHand_IKWeight`, `RightHand_IKWeight`, `LeftFoot_IKWeight`, `RightFoot_IKWeight`, `LeftHand_IKTarget_ID`, `RightHand_IKTarget_ID`, `LeftFootIK_Target_ID`, and `RightFootIK_Target_ID`. Weight is local Y in 0–1 Unity units. Target ID N is local Y = N / 10, matching the runtime's `floor((Y + 0.05) * 10)` lookup. IDs hold until their next position key; weights interpolate. Humanoid export retains these data-joint translation curves alongside muscle curves. Editing an ID authors the runtime selector; preview contact still uses Studio's explicit attachment constraints.
