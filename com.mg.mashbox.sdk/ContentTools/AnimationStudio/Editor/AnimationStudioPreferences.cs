using System;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [Serializable] private sealed class AnimationConfiguration
        {
            public bool showAllActorControls = true;
            public bool muteAudio;
            public bool constraintsExpanded = true;
            public bool constraintKeepOffset = true;
            public bool showIKControls = true;
            public bool fadeRigControls = true;
            public bool hideInactiveControls;
            public bool rigDiagram = true;
            public bool rigToolbarExpanded = true;
            public bool localHandleSpace;
            public int generationMode;
            public string motionPrompt = "A person takes two steps forward, then waves with their right hand.";
            public string motionVideo = "";
            public float motionDuration = 3;
            public float motionStart;
            public int motionSeed = 42;
            public bool motionInPlace;
            public bool motionPlantFeet = true;
            public bool motionUpdateCurrent = true;
            public float motionContactSpeed = .25f;
            public float motionPoseBlend = .4f;
            public bool motionStepTransitions = true;
            public float motionStepDuration = .9f;
            public float motionStepLift = .09f;
            public float motionBodyWeight = .8f;
            public bool generationExpanded = true;
            public string imagePosePath = "";
            public bool imagePoseMirror;
            public bool imagePoseKeepPosition = true;
            public float imagePoseWeight = 1;
            public bool displayOptions;
            public int inspectorTab;
            public bool workspaceExpanded;
            public bool lockActorMeshes=true;
            public bool ikMode = true, poleMode, rotate, humanoidExport = true;
            public bool showSkeleton = true, showGroundGrid = true, autoKey, neutralShading = true;
            public float controlSize = 1.5f;
            public bool showPoles = true, showFKControls = true, inspectorExpanded = true;
        }
        internal void SaveAnimationConfiguration()
        {
            StudioPreferenceStore.SetString("Studio.Animation", JsonUtility.ToJson(new AnimationConfiguration {
                showAllActorControls = showAllActorControls,
                muteAudio = muteAudio,
                constraintsExpanded = constraintsExpanded,
                constraintKeepOffset = constraintKeepOffset,
                showIKControls = showIKControls,
                fadeRigControls = fadeRigControls,
                hideInactiveControls = hideInactiveControls,
                rigDiagram = rigDiagram,
                rigToolbarExpanded = rigToolbarExpanded,
                localHandleSpace = localHandleSpace,
                generationMode = generationMode,
                motionPrompt = motionPrompt,
                motionVideo = motionVideo,
                motionDuration = motionDuration,
                motionStart = motionStart,
                motionSeed = motionSeed,
                motionInPlace = motionInPlace,
                motionPlantFeet = motionPlantFeet,
                motionUpdateCurrent = motionUpdateCurrent,
                motionContactSpeed = motionContactSpeed,
                motionPoseBlend = motionPoseBlend,
                motionStepTransitions = motionStepTransitions,
                motionStepDuration = motionStepDuration,
                motionStepLift = motionStepLift,
                motionBodyWeight = motionBodyWeight,
                generationExpanded = generationExpanded,
                imagePosePath = imagePosePath,
                imagePoseMirror = imagePoseMirror,
                imagePoseKeepPosition = imagePoseKeepPosition,
                imagePoseWeight = imagePoseWeight,
                displayOptions = displayOptions,
                inspectorTab = inspectorTab,
                workspaceExpanded = workspaceExpanded,
                lockActorMeshes = lockActorMeshes,
                ikMode = ikMode, poleMode = poleMode, rotate = rotate, humanoidExport = humanoidExport,
                showSkeleton = showSkeleton, showGroundGrid = showGroundGrid, autoKey = autoKey, neutralShading = neutralShading,
                controlSize = controlSize, showPoles = showPoles, showFKControls = showFKControls, inspectorExpanded = inspectorExpanded
            }));
        }
        internal void LoadAnimationConfiguration()
        {
            string raw = StudioPreferenceStore.GetString("Studio.Animation");
            if (string.IsNullOrEmpty(raw)) return;
            try
            {
                var value = JsonUtility.FromJson<AnimationConfiguration>(raw);
                if (value == null) return;
                showAllActorControls = value.showAllActorControls;
                muteAudio = value.muteAudio;
                constraintsExpanded = value.constraintsExpanded;
                constraintKeepOffset = value.constraintKeepOffset;
                showIKControls = value.showIKControls;
                fadeRigControls = value.fadeRigControls;
                hideInactiveControls = value.hideInactiveControls;
                rigDiagram = value.rigDiagram;
                rigToolbarExpanded = value.rigToolbarExpanded;
                localHandleSpace = value.localHandleSpace;
                generationMode = value.generationMode;
                motionPrompt = value.motionPrompt;
                motionVideo = value.motionVideo;
                motionDuration = value.motionDuration;
                motionStart = value.motionStart;
                motionSeed = value.motionSeed;
                motionInPlace = value.motionInPlace;
                motionPlantFeet = value.motionPlantFeet;
                motionUpdateCurrent = value.motionUpdateCurrent;
                motionContactSpeed = value.motionContactSpeed;
                motionPoseBlend = value.motionPoseBlend;
                motionStepTransitions = value.motionStepTransitions;
                motionStepDuration = value.motionStepDuration;
                motionStepLift = value.motionStepLift;
                motionBodyWeight = value.motionBodyWeight;
                generationExpanded = value.generationExpanded;
                imagePosePath = value.imagePosePath;
                imagePoseMirror = value.imagePoseMirror;
                imagePoseKeepPosition = value.imagePoseKeepPosition;
                imagePoseWeight = value.imagePoseWeight;
                displayOptions = value.displayOptions;
                inspectorTab = value.inspectorTab;
                workspaceExpanded = value.workspaceExpanded;
                lockActorMeshes = value.lockActorMeshes;
                ikMode = value.ikMode; poleMode = value.poleMode; rotate = value.rotate; humanoidExport = value.humanoidExport;
                showSkeleton = value.showSkeleton; showGroundGrid = value.showGroundGrid; autoKey = value.autoKey; neutralShading = value.neutralShading;
                controlSize = float.IsNaN(value.controlSize) || float.IsInfinity(value.controlSize) ? 1.5f : Mathf.Clamp(value.controlSize, .1f, 10);
                showPoles = value.showPoles; showFKControls = value.showFKControls; inspectorExpanded = value.inspectorExpanded;
                if (viewport && !modelingMode) viewport.SetInspectorExpanded(inspectorExpanded);
            }
            catch (ArgumentException ex) { Debug.LogWarning("MashBox Studio could not load animation configuration: " + ex.Message); }
        }
    }
}
