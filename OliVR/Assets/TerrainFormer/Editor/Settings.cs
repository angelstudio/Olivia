using System;
using System.Collections.Generic;
using System.IO;
using TinyJSON;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    public class Settings {
        internal const bool defaultShowGrid = false;
        internal const bool raycastOntoFlatPlaneDefault = true;
        internal const bool showSceneViewInformationDefault = true;
        internal const bool showCurrentHeightDefault = true;
        internal const bool displayProjectionModeDefault = true;
        internal const bool displayCurrentToolDefault = true;
        internal const bool displaySamplesSizeDefault = false;
        internal const bool useIconsForToolbarDefault = true;
        internal static readonly Color brushColourDefault = new Color(0.2f, 178f / 255f, 1f, 178f / 255f);
        internal const float brushSizeIncrementMultiplierDefault = 0.001f;
        internal const int brushPreviewSizeDefault = 48;
        internal const bool alwaysShowBrushSelectionDefault = false;
        internal const bool alwaysUpdateTerrainLODsDefault = false;
        
        [Exclude]
        internal static Action AlwaysShowBrushSelectionChanged;
        [Exclude]
        internal static Action BrushColourChanged;

        [Exclude]
        internal string path;

        [Include]
        internal Dictionary<TerrainMode, BrushSettings> brushSettings;

        [Exclude]
        internal AnimationCurve generateRampCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 0f, 1f));
        [Include]
        internal FauxKeyframe[] generateRampCurveFaux;
        [Include]
        internal float generateHeight = 5f;

        [Include]
        internal float setHeight = 10f;
        [Include]
        internal int boxFilterSize = 3;
        [Include]
        internal int smoothingIterations = 1;

        [Include]
        internal string mainDirectory;
        
        [Include]
        internal FlattenMode flattenMode = FlattenMode.Flatten;

        [Include]
        internal bool showGrid = defaultShowGrid;
        [Include]
        internal bool raycastOntoFlatPlane = raycastOntoFlatPlaneDefault;
        [Include]
        internal bool showSceneViewInformation = showSceneViewInformationDefault;
        [Include]
        internal bool showCurrentHeight = showCurrentHeightDefault;
        [Include]
        internal bool displayProjectionMode = displayProjectionModeDefault;
        [Include]
        internal bool displayCurrentTool = displayCurrentToolDefault;
        [Include]
        internal bool useIconsForToolbar = useIconsForToolbarDefault;
        [Include]
        private Color brushColour = brushColourDefault;
        internal Color BrushColour { 
            get {
                return brushColour;
            }
            set {
                if(brushColour == value) return;
                brushColour = value;
                if(BrushColourChanged != null) BrushColourChanged();
            }
        }

        [Include]
        internal float brushSizeIncrementMultiplier = brushSizeIncrementMultiplierDefault;
        [Include]
        internal int brushPreviewSize = brushPreviewSizeDefault;
        [Include]
        private bool alwaysShowBrushSelection = alwaysShowBrushSelectionDefault;
        [Exclude]
        internal bool AlwaysShowBrushSelection {
            get {
                return alwaysShowBrushSelection;
            }
            set {
                if(value == alwaysShowBrushSelection) return;
                alwaysShowBrushSelection = value;
                if(AlwaysShowBrushSelectionChanged != null) AlwaysShowBrushSelectionChanged();
            }
        }
        
        [Include]
        internal bool alwaysUpdateTerrainLODs = alwaysUpdateTerrainLODsDefault;

        public static Settings Create(string path) {
            Settings newSettings = new Settings();

            if(File.Exists(path)) {
                JSON.MakeInto(JSON.Load(File.ReadAllText(path)), out newSettings);
            } else {
                // Populate initial/default settings
                newSettings.brushSettings = new Dictionary<TerrainMode, BrushSettings>();
                newSettings.brushSettings.Add(TerrainMode.RaiseOrLower, new BrushSettings());

                newSettings.brushSettings.Add(TerrainMode.Smooth, new BrushSettings());
                newSettings.brushSettings[TerrainMode.Smooth].BrushSpeed = 2f;

                newSettings.brushSettings.Add(TerrainMode.SetHeight, new BrushSettings());
                newSettings.brushSettings[TerrainMode.SetHeight].BrushSpeed = 2f;

                newSettings.brushSettings.Add(TerrainMode.Flatten, new BrushSettings());
                newSettings.brushSettings[TerrainMode.Flatten].BrushSpeed = 2f;
            }
            newSettings.path = path;

            return newSettings;
        }

        public void Save() {
            try {
                // If the the setting's directory doesn't exist, return since we assume this means that Terrain Former has been moved.
                if(Directory.Exists(Path.GetDirectoryName(path)) == false) return;

                string json = JSON.Dump(this);
                File.WriteAllText(path, json);
            } catch(Exception e) {
                Debug.LogError(e.Message);
            }
        }

        [BeforeEncode]
        public void BeforeEncode() {
            // Update all fake represnetations of keyframes for serialization
            generateRampCurveFaux = CopyKeyframesToFauxKeyframes(generateRampCurve.keys);
            foreach(BrushSettings brushSetting in brushSettings.Values) {
                brushSetting.brushFalloffFauxFrames = CopyKeyframesToFauxKeyframes(brushSetting.brushFalloff.keys);
            }
        }

        /**
        * NOTE: If there is a new AnimationCurve that's not been saved yet, there will likely be a NullReferenceException. In the 
        * future we need to check for this, otherwise shipping the update to users will result in them being forced to delete their
        * settings file or to manually update it themselves.
        */
        [AfterDecode]
        public void AfterDecode() {
            // Copy all fake representations of keyframes and change them into AnimationCurves
            generateRampCurve = new AnimationCurve(CopyFauxKeyframesToKeyframes(generateRampCurveFaux));
            foreach(BrushSettings brushSetting in brushSettings.Values) {
                brushSetting.brushFalloff = new AnimationCurve(CopyFauxKeyframesToKeyframes(brushSetting.brushFalloffFauxFrames));
            }
        }

        private FauxKeyframe[] CopyKeyframesToFauxKeyframes(Keyframe[] keyframes) {
            FauxKeyframe[] newKeyframes = new FauxKeyframe[keyframes.Length];
            for(int i = 0; i < keyframes.Length; i++) {
                newKeyframes[i] = new FauxKeyframe(keyframes[i]);
            }
            return newKeyframes;
        }

        private Keyframe[] CopyFauxKeyframesToKeyframes(FauxKeyframe[] fauxKeyframes) {
            Keyframe[] newKeyframes = new Keyframe[fauxKeyframes.Length];
            for(int i = 0; i < fauxKeyframes.Length; i++) {
                newKeyframes[i] = new Keyframe(fauxKeyframes[i].time, fauxKeyframes[i].value);
                newKeyframes[i].inTangent = fauxKeyframes[i].inTangent;
                newKeyframes[i].outTangent = fauxKeyframes[i].outTangent;
                newKeyframes[i].tangentMode = fauxKeyframes[i].tangentMode;
            }
            return newKeyframes;
        }

        internal bool AreSettingsDefault() {
            return showGrid == defaultShowGrid && raycastOntoFlatPlane == raycastOntoFlatPlaneDefault &&
                showSceneViewInformation == showSceneViewInformationDefault && showCurrentHeight == showCurrentHeightDefault &&
                displayProjectionMode == displayProjectionModeDefault && displayCurrentTool == displayCurrentToolDefault &&
                useIconsForToolbar == useIconsForToolbarDefault && brushColour == brushColourDefault && brushPreviewSize == brushPreviewSizeDefault &&
                brushSizeIncrementMultiplier == brushSizeIncrementMultiplierDefault && alwaysShowBrushSelection == alwaysShowBrushSelectionDefault && 
                alwaysUpdateTerrainLODs == alwaysUpdateTerrainLODsDefault;
        }

        internal void RestoreDefaultSettings() {
            showGrid = defaultShowGrid;
            raycastOntoFlatPlane = raycastOntoFlatPlaneDefault;
            showSceneViewInformation = showSceneViewInformationDefault;
            showCurrentHeight = showCurrentHeightDefault;
            displayProjectionMode = displayProjectionModeDefault;
            displayCurrentTool = displayCurrentToolDefault;
            useIconsForToolbar = useIconsForToolbarDefault;
            BrushColour = brushColourDefault;
            brushSizeIncrementMultiplier = brushSizeIncrementMultiplierDefault;
            TerrainFormerInspector.Instance.UpdateBrushSizeIncrementIndex(brushSizeIncrementMultiplier);
            brushPreviewSize = brushPreviewSizeDefault;
            alwaysShowBrushSelection = alwaysShowBrushSelectionDefault;
            alwaysUpdateTerrainLODs = alwaysUpdateTerrainLODsDefault;
        }
    }
}
