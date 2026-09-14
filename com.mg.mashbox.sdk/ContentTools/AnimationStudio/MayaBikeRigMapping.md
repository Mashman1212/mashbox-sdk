# Bike rig mapping

Inspected source: `D:/Character/Vehicles/BMX/BMX Rigged.ma`, referenced by `Moto_Left.ma`. The source files are read only; no Maya scene edits or scripts were executed.

| Maya control | Unity joint behavior |
| --- | --- |
| BMX_Curve | Joints translation and rotation |
| Bars_Curve.rotateY | Bars_Joint local Y rotation relative to its imported joint orientation |
| Frame_Curve.rotateY | Frame_Joint local Y rotation relative to its imported joint orientation |
| DriveTrain_Curve.rotateX | DriveTrain_Joint local X rotation; subtract the change from both pedal local X rotations |
| LeftPedal_Curve.rotateX | LeftPedal_Joint local X rotation |
| RightPedal_Curve.rotateX | RightPedal_Joint local X rotation |
| CTRL_PIDBalanceControl_X/ctrl.translateY | PIDBalanceControl local Y; source expression multiplies centimeters by 100, so the numeric control value equals the output in Unity meters |
| CTRL_PIDBalanceControl_Z/ctrl.translateY | PIDBalanceControl local Z, same conversion |

`BikeCurves` follows the BMX control through a Maya parent constraint. `BackEndCurves` follows the frame. Unity displays the control curves on the driven joint hierarchy, so they follow the same root/frame movement without running Maya constraints. Six NURBS curves were sampled from the Maya file at 65 points each, converted from centimeters to meters and reflected into Unity coordinates. This is a specific mapping for this BMX skeleton, not a general Maya rig/expression importer. Wheel FK controls are additions, not copied Maya controls. Scooter mesh constraints and rider hand/foot constraints are not automatically recreated.

The source BMX_Curve has translateY = 35 cm, driving the Joints root. The source mesh parent constraint separately uses a -35 cm translation offset. The original curve extraction used the undriven Joints translation and therefore added an extra 35 cm in the displayed curves. The viewer now removes that offset in each joint's rest coordinate frame. Per-control handle layout offsets are saved separately from animated joints. Model-rest part attachment preserves the model origin while following the chosen joint, rather than snapping a whole frame mesh to the elevated joint pivot.

The rider and vehicle remain independent actor tracks. Import the separately baked rider and bike clips to reproduce their authored interaction, or use Actors / Attachment Constraints for explicit hand/foot attachments. Generic vehicle export filters out the Controls hierarchy and retains Joints animation.
