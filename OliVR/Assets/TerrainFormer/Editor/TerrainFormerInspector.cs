using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/**
* IMPORTANT NOTE:
* Unity's terrain data co-ordinates are not setup as you might expect.
* Assuming the terrain is not rotated, this is the terrain strides vs axis:
* [0, 0]            = -X, -Z
* [width, 0]        = +X, -Z
* [0, height]       = -X, +Z
* [width, height]   = +X, +Z
* 
* This means that the that X goes out to the Z-Axis, and Y goes out into the X-Axis.
* This also means that a world space position such as the mouse position from a raycast needs 
*   its worldspace X-Axis position mapped to Z, and the worldspace Y-Axis mapped to X
*/

namespace JesseStiller.TerrainFormerExtension {
    [CustomEditor(typeof(TerrainFormer))]
    internal class TerrainFormerInspector : Editor {
        internal static TerrainFormerInspector Instance;
        private TerrainFormer terrainFormer;
        
        /**
        * Caching the terrain brush is especially useful for RotateTemporaryBrushSamples. It would take >500ms when accessing the terrain brush
        * through the property. Using it in when it's been cached makes roughly a 10x speedup and doesn't allocated ~3 MB of garbage.
        */
        private TerrainBrush cachedTerrainBrush;
        private float[,] temporarySamples;

        // Reflection fields
        private object unityTerrainInspector;
        private Type unityTerrainInspectorType;
        private PropertyInfo unityTerrainSelectedItem;
        private static PropertyInfo guiUtilityTextFieldInput;

        private string texturesDirectory;
        private string shadersDirectory;
        private string editorDirectory;

        internal static Settings settings;
                
        // Flatten fields
        private float flattenHeight = -1f;
        
        // States and Information
        private int lastHeightmapResolultion;
        private bool isSelectingBrush = false;
        private bool falloffChanged;

        [Flags]
        private enum SamplesDirty {
            None = 0,
            InspectorTexture = 1,
            ProjectorTexture = 2,
            BrushSamples = 4,
        }
        private SamplesDirty samplesDirty = SamplesDirty.None;

        private float[,] heights;
        private float[,] newHeights;

        private int brushSizePixels;

        // Gizmos
        private GameObject gridPlane;
        private Material gridPlaneMaterial;

        // Projector and cursor fields
        private GameObject brushProjectorGameObject;
        private Projector brushProjector;
        private Material brushProjectorMaterial;
        private GameObject topPlaneGameObject;
        private Material topPlaneMaterial;

        private Texture2D brushProjectorTexture;

        // Brush fields
        private const float minBrushSpeed = 0.02f;
        private const float maxBrushSpeed = 2f;
        private const float minSpacingBounds = 0.1f;
        private const float maxSpacingBounds = 30f;
        private const float minRandomOffset = 0.001f;
        private const float minRandomRotationBounds = -180f;
        private const float maxRandomRotationBounds = 180f;
        private BrushCollection brushCollection;
        private int brushSizeIncrementIndex = 0; 
        private readonly string[] brushSizeIncrementLabels = new string[] { "0.05%", "0.1%", "0.2%", "0.5%" };
        private readonly float[] brushSizeIncrementValues = new float[] { 0.0005f, 0.001f, 0.002f, 0.005f };

        // Terrain fields
        private static readonly int terrainEditorHash = "TerrainFormerEditor".GetHashCode(); // A unique ID used for the TerrainEditor windows' events
        private GameObject terrainGameObject;
        private Terrain terrain;
        private TerrainData terrainData;
        private Collider terrainCollider;
        private string terrainPath;

        // The first mode in order from left to right that is not a scultping tool.
        private readonly TerrainMode FirstNonScultpingTerrainMode = TerrainMode.Generate;

        // Mode fields
        private static Texture2D[] modeIcons;
        private static readonly string[] modeNames = new string[] {
            "Raise/Lower",
            "Smooth",
            "Set Height",
            "Flatten",
            "Generate",
            "Settings",
        };

        // Mouse related fields
        private bool mouseIsDown;
        private Vector2 mousePosition = new Vector2(); // The current screen-space position of the mouse. This position is used for raycasting
        private Vector2 lastMousePosition;
        private Vector3 lastWorldspaceMousePosition;
        private Vector3 lastClickPosition; // The point of the terrain the mouse clicked on
        private float mouseSpacingDistance = 0f;
        private float currentTotalMouseDelta = 0f;
        internal float CurrentTotalMouseDelta {
            get {
                return currentTotalMouseDelta;
            }
        }

        // Styles
        private GUIStyle largeBoldLabel;
        private GUIStyle showBehaviourFoldoutStyle;
        private GUIStyle sceneViewInformationStyle;
        private GUIStyle brushNameAlwaysShowBrushSelectionStyle;
        private GUIStyle gridListStyle;

        // GUI Contents
        private static readonly GUIContent smoothAllTerrainContent = new GUIContent("Smooth Entirety", "Smooths the entirety of the terrain based on the smoothing settings.");
        private static readonly GUIContent boxFilterSizeContent = new GUIContent("Smooth Radius", "Sets the number of adjacent terrain segments that are taken into account when smoothing " +
                "each segment. A higher value will more quickly smooth the area to become almost flat, but it may slow down performance while smoothing.");
        private static readonly GUIContent smoothingIterationsContent = new GUIContent("Smooth Iterations", "Sets how many times the entire terrain will be smoothed. (This setting only " +
                "applies to the Smooth Entirety button).");
        private static readonly GUIContent flattenModeContent = new GUIContent("Flatten Mode", "Sets the mode of flattening that will be used.\n- Flatten: Terrain higher than the current " +
                "click location height will be set to the click location height.\n- Bridge: The entire terrain will be set to the click location height.\n- Extend: Terrain lower than the current " +
                "click location height wil be set to the click location height.");
        private static readonly GUIContent showBrushGridContent = new GUIContent("Show Brush Grid", "Sets whether or not a grid will be visible while sculpting");
        private static readonly GUIContent raycastModeLabelContent = new GUIContent("Sculpt Onto", "Sets the way terrain will be sculpted.\n- Plane: Sculpting will be projected onto a plane " +
                "that's located where you initially left-clicked at.\n- Terrain: Sculpting will be projected onto the terrain.");
        private static readonly GUIContent toolbarStyleContent = new GUIContent("Toolbar Style");
        private static readonly GUIContent brushSizeIncrementContent = new GUIContent("Brush Size Increment", "Sets the percent of the terrain size that will be added/subtracted from the " +
                "brush size while using the brush size increment/decrement shortcuts. (Eg, a value of 2% with a terrain size of 512 will increment 10.54 [2% of 512]).");
        private static readonly GUIContent alwaysUpdateTerrainLODsContent = new GUIContent("Always Update Terrain LOD", "Sets whether or not the terrain's level-of-details (LOD) will be updated " +
                "every time the terrain is updated. This is especially useful when your computer is heavily GPU-bound or when painting across large amounts of terrain.");
        private static readonly GUIContent alwaysShowBrushSelectionContent = new GUIContent("Always Show Brush Selection", "Sets whether or not the brush selection control will be expanded " +
                "in the general brush settings area.");

		private static readonly string[] raycastModes = { "Plane", "Terrain" };
        private static readonly string[] toolbarStyles = { "Icon", "Text" };
        private static readonly string[] previewSizesContent = new string[] { "32px", "48px", "64px" };
        private static readonly int[] previewSizeValues = new int[] { 32, 48, 64 };

        private float randomSpacing;

        private TerrainCommand currentCommand;
        
        internal BrushSettings CurrentBrushSettings {
            get {
                if(CurrentMode == TerrainMode.None || CurrentMode > FirstNonScultpingTerrainMode) return null;
                return settings.brushSettings[CurrentMode];
            }
        }
        
        private static SavedInt currentMode;
        private TerrainMode CurrentMode {
            get {
                if(Tools.current == Tool.None) {
                    return (TerrainMode)currentMode.Value;
                } else { 
                    return TerrainMode.None;
                }
            }
            set {
                if(value == CurrentMode) return;
                
                if(value != TerrainMode.None) Tools.current = Tool.None;

                // If the built-in Unity tools were active, make them inactive by setting their mode to None (-1)
                if((int)unityTerrainSelectedItem.GetValue(unityTerrainInspector, null) != -1) {
                    unityTerrainSelectedItem.SetValue(unityTerrainInspector, -1, null);

                    // Update the heights of the terrain editor in case they were edited in the Unity terrain editor
                    heights = terrainData.GetHeights(0, 0, terrainData.heightmapWidth, terrainData.heightmapHeight);
                }
                
                currentMode.Value = (int)value;
            }
        }
        
        internal float[,] BrushSamplesWithSpeed {
            get {
                return brushCollection.brushes[CurrentBrushSettings.SelectedBrush].samplesWithSpeed;
            }
        }

        private TerrainBrush CurrentBrush {
            get {
                if(CurrentBrushSettings == null) return null;
                if(brushCollection.brushes.ContainsKey(CurrentBrushSettings.SelectedBrush) == false) {
                    CurrentBrushSettings.SelectedBrush = brushCollection.brushes.Keys.First();
                }

                return brushCollection.brushes[CurrentBrushSettings.SelectedBrush];
            }
        }

        private float MaxBrushSize {
            get {
                return Mathf.Max(terrainData.size.x, terrainData.size.z);
            }
        }

        private float MinBrushSize {
            get {
                return (MaxBrushSize / terrainData.heightmapResolution) * 3;
            }
        }
        
        internal AnimationCurve BrushFalloff {
            get {
                return CurrentBrushSettings.brushFalloff;
            }
            set {
                CurrentBrushSettings.brushFalloff = value;
            }
        }
        
        // Simple initialization logic that doesn't rely on any secondary data
        void OnEnable() {
            Instance = this;
            terrainFormer = (TerrainFormer)target;
            
            // Look for the main directory by finding the path of the Terrain Former script.
            string terrainFormerScriptPath = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(terrainFormer));
            string mainDirectory;
            if(File.Exists(terrainFormerScriptPath)) {
                mainDirectory = Path.GetDirectoryName(terrainFormerScriptPath) + "/";
                UpdateSubDirectoryPaths(mainDirectory);
                if(VerifyMainDirectory() == false) {
                    Debug.LogError("Couldn't find the main Terrain Former folder or the folder doesn't contain all necessary directories.");
                    return;
                }
            } else {
                Debug.LogError("Couldn't find the main Terrain Former script (TerrainFormer.cs)");
                return;
            }

            string settingsPath = Path.Combine(Application.dataPath.Remove(Application.dataPath.Length - 7, 7), Path.Combine(mainDirectory, "Settings.tf"));
            settings = Settings.Create(settingsPath);
            settings.mainDirectory = mainDirectory;

            currentMode = new SavedInt("TerrainFormer/CurrentMode", -1);
            // If there is a Unity tool selected, make sure Terrain Former's mode is set to None
            if(Tools.current != Tool.None) {
                currentMode.Value = (int)TerrainMode.None;
            }
            currentMode.ValueChanged += CurrentModeChanged;
            
            BrushSettings.UseFalloffForCustomBrushesChanged += UseFalloffForCustomBrushesChanged;
            BrushSettings.UseAlphaFalloffChanged += UseAlphaFalloffChanged;
            BrushSettings.BrushSpeedChanged += BrushSpeedChanged;
            BrushSettings.BrushRoundnessChanged += BrushRoundnessChanged;
            BrushSettings.BrushAngleDeltaChanged += BrushAngleDeltaChanged;
            BrushSettings.SelectedBrushChanged += SelectedBrushValueChanged;
            BrushSettings.RandomOffsetChanged += RandomOffsetChanged;
            BrushSettings.RandomRotationChanged += RandomRotationChanged;
            BrushSettings.RandomSpacingChanged += RandomSpacingChanged;
            Settings.AlwaysShowBrushSelectionChanged += AlwaysShowBrushSelectionValueChanged;
            Settings.BrushColourChanged += BrushColourChanged;
            
            BrushSettings.BrushSizeChanged += BrushSizeChanged;
            
            UpdateBrushSizeIncrementIndex(settings.brushSizeIncrementMultiplier);

            Undo.undoRedoPerformed += UndoRedoPerformed;
            
            // Re-initialize just in case variables were lost during an assembly reload
            Initialize(true);
        }
        
        /**
        * Initialize contains logic that is intrinsically tied to this entire terrain tool. If any of these fields and 
        * other things are missing, then the entire editor will break. If they are missing, an attempt will be made 
        * every GUI frame to find them.
        * Returns true if the initialization was successful or if everything is already initialized, false otherwise.
        * If the user moves Terrain Former's Editor folder away and brings it back, the brushProjector dissapears. This is why
        * it is checked for on Initialization.
        */
        private bool Initialize(bool forceReinitialize = false) {
            if(forceReinitialize == false && terrainFormer != null && terrain != null && terrainData != null && brushProjector != null) {
                return true;
            }

            /**
            * If there is more than one object selected, do not even bother initializing. This also fixes a strange 
            * exception occurance when two terrains or more are selected; one with Terrain Former and one without
            */
            if(Selection.objects.Length != 1) return false;
            
            if(terrainFormer == null) return false;
            
            terrainGameObject = terrainFormer.gameObject;
            terrain = terrainGameObject.GetComponent<Terrain>();

            // Make sure there is only ever one Terrain Former
            TerrainFormer[] terrainFormerInstances = terrainGameObject.GetComponents<TerrainFormer>();
            if(terrainFormerInstances.Length > 1) {
                for(int i = terrainFormerInstances.Length - 1; i > 0; i--) {
                    DestroyImmediate(terrainFormerInstances[i]);
                }
                EditorUtility.DisplayDialog("Terrain Former", "You can't add multiple Terrain Former instances to a single Terrain object.", "Close");
                return false;
            }

            if(terrain == null) return false;
            if(terrain.terrainData == null) return false;

            terrainData = terrain.terrainData;
            
            lastHeightmapResolultion = terrainData.heightmapResolution;
            
            terrainCollider = terrainFormer.gameObject.GetComponent<Collider>();

            heights = terrainData.GetHeights(0, 0, terrainData.heightmapWidth, terrainData.heightmapHeight);
            
            brushCollection = new BrushCollection();
            
            CreateProjector();

            CreateGridPlane();

            /**
            * On startup, the current mode is assigned to a new value but the ValueChanged event is not fired since other parts have not
            * been initialized (ie; the projector). After everything has been initialized call CurrentModeChanged to update parameters such
            * as brush size pixels and update the brush textures.
            */
            CurrentModeChanged();

            modeIcons = new Texture2D[] {
                AssetDatabase.LoadAssetAtPath<Texture2D>(settings.mainDirectory + "Textures/Icons/RaiseLower.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(settings.mainDirectory + "Textures/Icons/Smooth.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(settings.mainDirectory + "Textures/Icons/SetHeight.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(settings.mainDirectory + "Textures/Icons/Flatten.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(settings.mainDirectory + "Textures/Icons/Generate.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(settings.mainDirectory + "Textures/Icons/Settings.png"),
            };

            /**
            * Get an instance of the built-in Unity Terrain Inspector so we can override the selectedTool property
            * when the user selects a different tool in Terrain Former. This makes it so the user can't accidentally
            * use two terain tools at once (eg. Unity Terrain's raise/lower, and Terrain Former's raise/lower)
            */
            unityTerrainInspectorType = Assembly.GetAssembly(typeof(Editor)).GetType("UnityEditor.TerrainInspector");
            unityTerrainSelectedItem = unityTerrainInspectorType.GetProperty("selectedTool", BindingFlags.NonPublic | BindingFlags.Instance);

            UnityEngine.Object[] terrainInspectors = Resources.FindObjectsOfTypeAll(unityTerrainInspectorType);
            // Iterate through each Unity terrain inspector to look for one that matches this object
            foreach(UnityEngine.Object inspector in terrainInspectors) {
                Editor inspectorAsType = inspector as Editor;
                GameObject inspectorGameObject = ((Terrain)inspectorAsType.target).gameObject;

                if(inspectorGameObject == null) continue;

                if(inspectorGameObject == terrainGameObject) {
                    unityTerrainInspector = inspector;
                }
            }

            if(unityTerrainInspector == null) {
                Debug.LogError("Terrain Former was unable to find the Unity terrain inspector for this GameObject.");
                return false;
            }

            guiUtilityTextFieldInput = typeof(GUIUtility).GetProperty("textFieldInput", BindingFlags.NonPublic | BindingFlags.Static);

            AssetWatcher.OnAssetsImported += OnAssetsImported;
            AssetWatcher.OnAssetsMoved += OnAssetsMoved;
            AssetWatcher.OnAssetsDeleted += OnAssetsDeleted;

            terrainPath = AssetDatabase.GetAssetPath(terrainData);
            
            return true;
        }

        void OnDisable() {
            settings.Save();

            brushCollection = null;
            
            // Destroy all gizmos
            if(brushProjectorGameObject != null) {
                DestroyImmediate(brushProjectorMaterial);
                DestroyImmediate(brushProjectorGameObject);
                DestroyImmediate(topPlaneGameObject);
                brushProjector = null;
            }

            if(gridPlane != null) {
                DestroyImmediate(gridPlaneMaterial);
                DestroyImmediate(gridPlane.gameObject);
                gridPlaneMaterial = null;
                gridPlane = null;
            }

            Undo.undoRedoPerformed -= UndoRedoPerformed;

            AssetWatcher.OnAssetsImported -= OnAssetsImported;
            AssetWatcher.OnAssetsMoved -= OnAssetsMoved;
            AssetWatcher.OnAssetsDeleted -= OnAssetsDeleted;

            currentMode.ValueChanged -= CurrentModeChanged;

            BrushSettings.UseFalloffForCustomBrushesChanged -= UseFalloffForCustomBrushesChanged;
            BrushSettings.UseAlphaFalloffChanged -= UseAlphaFalloffChanged;
            BrushSettings.BrushSpeedChanged -= BrushSpeedChanged;
            BrushSettings.BrushRoundnessChanged -= BrushRoundnessChanged;
            BrushSettings.BrushAngleDeltaChanged -= BrushAngleDeltaChanged;
            BrushSettings.SelectedBrushChanged -= SelectedBrushValueChanged;
            BrushSettings.RandomOffsetChanged -= RandomOffsetChanged;
            BrushSettings.RandomRotationChanged -= RandomRotationChanged;
            BrushSettings.RandomSpacingChanged -= RandomSpacingChanged;
            Settings.AlwaysShowBrushSelectionChanged -= AlwaysShowBrushSelectionValueChanged;
            Settings.BrushColourChanged -= BrushColourChanged;

            BrushSettings.BrushSizeChanged -= BrushSizeChanged;
        }

        #region EditorMessages
        public override void OnInspectorGUI() {
            // Stop if the initialization was unsuccessful
            if(terrain == null) {
                EditorGUILayout.HelpBox("Missing terrain. Make sure that Terrain Former is assigned to an object with a Terrain component.", MessageType.Error);
                return;
            } else if(terrainData == null) {
                EditorGUILayout.HelpBox("Missing terrain data asset. Reassign the terrain asset in the Unity Terrain component.", MessageType.Error);
                return;
            } else if(terrainCollider == null) {
                EditorGUILayout.HelpBox("There is no terrain collider attached to this object, please add one since Terrain Former relies upon the terrain collider.", MessageType.Warning);
                return;
            } else if(target == null) {
                EditorGUILayout.HelpBox("There is no target object. Make sure Terrain Former is a component of a terrain object.", MessageType.Error);
                return;
            } else if(VerifyMainDirectory() == false) {
                EditorGUILayout.HelpBox("Terrain Former can't find its main directory. Please make sure that all contents of Terrain Former are contained in the same folder. Reimport Terrain Former if need be.", MessageType.Error);
                return;
            }

            if(Initialize() == false) return;

            if(largeBoldLabel == null) {
                largeBoldLabel = new GUIStyle(EditorStyles.largeLabel);
                largeBoldLabel.fontSize = 13;
                largeBoldLabel.fontStyle = FontStyle.Bold;
                largeBoldLabel.alignment = TextAnchor.MiddleCenter;
            }
            if(showBehaviourFoldoutStyle == null) {
                showBehaviourFoldoutStyle = new GUIStyle(GUI.skin.GetStyle("foldout"));
                showBehaviourFoldoutStyle.fontStyle = FontStyle.Bold;
            }
            if(brushNameAlwaysShowBrushSelectionStyle == null) {
                brushNameAlwaysShowBrushSelectionStyle = new GUIStyle(GUI.skin.label);
                brushNameAlwaysShowBrushSelectionStyle.alignment = TextAnchor.MiddleRight;
            }
            if(gridListStyle == null) {
                gridListStyle = GUI.skin.GetStyle("GridList");
            }

            EditorGUIUtility.labelWidth = 130f;
            
            CheckKeyboardShortcuts(Event.current);

            if(Event.current.type == EventType.MouseUp || Event.current.type == EventType.KeyUp) {
                UpdateDirtyBrushSamples();
            }

            // Check if the user modified the heightmap resolution. If so, update the brush samples
            int heightmapResolution = terrainData.heightmapResolution;
            if(lastHeightmapResolultion != -1 && lastHeightmapResolultion != heightmapResolution) {
                BrushSizeChanged();
            }
            lastHeightmapResolultion = heightmapResolution;
            
            /** 
            * Get the current Unity Terrain Inspector mode, and set the Terrain Former mode to none if the Unity Terrain
            * Inspector mode is not none.
            */
            if(CurrentMode != TerrainMode.None) {
                int unityTerrainMode = (int)unityTerrainSelectedItem.GetValue(unityTerrainInspector, null);
                // If the mode is not "None" (-1), then the Terrain Former mode must be set to none
                if(unityTerrainMode != -1) {
                    currentMode.Value = (int)TerrainMode.None;
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Space(-8f);

            TerrainMode newMode;
            if(settings.useIconsForToolbar) { 
                newMode = (TerrainMode)GUILayout.Toolbar((int)CurrentMode, modeIcons, GUILayout.MinWidth(224f), GUILayout.Height(22f));
            } else { 
                newMode = (TerrainMode)GUILayout.Toolbar((int)CurrentMode, modeNames, GUILayout.MinWidth(224f), GUILayout.Height(22f));
            }
            
            if(newMode != CurrentMode) {
                CurrentMode = newMode;
                SceneView.RepaintAll();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if(CurrentMode == TerrainMode.None) {
                return;
            } else {
                GUILayout.Label(modeNames[(int)CurrentMode], largeBoldLabel);
            }

            switch(CurrentMode) {
                case TerrainMode.RaiseOrLower:
                    break;
                case TerrainMode.Smooth:
                    settings.boxFilterSize = EditorGUILayout.IntSlider(boxFilterSizeContent, settings.boxFilterSize, 1, 5);

                    GUILayout.Label("Smooth Entirety", EditorStyles.boldLabel);

                    GUIUtilities.DrawFillAndRightControl(
                        fillControl: (r) => {
                            settings.smoothingIterations = EditorGUI.IntSlider(r, smoothingIterationsContent, settings.smoothingIterations, 1, 10);
                        },
                        rightControl: (r) => {
                            r.yMin -= 2f;
                            r.yMax += 2f;
                            if(GUI.Button(r, smoothAllTerrainContent)) {
                                SmoothAll();
                            }
                        },
                        rightControlWidth: 110
                    );
                break;
                    case TerrainMode.SetHeight:
                    GUIUtilities.DrawFillAndRightControl(
                        fillControl: (r) => {
                            settings.setHeight = EditorGUI.Slider(r, "Set Height", settings.setHeight, 0f, terrainData.size.y);
                        },
                        rightControl: (r) => {
                            r.yMax += 2;
                            r.yMin -= 2;
                            if(GUI.Button(r, "Apply to Terrain")) {
                                FlattenTerrain(settings.setHeight);
                            }
                        }, 
                        rightControlWidth: 111
                    );

                    break;
                case TerrainMode.Flatten:
                    settings.flattenMode = (FlattenMode)EditorGUILayout.EnumPopup(flattenModeContent, settings.flattenMode);

                    break;
                case TerrainMode.Generate:
                    AnimationCurve newCurve = EditorGUILayout.CurveField("Falloff", new AnimationCurve(settings.generateRampCurve.keys));
                    if(Utilities.AnimationCurvesEqual(newCurve, settings.generateRampCurve) == false) {
                        settings.generateRampCurve = newCurve;
                        Utilities.ClampAnimationCurve(settings.generateRampCurve);
                    }

                    settings.generateHeight = EditorGUILayout.Slider("Height", settings.generateHeight, 0f, terrainData.size.y);

                    using(new GUIUtilities.Horizontal()) {
                        GUILayout.Space(Screen.width * 0.1f);
                        if(GUILayout.Button("Create Linear Ramp", GUILayout.Height(20f))) {
                            CreateRampCurve(settings.generateHeight);
                        }
                        if(GUILayout.Button("Create Circular Ramp", GUILayout.Height(20f))) {
                            Undo.RegisterCompleteObjectUndo(terrainData, "Created Circular Ramp");
                            CreateCircularRampCurve(settings.generateHeight);
                        }
                        GUILayout.Space(Screen.width * 0.1f);
                    }

                    break;
                case TerrainMode.Settings:
                    EditorGUIUtility.labelWidth = 186f;
                    GUILayout.Label("General", EditorStyles.boldLabel);

                    // Raycast Onto Plane
                    Rect raycastModeRect = EditorGUILayout.GetControlRect();
                    Rect raycastModeLabelRect = new Rect(raycastModeRect);
                    Rect raycastModeToolbarRect = EditorGUI.PrefixLabel(raycastModeLabelRect, raycastModeLabelContent);

                    int raycastModeIndex = settings.raycastOntoFlatPlane == true ? 0 : 1;
                    int selectedRaycastMode = GUI.Toolbar(raycastModeToolbarRect, raycastModeIndex, raycastModes, EditorStyles.radioButton);
                    settings.raycastOntoFlatPlane = selectedRaycastMode == 0 ? true : false;
                    
                    Rect brushSizeIncrementRect = EditorGUILayout.GetControlRect();
                    Rect brushSizeIncrementToolbarRect = EditorGUI.PrefixLabel(brushSizeIncrementRect, brushSizeIncrementContent);
                    brushSizeIncrementToolbarRect.xMax -= 2;
                    brushSizeIncrementIndex = GUI.Toolbar(brushSizeIncrementToolbarRect, brushSizeIncrementIndex, brushSizeIncrementLabels, EditorStyles.radioButton);
                    settings.brushSizeIncrementMultiplier = brushSizeIncrementValues[brushSizeIncrementIndex];

                    settings.showGrid = EditorGUILayout.Toggle(showBrushGridContent, settings.showGrid);

                    settings.BrushColour = EditorGUILayout.ColorField("Editor Brush Colour", settings.BrushColour);

                    settings.alwaysUpdateTerrainLODs = EditorGUILayout.Toggle(alwaysUpdateTerrainLODsContent, settings.alwaysUpdateTerrainLODs);

                    GUILayout.Label("User Interface", EditorStyles.boldLabel);

                    // Use Icon For Toolbar
                    Rect toolbarStyleRect = EditorGUILayout.GetControlRect();
                    Rect toolbarStyleLabelRect = new Rect(toolbarStyleRect);
                    Rect toolbarStyleToolbarRect = EditorGUI.PrefixLabel(toolbarStyleLabelRect, toolbarStyleContent);
                    int toolbarStyleIndex = settings.useIconsForToolbar == true ? 0 : 1;
                    int selectedToolbarStyleIndex = GUI.Toolbar(toolbarStyleToolbarRect, toolbarStyleIndex, toolbarStyles, EditorStyles.radioButton);
                    settings.useIconsForToolbar = selectedToolbarStyleIndex == 0 ? true : false;
                    
                    settings.AlwaysShowBrushSelection = EditorGUILayout.Toggle(alwaysShowBrushSelectionContent, settings.AlwaysShowBrushSelection);

                    Rect previewSizeRect = EditorGUILayout.GetControlRect();
                    Rect previewSizeToolbarRect = EditorGUI.PrefixLabel(previewSizeRect, new GUIContent("Brush Preview Size"));
                    previewSizeToolbarRect.xMax -= 2;
                    settings.brushPreviewSize = EditorGUI.IntPopup(previewSizeToolbarRect, settings.brushPreviewSize, previewSizesContent, previewSizeValues);
                    
                    GUILayout.Space(2f);

                    settings.showSceneViewInformation = EditorGUILayout.BeginToggleGroup("Show Scene View Information", settings.showSceneViewInformation);
                    GUI.enabled = settings.showSceneViewInformation;
                    settings.showCurrentHeight = EditorGUILayout.Toggle("Display Current Height", settings.showCurrentHeight);
                    settings.displayProjectionMode = EditorGUILayout.Toggle("Display Sculpt Onto", settings.displayProjectionMode);
                    settings.displayCurrentTool =  EditorGUILayout.Toggle("Display Current Tool", settings.displayCurrentTool);
                    EditorGUILayout.EndToggleGroup();
                    GUI.enabled = true;
                    
                    GUILayout.Label("Shortcuts", EditorStyles.boldLabel);
                    foreach(Shortcut shortcut in Shortcut.Shortcuts.Values) {
                        shortcut.DoShortcutField();
                    }
                    
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    // If all the settings are at their default value, disable the "Restore Defaults"
                    bool shortcutsNotDefault = false;
                    foreach(Shortcut shortcut in Shortcut.Shortcuts.Values) {
                        if(shortcut.Binding != shortcut.defaultBinding) {
                            shortcutsNotDefault = true;
                            break;
                        }
                    }
                    
                    if(settings.AreSettingsDefault() && shortcutsNotDefault == false) {
                        GUI.enabled = false;
                    }
                    if(GUILayout.Button("Restore Defaults", GUILayout.Width(120f), GUILayout.Height(20))) {
                        if(EditorUtility.DisplayDialog("Restore Defaults", "Are you sure you want to restore all settings to their defaults?", "Restore Defaults", "Cancel")) {
                            settings.RestoreDefaultSettings();

                            // Reset shortcuts to defaults
                            foreach(Shortcut shortcut in Shortcut.Shortcuts.Values) {
                                shortcut.waitingForInput = false;
                                shortcut.Binding = shortcut.defaultBinding;
                            }
                        }
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();

                    break;
                case TerrainMode.None:
                    break;
                default:
                    GUILayout.Label("The interface is not yet implemented.", EditorStyles.miniLabel);
                    break;
            }

            float lastLabelWidth = EditorGUIUtility.labelWidth;
            
            EditorGUILayout.Space();
            
            // General Brush Settings
            if(CurrentMode != TerrainMode.None && CurrentMode < FirstNonScultpingTerrainMode) {
                // Draw the brush selection menu
                if(settings.AlwaysShowBrushSelection || isSelectingBrush) {
                    EventType eventType = Event.current.type;

                    Rect brushesTitleRect = EditorGUILayout.GetControlRect();
                    GUI.Label(brushesTitleRect, settings.AlwaysShowBrushSelection ? "Brushes" : "Select Brush", EditorStyles.boldLabel);

                    if(settings.AlwaysShowBrushSelection) {
                        brushesTitleRect.xMin = brushesTitleRect.xMax - 300f;
                        GUI.Label(brushesTitleRect, CurrentBrush is ProceduralBrush ? "Procedural Brush" : CurrentBrush.name, brushNameAlwaysShowBrushSelectionStyle);
                    }
                    
                    if(brushCollection.PreviewTextures.Length != 0) {
                        GUILayout.BeginVertical(GUI.skin.box, GUILayout.MinHeight(16f));
                        float brushesPerRow = (Screen.width - 20f) / settings.brushPreviewSize;
                        float columns = Mathf.Ceil(brushCollection.PreviewTextures.Length / brushesPerRow);
                        
                        Rect selectionGridRect = GUILayoutUtility.GetAspectRect(brushesPerRow / columns);
                        
                        int currentBrushIndex = GetCurrentBrushIndex();

                        int newSelectedBrush = GUI.SelectionGrid(selectionGridRect, currentBrushIndex, brushCollection.PreviewTextures,
                            Mathf.FloorToInt(brushesPerRow), gridListStyle);
                        
                        if(eventType == EventType.MouseUp) {
                            // Exit the menu if either the user clicked on a different brush, or clicked anywhere in the selection grid
                            if(newSelectedBrush != currentBrushIndex || selectionGridRect.Contains(Event.current.mousePosition)) {
                                CurrentBrushSettings.SelectedBrush = brushCollection.brushes.Keys.ElementAt(newSelectedBrush);
                                
                                isSelectingBrush = false;
                            }
                        }
                        GUILayout.EndVertical();
                    } else {
                        EditorGUILayout.HelpBox("No custom textures have been found at " + BrushCollection.globalCustomBrushPath, MessageType.None);
                    }
                }
                if(settings.AlwaysShowBrushSelection || !isSelectingBrush) {
                    if(settings.AlwaysShowBrushSelection) {
                        GUILayout.Space(10f);
                    }
                    if(CurrentMode != TerrainMode.RaiseOrLower) {
                        GUILayout.Label("Brush", EditorStyles.boldLabel);
                    }

                    using(new GUIUtilities.Horizontal()) {
                        // Draw Brush Paramater Editor
                        using(new GUIUtilities.Vertical()) {
                            // HACK: Setting GUILayout.MinWidth because supplying "null" results in an exception
                            using(new GUIUtilities.Vertical(settings.AlwaysShowBrushSelection ? GUILayout.MinWidth(0f) : GUILayout.Width(Screen.width - 105f))) {
                                bool isBrushProcedural = CurrentBrush is ProceduralBrush;

                                CurrentBrushSettings.BrushSize = EditorGUILayout.Slider("Size", CurrentBrushSettings.BrushSize, MinBrushSize, MaxBrushSize);

                                CurrentBrushSettings.BrushSpeed = EditorGUILayout.Slider("Speed", CurrentBrushSettings.BrushSpeed, minBrushSpeed, maxBrushSpeed);
                                
                                falloffChanged = false;
                                
                                if(isBrushProcedural) {
                                    /**
                                    * I must create a new AnimationCurve based on the existing BrushFalloff keys; otherwise, 
                                    * Unity will automatically assign BrushFalloff to its new value, which would make the 
                                    * equality comparer always return true (meaning equal)
                                    */
                                    AnimationCurve newCurve = EditorGUILayout.CurveField("Falloff", new AnimationCurve(BrushFalloff.keys));
                                    if(Utilities.AnimationCurvesEqual(newCurve, BrushFalloff) == false) {
                                        BrushFalloff = newCurve;
                                        BrushFalloffChanged();
                                    }
                                } else {
                                    GUIUtilities.DrawFillAndRightControl(
                                        fillControl: (r) => {
                                            Rect falloffToggleRect = new Rect(r);
                                            falloffToggleRect.xMax = EditorGUIUtility.labelWidth;
                                            CurrentBrushSettings.UseFalloffForCustomBrushes = EditorGUI.Toggle(falloffToggleRect, CurrentBrushSettings.UseFalloffForCustomBrushes);

                                            Rect falloffToggleLabelRect = new Rect(falloffToggleRect);
                                            falloffToggleLabelRect.xMin += 15f;
                                            EditorGUI.PrefixLabel(falloffToggleLabelRect, new GUIContent("Falloff"));

                                            Rect falloffAnimationCurve = new Rect(r);
                                            falloffAnimationCurve.xMin = EditorGUIUtility.labelWidth + 14f;
                                            AnimationCurve newCurve = EditorGUI.CurveField(falloffAnimationCurve, new AnimationCurve(BrushFalloff.keys));

                                            if(Utilities.AnimationCurvesEqual(newCurve, BrushFalloff) == false) {
                                                BrushFalloff = newCurve;
                                                BrushFalloffChanged();
                                            }
                                        },
                                        rightControl: (r) => {
                                            using(new GUIUtilities.GUIEnabledBlock(CurrentBrushSettings.UseFalloffForCustomBrushes)) {
                                                Rect alphaFalloffLabelRect = new Rect(r);
                                                alphaFalloffLabelRect.xMin += 14;
                                                GUI.Label(alphaFalloffLabelRect, "Alpha");

                                                Rect alphaFalloffRect = new Rect(r);
                                                alphaFalloffRect.xMin--;
                                                CurrentBrushSettings.UseAlphaFalloff = EditorGUI.Toggle(alphaFalloffRect, CurrentBrushSettings.UseAlphaFalloff);
                                            }
                                        },
                                        rightControlWidth: 54
                                    );
                                }
                                
                                // We need to delay updating brush samples while changing falloff until changes have stopped for at least one frame
                                if(falloffChanged == false && samplesDirty != SamplesDirty.None) {
                                    UpdateDirtyBrushSamples();
                                }

                                if(isBrushProcedural == false && CurrentBrushSettings.UseFalloffForCustomBrushes == false) {
                                    GUI.enabled = false;
                                }

                                EditorGUI.indentLevel = 1;

                                CurrentBrushSettings.BrushRoundness = EditorGUILayout.Slider("Roundness", CurrentBrushSettings.BrushRoundness, 0f, 1f);

                                EditorGUI.indentLevel = 0;

                                if(isBrushProcedural == false && CurrentBrushSettings.UseFalloffForCustomBrushes == false) {
                                    GUI.enabled = true;
                                }

                                /**
                                * Custom Brush Angle
                                */
                                CurrentBrushSettings.BrushAngle = EditorGUILayout.Slider("Angle", CurrentBrushSettings.BrushAngle, -180f, 180f);
                                
                                CurrentBrushSettings.showBehaviourFoldout = EditorGUILayout.Foldout(CurrentBrushSettings.showBehaviourFoldout, "Behaviour", showBehaviourFoldoutStyle);
                                if(CurrentBrushSettings.showBehaviourFoldout) {                                    
                                    GUIUtilities.DrawToggleAndMinMax(
                                        toggleControl: (r) => {
                                            CurrentBrushSettings.useBrushSpacing = EditorGUI.ToggleLeft(r, "Random Spacing", CurrentBrushSettings.useBrushSpacing);
                                        },
                                        minMaxSliderControl: (r) => {
                                            float minBrushSpacing = CurrentBrushSettings.MinBrushSpacing;
                                            float maxBrushSpacing = CurrentBrushSettings.MaxBrushSpacing;
                                            EditorGUI.MinMaxSlider(r, ref minBrushSpacing, ref maxBrushSpacing, minSpacingBounds, maxSpacingBounds);
                                            CurrentBrushSettings.SetMinBrushSpacing(minBrushSpacing, minSpacingBounds);
                                            CurrentBrushSettings.SetMaxBrushSpacing(maxBrushSpacing, maxSpacingBounds);
                                        },
                                        minFloatControl: (r) => {
                                            CurrentBrushSettings.SetMinBrushSpacing(Mathf.Clamp(EditorGUI.FloatField(r, CurrentBrushSettings.MinBrushSpacing), minSpacingBounds, maxSpacingBounds),
                                                minSpacingBounds);
                                        },
                                        maxFloatControl: (r) => {
                                            CurrentBrushSettings.SetMaxBrushSpacing(Mathf.Clamp(EditorGUI.FloatField(r, CurrentBrushSettings.MaxBrushSpacing), minSpacingBounds, maxSpacingBounds),
                                                maxSpacingBounds);
                                        },
                                        enableFillControl: true,
                                        enableToggle: true
                                    );

                                    float maxRandomOffset = Mathf.Min(terrainData.heightmapWidth, terrainData.heightmapHeight) * 0.1f;
                                    GUIUtilities.DrawToggleAndFill(
                                        toggleControl: (r) => {
                                            CurrentBrushSettings.useRandomOffset = EditorGUI.ToggleLeft(r, "Random Offset", CurrentBrushSettings.useRandomOffset);
                                        },
                                        fillControl: (r) => {
                                            CurrentBrushSettings.SetRandomOffset(EditorGUI.Slider(r, CurrentBrushSettings.RandomOffset, minRandomOffset, maxRandomOffset), minRandomOffset, maxRandomOffset);
                                        },
                                        enableFillControl: true,
                                        enableToggle: true
                                    );
                                    
                                    GUIUtilities.DrawToggleAndMinMax(
                                        toggleControl: (r) => {
                                            CurrentBrushSettings.useRandomRotation = EditorGUI.ToggleLeft(r, "Random Rotation", CurrentBrushSettings.useRandomRotation);
                                        },
                                        minMaxSliderControl: (r) => {
                                            float minRandomRotation = CurrentBrushSettings.MinRandomRotation;
                                            float maxRandomRotation = CurrentBrushSettings.MaxRandomRotation;
                                            EditorGUI.MinMaxSlider(r, ref minRandomRotation, ref maxRandomRotation, minRandomRotationBounds, maxRandomRotationBounds);
                                            CurrentBrushSettings.SetMinRandomRotation(minRandomRotation, minRandomRotationBounds);
                                            CurrentBrushSettings.SetMaxRandomRotation(maxRandomRotation, maxRandomRotationBounds);
                                        }, 
                                        minFloatControl: (r) => {
                                            CurrentBrushSettings.SetMinRandomRotation(Mathf.Clamp(EditorGUI.FloatField(r, CurrentBrushSettings.MinRandomRotation), 
                                                minRandomRotationBounds, maxRandomRotationBounds), minRandomRotationBounds);
                                        }, 
                                        maxFloatControl: (r) => {
                                            CurrentBrushSettings.SetMaxRandomRotation(Mathf.Clamp(EditorGUI.FloatField(r, CurrentBrushSettings.MaxRandomRotation), 
                                                minRandomRotationBounds, maxRandomRotationBounds), maxRandomRotationBounds);
                                        },
                                        enableFillControl: true,
                                        enableToggle: true
                                    );
                                }
                            }
                        }

                        if(settings.AlwaysShowBrushSelection == false) {
                            GUILayout.Space(-4f);
                            using(new GUIUtilities.Vertical()) {
                                GUILayout.Space(-17f);
                                
                                GUIStyle miniBoldLabelCentered = new GUIStyle(EditorStyles.miniBoldLabel);
                                miniBoldLabelCentered.alignment = TextAnchor.MiddleCenter;
                                miniBoldLabelCentered.wordWrap = true;

                                using(new GUIUtilities.Horizontal()) {
                                    GUILayout.FlexibleSpace();
                                    GUILayout.Space(2f);

                                    if(CurrentBrush is ProceduralBrush) {
                                        GUILayout.Box("Procudural Brush", miniBoldLabelCentered, new GUILayoutOption[] { GUILayout.Width(85f), GUILayout.Height(30f) });
                                    } else {
                                        GUILayout.Box(CurrentBrush.name, miniBoldLabelCentered, new GUILayoutOption[] { GUILayout.Width(85f), GUILayout.Height(30f) });
                                    }

                                    
                                    GUILayout.FlexibleSpace();
                                }

                                GUILayout.Space(-5f);
                                
                                // Draw Brush Preview
                                using(new GUIUtilities.Horizontal()) {
                                    GUILayout.FlexibleSpace();
                                    if(GUILayout.Button(CurrentBrush.previewTexture, GUIStyle.none)) {
                                        ToggleSelectingBrush();
                                    }
                                    GUILayout.Space(3f);
                                    GUILayout.FlexibleSpace();
                                }
                                // Draw Select/Cancel Brush Selection Button
                                using(new GUIUtilities.Horizontal()) {
                                    GUILayout.FlexibleSpace();
                                    GUILayout.Space(3f);
                                    if(GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Height(18))) {
                                        ToggleSelectingBrush();
                                    }
                                    GUILayout.FlexibleSpace();
                                }
                            }
                        }
                    }
                }
            }

            EditorGUIUtility.labelWidth = lastLabelWidth;
        }

        private void BrushFalloffChanged() {
            Utilities.ClampAnimationCurve(BrushFalloff);

            falloffChanged = true;
            samplesDirty |= SamplesDirty.ProjectorTexture;

            if(settings.AlwaysShowBrushSelection) {
                brushCollection.UpdatePreviewTextures();
            } else {
                UpdateBrushInspectorTexture();
            }
        }

        private void ToggleSelectingBrush() {
            isSelectingBrush = !isSelectingBrush;

            // Update the brush previews if the user is now selecting brushes
            if(isSelectingBrush) {
                brushCollection.UpdatePreviewTextures();
            }
        }

        // Updates the position of the projector in the scene view
        public void OnPreSceneGUI() {
            if(Initialize() == false) return;
            if(CurrentMode == TerrainMode.None) return;
            if(Event.current.type != EventType.Repaint) return;
            if(terrainCollider == null) return;

            if((Event.current.control && mouseIsDown) == false) {
                UpdateProjector();
            }
        }

        void OnSceneGUI() {
            if(Initialize() == false) return;
            if(terrainCollider == null) return;
            
            // Get a unique ID for this editor so we can get events unique the editor's scope
            int controlId = GUIUtility.GetControlID(terrainEditorHash, FocusType.Passive);
            
            Event currentEvent = Event.current;
            EventType editorEventType = currentEvent.GetTypeForControl(controlId);

            /**
            * HACK: When dragging a slider or modifiying a value by dragging the mouse, dragging over to the SceneView
            * will cause no Mouse Up event to fire. Releasing the mouse in SceneView however results in a mouseMove event,
            * so we'll have to rely on that to fix this problem.
            */
            if(editorEventType == EventType.MouseMove && editorEventType != EventType.KeyDown && editorEventType != EventType.KeyUp) {
                UpdateDirtyBrushSamples();
            }
            
            if(editorEventType == EventType.MouseUp || editorEventType == EventType.KeyUp) {
                UpdateDirtyBrushSamples();
            }
            
            CheckKeyboardShortcuts(currentEvent);
            
            if(CurrentMode == TerrainMode.None || CurrentMode == TerrainMode.Generate || CurrentMode == TerrainMode.Settings) return;

            /**
            * Draw scene-view information
            */
            if(settings.showSceneViewInformation && (settings.showCurrentHeight || settings.displayCurrentTool || settings.displayProjectionMode)) {
                StringBuilder displayText = new StringBuilder();

                if(settings.displayCurrentTool) {
                    displayText.Append("\nTool:\t\t" + modeNames[currentMode.Value]);
                }
                if(settings.showCurrentHeight) {
                    float height;
                    displayText.Append("\nHeight:\t\t");
                    if(currentEvent.control && mouseIsDown) {
                        displayText.Append(lastClickPosition.y.ToString("0.00"));
                    } else if(GetTerrainHeightAtMousePosition(out height)) {
                        displayText.Append(height.ToString("0.00"));
                    } else {
                        displayText.Append("0.00");
                    }
                }
                if(settings.displayProjectionMode) {
                    displayText.Append("\nSculpt Onto:\t");
                    if(CurrentMode == TerrainMode.SetHeight || CurrentMode == TerrainMode.Flatten) {
                        displayText.Append("Plane (locked)");
                    } else {
                        displayText.Append(raycastModes[settings.raycastOntoFlatPlane ? 0 : 1]);
                    }
                }
                
                Handles.BeginGUI();
                if(sceneViewInformationStyle == null) {
                    sceneViewInformationStyle = new GUIStyle(GUI.skin.box);
                    sceneViewInformationStyle.alignment = TextAnchor.MiddleLeft;
                    sceneViewInformationStyle.padding = new RectOffset(10, 4, 3, 3);
                    sceneViewInformationStyle.border = new RectOffset(12, 12, 12, 12);
                    LoadSceneViewInformationPanel();
                }
                if(sceneViewInformationStyle.normal.background == null) {
                    LoadSceneViewInformationPanel();
                }

                string displayTextString = displayText.ToString();
                float textAreaHeight = sceneViewInformationStyle.CalcHeight(new GUIContent(displayTextString), 240f);
                GUILayout.BeginArea(new Rect(5f, 5f, 240f, textAreaHeight), displayTextString.Remove(0, 1), sceneViewInformationStyle);
                GUILayout.EndArea();
                Handles.EndGUI();
            }
            
            // Update mouse-related fields
            if(editorEventType == EventType.Repaint || currentEvent.isMouse) {
                if(mousePosition == Vector2.zero) {
                    lastMousePosition = currentEvent.mousePosition;
                } else {
                    lastMousePosition = mousePosition;
                }

                mousePosition = currentEvent.mousePosition;
                
                if(editorEventType == EventType.MouseDown) {
                    currentTotalMouseDelta = 0;
                } else {
                    currentTotalMouseDelta += mousePosition.y - lastMousePosition.y;
                }
            }

            // Only accept left clicks
            if(currentEvent.button != 0) return;
            
            switch(editorEventType) {
                // MouseDown will execute the same logic as MouseDrag
                case EventType.MouseDown:
                case EventType.MouseDrag:
                    /*
                    * Break if any of the following rules are true:
                    * 1) The event happening for this window is a MouseDrag event and the hotControl isn't this window
                    * 2) Alt + Click have been executed
                    * 3) The HandleUtllity finds a control closer to this control
                    */
                    if(editorEventType == EventType.MouseDrag &&
                        GUIUtility.hotControl != controlId ||
                        (Event.current.alt || currentEvent.button != 0) ||
                        HandleUtility.nearestControl != controlId) {
                        break;
                    }
                    if(currentEvent.type == EventType.MouseDown) {
                        /**
                        * To make sure the initial press down always sculpts the terrain while spacing is active, set 
                        * the mouseSpacingDistance to a high value to always activate it straight away
                        */
                        mouseSpacingDistance = float.MaxValue;
                        UpdateRandomSpacing();
                        GUIUtility.hotControl = controlId;
                    }
                    
                    // Update the lastClickPosition when the mouse has been pressed down
                    if(mouseIsDown == false) {
                        Vector3 hitPosition;
                        Vector2 uv;
                        if(Raycast(out hitPosition, out uv)) {
                            lastWorldspaceMousePosition = hitPosition;
                            lastClickPosition = hitPosition;
                            mouseIsDown = true;
                        }
                    }
                    
                    currentEvent.Use();
                    break;
                case EventType.MouseMove:
                    if(Raycast() == false) // Check if the preview brush is visible by raycasting
                        break;
                    HandleUtility.Repaint();
                    break;
                case EventType.MouseUp:
                    // Reset the hotControl to nothing as long as it matches the TerrainEditor controlID
                    if(GUIUtility.hotControl != controlId) break;

                    GUIUtility.hotControl = 0;

                    // Render (or unhide?) all aspects of terrain (heightmap, trees and details)
                    terrain.editorRenderFlags = TerrainRenderFlags.all;
                    
                    terrain.ApplyDelayedHeightmapModification();

                    gridPlane.SetActive(false);

                    // Reset the flatten height tool's value after the mouse has been released
                    flattenHeight = -1f;

                    mouseIsDown = false;
                    currentCommand = null;
                    currentTotalMouseDelta = 0f;
                    lastClickPosition = Vector3.zero;

                    currentEvent.Use();
                    break;
                case EventType.Repaint:
                    SetCursorEnabled(false);
                    break;
                case EventType.Layout:
                    if(CurrentMode == TerrainMode.None) break;

                    // Sets the ID of the default control. If there is no other handle being hovered over, it will choose this value
                    HandleUtility.AddDefaultControl(controlId);
                    break;
            }
            
            // Apply the current terrain tool
            if(editorEventType == EventType.Repaint && mouseIsDown) {
                Vector3 mouseWorldspacePosition;
                if(GetMousePositionInWorldSpace(out mouseWorldspacePosition)) {
                    if(CurrentBrushSettings.useBrushSpacing) {
                        mouseSpacingDistance += (new Vector2(lastWorldspaceMousePosition.x, lastWorldspaceMousePosition.z) - 
                            new Vector2(mouseWorldspacePosition.x, mouseWorldspacePosition.z)).magnitude;
                    }

                    terrain.editorRenderFlags = TerrainRenderFlags.heightmap;

                    TerrainPaintInfo paintInfo;
                    /**
                    * If the Current Mode supports an interactive mode, and control is also pressed, use the 
                    * last click position for the 
                    */
                    if(CurrentMode < FirstNonScultpingTerrainMode && currentEvent.control) {
                        paintInfo = GetTerrainArea(lastClickPosition);
                    } else {
                        paintInfo = GetTerrainArea(mouseWorldspacePosition);
                    }

                    /**
                    * Update the grid position
                    */
                    if(settings.showGrid == true ) {
                        if(gridPlane.activeSelf == false) { 
                            gridPlane.SetActive(true);
                        }

                        Vector3 gridPosition;
                        // If the current tool is interactive, keep the grid at the lastGridPosition
                        if(currentEvent.control) {
                            gridPosition = new Vector3(lastClickPosition.x, lastClickPosition.y + 0.001f, lastClickPosition.z);
                        } else {
                            gridPosition = new Vector3(mouseWorldspacePosition.x, lastClickPosition.y + 0.001f, mouseWorldspacePosition.z);
                        }
                        float gridPlaneDistance = Mathf.Abs(lastClickPosition.y - SceneView.currentDrawingSceneView.camera.transform.position.y);
                        float gridPlaneSize = CurrentBrushSettings.BrushSize * 1.2f;
                        gridPlane.transform.position = gridPosition;
                        gridPlane.transform.localScale = Vector3.one * gridPlaneSize;
                        
                        // Get the Logarithm of base 10 from the distance to get a power to mutliple the grid scale by
                        float power = Mathf.Round(Mathf.Log10(gridPlaneDistance) - 1);
                        
                        // Make the grid appear as if it's being illuminated by the cursor but keeping the grids remain within unit size tiles
                        gridPlaneMaterial.mainTextureOffset = new Vector2(gridPosition.x, gridPosition.z) / Mathf.Pow(10f, power);

                        gridPlaneMaterial.mainTextureScale = new Vector2(gridPlaneSize, gridPlaneSize) / Mathf.Pow(10f, power);
                    }
                                        
                    if(currentCommand != null) {
                        /**
                        * Only allow the various Behaviours to be active when control isn't pressed to make these behaviours 
                        * not occur while using interactive tools
                        */
                        if(currentEvent.control == false) {
                            float spacing = CurrentBrushSettings.BrushSize * randomSpacing;

                            // If brush spacing is enabled, do not update the current command until the cursor has exceeded the required distance
                            if(CurrentBrushSettings.useBrushSpacing && mouseSpacingDistance < spacing) {
                                lastWorldspaceMousePosition = mouseWorldspacePosition;
                                return;
                            } else {
                                UpdateRandomSpacing();
                                mouseSpacingDistance = 0f;
                            }
                            
                            if(CurrentBrushSettings.useRandomRotation && (CurrentBrush is ProceduralBrush && CurrentBrushSettings.BrushRoundness == 1f) == false) {
                                RotateTemporaryBrushSamples();
                                currentCommand.brushSamples = temporarySamples;
                            }
                        }

                        if(samplesDirty != SamplesDirty.None) {
                            UpdateDirtyBrushSamples();
                        }

                        currentCommand.Execute(currentEvent, paintInfo);
                        heights = currentCommand.heights;
                    } else {
                        float[,] unmodifiedHeights = (float[,])heights.Clone();

                        switch(CurrentMode) {
                            case TerrainMode.RaiseOrLower:
                                currentCommand = new RaiseOrLowerCommand(terrainData, heights, unmodifiedHeights, BrushSamplesWithSpeed);
                                break;
                            case TerrainMode.Smooth:
                                SmoothCommand smoothCommand = new SmoothCommand(terrainData, heights, unmodifiedHeights, BrushSamplesWithSpeed);
                                smoothCommand.boxFilterSize = settings.boxFilterSize;
                                currentCommand = smoothCommand;                            
                                break;
                            case TerrainMode.SetHeight:
                                SetHeightCommand setHeightCommand = new SetHeightCommand(terrainData, heights, unmodifiedHeights, BrushSamplesWithSpeed);
                                setHeightCommand.normalizedHeight = settings.setHeight / terrainData.size.y;        
                                currentCommand = setHeightCommand;                
                                break;
                            case TerrainMode.Flatten:
                                // Update the flatten height if it was reset before
                                if(flattenHeight == -1f) {
                                    flattenHeight = (mouseWorldspacePosition.y - terrain.transform.position.y) / terrainData.size.y;
                                }
                                FlattenCommand flattenCommand = new FlattenCommand(terrainData, heights, unmodifiedHeights, BrushSamplesWithSpeed);
                                flattenCommand.mode = settings.flattenMode;
                                flattenCommand.flattenHeight = flattenHeight;
                                currentCommand = flattenCommand;
                                break;
                        }
                    }

                    brushProjectorGameObject.SetActive(true);

                    newHeights = new float[paintInfo.clippedHeight, paintInfo.clippedWidth];
                    for(int x = 0; x < paintInfo.clippedWidth; x++) {
                        for(int y = 0; y < paintInfo.clippedHeight; y++) {
                            newHeights[y, x] = heights[y + paintInfo.normalizedBottomOffset, x + paintInfo.normalizedLeftOffset];
                        }
                    }
					
					if(paintInfo.clippedWidth > 0 && paintInfo.clippedHeight > 0) {
                        if(settings.alwaysUpdateTerrainLODs) {
                            terrainData.SetHeights(paintInfo.normalizedLeftOffset, paintInfo.normalizedBottomOffset, newHeights);                    
                        } else {
                            terrainData.SetHeightsDelayLOD(paintInfo.normalizedLeftOffset, paintInfo.normalizedBottomOffset, newHeights);
                        }
                    }
                }
                
                lastWorldspaceMousePosition = mouseWorldspacePosition;

                // While the mouse is down, always repaint
                SceneView.RepaintAll();
            }
        }

        private void LoadSceneViewInformationPanel() {
            sceneViewInformationStyle.normal.background = AssetDatabase.LoadAssetAtPath<Texture2D>(settings.mainDirectory + "Textures/SceneInfoPanel.PSD");
        }

        private void UpdateRandomSpacing() {
            randomSpacing = UnityEngine.Random.Range(CurrentBrushSettings.MinBrushSpacing, CurrentBrushSettings.MaxBrushSpacing);
        }
        
        private void RotateTemporaryBrushSamples() {
            cachedTerrainBrush = brushCollection.brushes[CurrentBrushSettings.SelectedBrush];
            
            if(temporarySamples == null || temporarySamples.GetLength(0) != brushSizePixels) {
                temporarySamples = new float[brushSizePixels, brushSizePixels];
            }

            Vector2 midPoint = new Vector2(brushSizePixels * 0.5f, brushSizePixels * 0.5f);
            float angle = CurrentBrushSettings.BrushAngle + UnityEngine.Random.Range(CurrentBrushSettings.MinRandomRotation, CurrentBrushSettings.MaxRandomRotation);
            float sineOfAngle = Mathf.Sin(angle * Mathf.Deg2Rad);
            float cosineOfAngle = Mathf.Cos(angle * Mathf.Deg2Rad);
            Vector2 newPoint;
            
            for(int x = 0; x < brushSizePixels; x++) {
                for(int y = 0; y < brushSizePixels; y++) {
                    newPoint = ExtraMath.RotatePointAroundPoint(new Vector2(x, y), midPoint, angle, sineOfAngle, cosineOfAngle);
                    temporarySamples[x, y] = GetInteropolatedBrushSample(newPoint.x, newPoint.y) * CurrentBrushSettings.BrushSpeed;
                }
            }
        }
        
        private float GetInteropolatedBrushSample(float x, float y) {
            int flooredX = Mathf.FloorToInt(x);
            int flooredY = Mathf.FloorToInt(y);
            int flooredXPlus1 = flooredX + 1;
            int flooredYPlus1 = flooredY + 1;

            if(flooredX < 0 || flooredX >= brushSizePixels || flooredY < 0 || flooredY >= brushSizePixels) return 0f;

            float topLeftSample = cachedTerrainBrush.samples[flooredX, flooredY];
            float topRightSample = 0f;
            float bottomLeftSample = 0f;
            float bottomRightSample = 0f;

            if(flooredXPlus1 < brushSizePixels) {
                topRightSample = cachedTerrainBrush.samples[flooredXPlus1, flooredY];
            }

            if(flooredYPlus1 < brushSizePixels) {
                bottomLeftSample = cachedTerrainBrush.samples[flooredX, flooredYPlus1];

                if(flooredXPlus1 < brushSizePixels) {
                    bottomRightSample = cachedTerrainBrush.samples[flooredXPlus1, flooredYPlus1];
                }
            }

            return Mathf.Lerp(Mathf.Lerp(topLeftSample, topRightSample, x % 1f), Mathf.Lerp(bottomLeftSample, bottomRightSample, x % 1f), y % 1f);
        }

        private void UpdateDirtyBrushSamples() {
            if(samplesDirty == SamplesDirty.None) return;
            
            // Update only the brush samples, and don't even update the projector texture
            if((samplesDirty & SamplesDirty.BrushSamples) == SamplesDirty.BrushSamples) {
                CurrentBrush.UpdateSamplesWithSpeed(brushSizePixels);
            }
            if((samplesDirty & SamplesDirty.ProjectorTexture) == SamplesDirty.ProjectorTexture) {
                UpdateBrushProjectorTextureAndSamples();
            }
            if((samplesDirty & SamplesDirty.InspectorTexture) == SamplesDirty.InspectorTexture) {
                UpdateBrushInspectorTexture();
            }

            brushProjector.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            samplesDirty = SamplesDirty.None;
        }
        #endregion

        /**
        * TODO: It should be possible to eliminate the need to do Repaint, currentEvent.Use() and to not be required to enter the 
        * shortcut name as a string.
        */
        private void CheckKeyboardShortcuts(Event currentEvent) {
            if(GUIUtility.hotControl != 0) return;

            // Only check for shortcuts when no terrain command is active
            if(currentCommand != null) return;

            if(currentEvent.type != EventType.KeyDown) return;

            /**
            * Check to make sure there is no textField focused. This will ensure that shortcut strokes will not override
            * typing in text fields. Through testing however, all textboxes seem to mark the event as Used. This is simply
            * here as a precaution.
            */
            if((bool)guiUtilityTextFieldInput.GetValue(null, null)) return;
            
            if(CurrentMode != TerrainMode.None && CurrentMode < FirstNonScultpingTerrainMode) {
                // Left Bracket - decrease brush size
                if(Shortcut.Shortcuts["Decrease Brush Size"].WasExecuted(currentEvent)) {
                    CurrentBrushSettings.BrushSize = Mathf.Clamp(CurrentBrushSettings.BrushSize - terrainData.size.x * settings.brushSizeIncrementMultiplier, MinBrushSize, MaxBrushSize);
                    Repaint();
                    currentEvent.Use();
                    return;
                } 
                // Right Bracket - increase brush size
                else if(Shortcut.Shortcuts["Increase Brush Size"].WasExecuted(currentEvent)) {
                    CurrentBrushSettings.BrushSize = Mathf.Clamp(CurrentBrushSettings.BrushSize + terrainData.size.x * settings.brushSizeIncrementMultiplier, MinBrushSize, MaxBrushSize);
                    Repaint();
                    currentEvent.Use();
                    return;
                }
                // Minus - decrease brush speed
                else if(Shortcut.Shortcuts["Decrease Brush Speed"].WasExecuted(currentEvent)) {
                    CurrentBrushSettings.BrushSpeed = Mathf.Clamp(Mathf.Round((CurrentBrushSettings.BrushSpeed - 0.1f) / 0.1f) * 0.1f, minBrushSpeed, maxBrushSpeed);
                    Repaint();
                    currentEvent.Use();
                    return;
                }
                // Equals - increase brush speed
                else if(Shortcut.Shortcuts["Increase Brush Speed"].WasExecuted(currentEvent)) {
                    CurrentBrushSettings.BrushSpeed = Mathf.Clamp(Mathf.Round((CurrentBrushSettings.BrushSpeed + 0.1f) / 0.1f) * 0.1f, minBrushSpeed, maxBrushSpeed);
                    Repaint();
                    currentEvent.Use();
                    return;
                }
                // P - next brush
                else if(Shortcut.Shortcuts["Next Brush"].WasExecuted(currentEvent)) {
                    IncrementSelectedBrush(1);
                    Repaint();
                    currentEvent.Use();
                } 
                // O - previous brush
                else if(Shortcut.Shortcuts["Previous Brush"].WasExecuted(currentEvent)) {
                    IncrementSelectedBrush(-1);
                    Repaint();
                    currentEvent.Use();
                }

                // Brush angle only applies to custom brushes
                if(CurrentBrush != null && CurrentBrush is ProceduralBrush == false) {
                    // 0 - reset brush angle
                    if(Shortcut.Shortcuts["Reset Brush Rotation"].WasExecuted(currentEvent)) {
                        CurrentBrushSettings.BrushAngle = 0f;
                        Repaint();
                        currentEvent.Use();
                    }
                    // ; - rotate brush counter-clockwise
                    else if(Shortcut.Shortcuts["Rotate Brush Counterclockwise"].WasExecuted(currentEvent)) {
                        CurrentBrushSettings.BrushAngle += 2f;
                        Repaint();
                        currentEvent.Use();
                        return;
                    }
                    // ' - rotate brush right
                    else if(Shortcut.Shortcuts["Rotate Brush Clockwise"].WasExecuted(currentEvent)) {
                        CurrentBrushSettings.BrushAngle -= 2f;
                        Repaint();
                        currentEvent.Use();
                        return;
                    }
                }
            }
            // Z - Set mode to Raise/Lower
            if(Shortcut.Shortcuts["Select Raise/Lower Mode"].WasExecuted(currentEvent)) {
                CurrentMode = TerrainMode.RaiseOrLower;
                Repaint();
                currentEvent.Use();
            }
            // X - Set mode to Smooth
            else if(Shortcut.Shortcuts["Select Smooth Mode"].WasExecuted(currentEvent)) {
                CurrentMode = TerrainMode.Smooth;
                Repaint();
                currentEvent.Use();
            }
            // C - Set mode to Set Height
            else if(Shortcut.Shortcuts["Select Set Height Mode"].WasExecuted(currentEvent)) {
                CurrentMode = TerrainMode.SetHeight;
                Repaint();
                currentEvent.Use();
            }
            // V - Set mode to Flatten
            else if(Shortcut.Shortcuts["Select Flatten Mode"].WasExecuted(currentEvent)) {
                CurrentMode = TerrainMode.Flatten;
                Repaint();
                currentEvent.Use();
            }
            // B - Set mode to Generate
            else if(Shortcut.Shortcuts["Select Generate Mode"].WasExecuted(currentEvent)) {
                CurrentMode = TerrainMode.Generate;
                Repaint();
                currentEvent.Use();
            }
            // N - Set mode to Settings
            else if(Shortcut.Shortcuts["Select Settings Tab"].WasExecuted(currentEvent)) {
                CurrentMode = TerrainMode.Settings;
                Repaint();
                currentEvent.Use();
            }
            // I - Toggle projection mode
            else if(Shortcut.Shortcuts["Toggle Sculpt Onto Mode"].WasExecuted(currentEvent)) {
                settings.raycastOntoFlatPlane = !settings.raycastOntoFlatPlane;
                Repaint();
                currentEvent.Use();
            }
            // Shift+G - Flatten Terrain Shortcut
            else if(Shortcut.Shortcuts["Flatten Terrain"].WasExecuted(currentEvent)) {
                Undo.RegisterCompleteObjectUndo(terrainData, "Flatten Terrain");
                FlattenTerrain(0f);
                currentEvent.Use();
            }
        }
        
        private int GetCurrentBrushIndex() {
            int currentIndex = -1;
            int i = 0;
            // Find the current brushes index
            foreach(string key in brushCollection.brushes.Keys) {
                if(key == CurrentBrush.name) {
                    currentIndex = i;
                    break;
                }
                i++;
            }
            return currentIndex;
        }

        private void IncrementSelectedBrush(int v) {
            int currentIndex = GetCurrentBrushIndex();

            if(currentIndex == -1) {
                Debug.LogError("Couldn't find the current brush in the brush collection");
                return;
            }

            int newIndex = currentIndex + v;

            if(newIndex < 0 || newIndex >= brushCollection.brushes.Count) return;

            string newKey = brushCollection.brushes.Keys.ElementAt(newIndex);
            if(newKey == null) {
                Debug.LogError("Couldn't find key based on an index");
                return;
            }
            CurrentBrushSettings.SelectedBrush = newKey;
        }
        
        private void CurrentModeChanged() {
            if(CurrentMode == TerrainMode.None || CurrentMode >= FirstNonScultpingTerrainMode) return;
            
            brushProjector.orthographicSize = CurrentBrushSettings.BrushSize * 0.5f;
            topPlaneGameObject.transform.localScale = new Vector3(CurrentBrushSettings.BrushSize, CurrentBrushSettings.BrushSize, CurrentBrushSettings.BrushSize);
            brushSizePixels = GetSegmentsFromUnits(CurrentBrushSettings.BrushSize);

            if(settings.AlwaysShowBrushSelection) {
                brushCollection.UpdatePreviewTextures();
            } else {
                UpdateBrushInspectorTexture();
            }

            UpdateBrushProjectorTextureAndSamples();
        }
        
        private void SelectedBrushValueChanged() {
            UpdateBrushTextures();
        }

        private void RandomSpacingChanged() {
            CurrentBrushSettings.useBrushSpacing = true;
        }

        private void RandomOffsetChanged() {
            CurrentBrushSettings.useRandomOffset = true;
        }

        private void RandomRotationChanged() {
            CurrentBrushSettings.useRandomRotation = true;
        }
        
        /**
        * Brush parameters changed
        */
        private void BrushSpeedChanged() {
            samplesDirty |= SamplesDirty.BrushSamples;
        }

        private void BrushColourChanged() {
            brushProjector.material.color = settings.BrushColour;
            topPlaneMaterial.color = settings.BrushColour * 0.9f;
        }

        private void BrushSizeChanged() {
            if(CurrentMode == TerrainMode.None || CurrentMode >= FirstNonScultpingTerrainMode) return;

            brushSizePixels = GetSegmentsFromUnits(CurrentBrushSettings.BrushSize);

            /**
            * HACK: Another spot where objects are seemingly randomly destroyed. The top plane and projector are (seemingly) destroyed between
            * switching from one terrain with Terrain Former to another.
            */
            if(topPlaneGameObject == null || brushProjector == null) {
                CreateProjector();
            }

            topPlaneGameObject.transform.localScale = new Vector3(CurrentBrushSettings.BrushSize, CurrentBrushSettings.BrushSize, CurrentBrushSettings.BrushSize);
            brushProjector.orthographicSize = CurrentBrushSettings.BrushSize * 0.5f;
            
            samplesDirty |= SamplesDirty.ProjectorTexture;
        }

        private void BrushRoundnessChanged() {
            samplesDirty |= SamplesDirty.ProjectorTexture;

            if(settings.AlwaysShowBrushSelection) {
                brushCollection.UpdatePreviewTextures();
            } else {
                UpdateBrushInspectorTexture();
            }
        }

        private void BrushAngleDeltaChanged(float delta) {
            if(settings.AlwaysShowBrushSelection) {
                brushCollection.UpdatePreviewTextures();
            } else {
                UpdateBrushInspectorTexture();
            }

            brushProjector.transform.eulerAngles = new Vector3(90f, brushProjector.transform.eulerAngles.y + delta, 0f);

            samplesDirty = SamplesDirty.BrushSamples | SamplesDirty.ProjectorTexture;
        }

        private void AlwaysShowBrushSelectionValueChanged() {
            /**
            * If the brush selection should always be shown, make sure isSelectingBrush is set to false because
            * when changing to AlwaysShowBrushSelection while the brush selection was active, it will return back to
            * selecting a brush.
            */
            if(settings.AlwaysShowBrushSelection == true) {
                isSelectingBrush = false;
            }
        }

        private void PreviewSizeValueChanged() {
            brushCollection.UpdatePreviewTextures();
        }

        private void UseCustomBrushAngleChanged() {
            UpdateBrushTextures();
        }

        private void UseFalloffForCustomBrushesChanged() {
            if(settings.AlwaysShowBrushSelection) {
                brushCollection.UpdatePreviewTextures();
            } else {
                UpdateBrushInspectorTexture();
            }
            UpdateBrushProjectorTextureAndSamples();
        }

        private void UseAlphaFalloffChanged() {
            if(settings.AlwaysShowBrushSelection) {
                brushCollection.UpdatePreviewTextures();
            } else {
                UpdateBrushInspectorTexture();
            }
            UpdateBrushProjectorTextureAndSamples();
        }

        /**
        * Update the heights array every time an Undo or Redo occurs. Since we must rely on storing and managing the 
        * heights data manually (since calling GetHeights constantly makes bumpy terrain), we must update it ourselves 
        * every time it's changed. 
        */
        private void UndoRedoPerformed() {
            if(terrainData == null) return;
            heights = terrainData.GetHeights(0, 0, terrainData.heightmapWidth, terrainData.heightmapHeight);
        }

        private void CreateGridPlane() {
            gridPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            gridPlane.name = "GridPlane";
            gridPlane.transform.Rotate(90f, 0f, 0f);
            gridPlane.transform.localScale = Vector3.one * 20f;
            gridPlane.hideFlags = HideFlags.HideAndDontSave;
            gridPlane.SetActive(false);

            Shader gridShader = Shader.Find("Hidden/TerrainFormer/Grid");
            if(gridShader == null) {
                Debug.LogError("Terrain Former couldn't find its grid shader");
                return;
            }

            gridPlaneMaterial = new Material(gridShader);
            gridPlaneMaterial.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(settings.mainDirectory + "Textures/Tile.png");
            gridPlaneMaterial.mainTexture.wrapMode = TextureWrapMode.Repeat;
            gridPlaneMaterial.shader.hideFlags = HideFlags.HideAndDontSave;
            gridPlaneMaterial.hideFlags = HideFlags.HideAndDontSave;
            gridPlaneMaterial.mainTextureScale = new Vector2(8f, 8f); // Set texture scale to create 8x8 tiles
            gridPlane.GetComponent<Renderer>().sharedMaterial = gridPlaneMaterial;
        }

        private void CreateProjector() {
            /**
            * Create the brush projector
            */
            brushProjectorGameObject = new GameObject("TerrainFormerProjector");
            brushProjectorGameObject.hideFlags = HideFlags.HideAndDontSave;
            
            brushProjector = brushProjectorGameObject.AddComponent<Projector>();
            brushProjector.nearClipPlane = -1000f;
            brushProjector.farClipPlane = 1000f;
            brushProjector.orthographic = true;
            brushProjector.transform.Rotate(90f, 0.0f, 0.0f);
            
            brushProjectorMaterial = new Material(Shader.Find("Hidden/TerrainFormer/Terrain Brush Preview"));
            brushProjectorMaterial.shader.hideFlags = HideFlags.HideAndDontSave;
            brushProjectorMaterial.hideFlags = HideFlags.HideAndDontSave;
            brushProjectorMaterial.color = settings.BrushColour;
            brushProjector.material = brushProjectorMaterial;
            Texture2D outlineTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(settings.mainDirectory + "Textures/BrushOutline.png");
            outlineTexture.wrapMode = TextureWrapMode.Clamp;
            brushProjectorMaterial.SetTexture("_OutlineTex", outlineTexture);
            
            /**
            * Create the top plane
            */
            topPlaneGameObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            topPlaneGameObject.name = "Top Plane";
            topPlaneGameObject.hideFlags = HideFlags.HideAndDontSave;
            DestroyImmediate(topPlaneGameObject.GetComponent<MeshCollider>());
            topPlaneGameObject.transform.Rotate(90f, 0f, 0f);
            
            topPlaneMaterial = new Material(Shader.Find("Hidden/TerrainFormer/BrushPlaneTop"));
            topPlaneMaterial.shader.hideFlags = HideFlags.HideAndDontSave;
            topPlaneMaterial.hideFlags = HideFlags.HideAndDontSave;
            topPlaneMaterial.color = settings.BrushColour * 0.9f;
            topPlaneMaterial.SetTexture("_OutlineTex", outlineTexture);
            
            topPlaneGameObject.GetComponent<MeshRenderer>().sharedMaterial = topPlaneMaterial;

            SetCursorEnabled(false);
        }

        private void UpdateProjector() {
            if(brushProjector == null) return;

            if(CurrentMode == TerrainMode.None || CurrentMode == TerrainMode.Generate || CurrentMode == TerrainMode.Settings) {
                SetCursorEnabled(false);
                
                return;
            }
            
            Vector3 position;
            if(GetMousePositionInWorldSpace(out position)) {
                brushProjectorGameObject.transform.position = position;
                brushProjectorGameObject.SetActive(true);

                if(CurrentMode == TerrainMode.Flatten) {
                    // Only show the topPlane if the height is more than 1/200th of the heightmap scale
                    if(position.y < terrainData.heightmapScale.y * 0.005f) {
                        topPlaneGameObject.SetActive(false);
                        return;
                    }

                    topPlaneGameObject.SetActive(true);
                    topPlaneGameObject.transform.position = new Vector3(position.x, position.y, position.z);
                } else if(CurrentMode == TerrainMode.SetHeight) {
                    if(settings.setHeight < terrainData.heightmapScale.y * 0.005f) {
                        topPlaneGameObject.SetActive(false);
                        return;
                    }

                    topPlaneGameObject.SetActive(true);
                    topPlaneGameObject.transform.position = new Vector3(position.x, settings.setHeight, position.z);
                } else {
                    topPlaneGameObject.SetActive(false);
                }
            } else {
                SetCursorEnabled(false);
            }
            
            HandleUtility.Repaint();
        }

        private void UpdateBrushTextures() {
            UpdateBrushInspectorTexture();
            UpdateBrushProjectorTextureAndSamples();
        }
        
        private void UpdateBrushProjectorTextureAndSamples() {
            brushProjectorTexture = CurrentBrush.UpdateSamplesAndMainTexture(brushSizePixels);
            
            // HACK: Projector objects are destroyed (seemingly randomly), so recreate them if necessary
            if(brushProjectorGameObject == null || brushProjectorMaterial == null) {
                CreateProjector();
            }

            brushProjectorMaterial.mainTexture = brushProjectorTexture;
            topPlaneMaterial.mainTexture = brushProjectorTexture;
            
            if(currentCommand != null) {
                currentCommand.brushSamples = BrushSamplesWithSpeed;
            }
        }

        private void UpdateBrushInspectorTexture() {
            CurrentBrush.CreatePreviewTexture();
        }
        
        private TerrainPaintInfo GetTerrainArea(Vector3 pos) {
            Vector2 uv;
            // Only use the random offset if an interactive tool is not being used.
            if(Event.current.control == false && CurrentBrushSettings.useRandomOffset) {
                Vector2 randomCirclePoint = UnityEngine.Random.insideUnitCircle * CurrentBrushSettings.RandomOffset;
                uv = new Vector2((pos.x - terrain.transform.position.x + randomCirclePoint.x) / terrainData.size.x,
                    (pos.z - terrain.transform.position.z + randomCirclePoint.y) / terrainData.size.z);
            } else {
                uv = new Vector2((pos.x - terrain.transform.position.x) / terrainData.size.x,
                    (pos.z - terrain.transform.position.z) / terrainData.size.z);
            }
            
            // The number of height segments that fit in the brush
            int brushSizeSegments = GetSegmentsFromUnits(CurrentBrushSettings.BrushSize);
            float halfBrushSizeSegments = brushSizeSegments * 0.5f;

            brushSizePixels = brushSizeSegments;

            // The bottom-left positions/segments that the cursor currently is pointing to
            int leftSegment = Mathf.RoundToInt(uv.x * terrainData.heightmapWidth);
            int bottomSegment = Mathf.RoundToInt(uv.y * terrainData.heightmapHeight);

            // The segments the cursor is pointing to, minus half the brush size in segments 
            int normalizedLeftSegment = Mathf.Max(Mathf.RoundToInt(leftSegment - halfBrushSizeSegments), 0);
            int normalizedBottomSegment = Mathf.Max(Mathf.RoundToInt(bottomSegment - halfBrushSizeSegments), 0);

            /** 
            * Create a paint patch used for off setting the terrain samples.
            * Clipped left contains how many segments are being clipped to the left side of the terrain. The value is 0 if there 
            * are no segments being clipped. This same pattern applies to clippedBottom
            */
            int clippedLeft = 0;
            if(leftSegment - halfBrushSizeSegments < 0) {
                clippedLeft = Mathf.RoundToInt(Mathf.Abs(leftSegment - halfBrushSizeSegments));
            }

            int clippedBottom = 0;
            if(bottomSegment - halfBrushSizeSegments < 0) {
                clippedBottom = Mathf.RoundToInt(Mathf.Abs(bottomSegment - halfBrushSizeSegments));
            }

            int clippedWidth = 0;
            if(leftSegment + halfBrushSizeSegments > terrainData.heightmapWidth) {
                clippedWidth = Mathf.RoundToInt((leftSegment + halfBrushSizeSegments) - terrainData.heightmapWidth);
            }

            int clippedHeight = 0;
            if(bottomSegment + halfBrushSizeSegments > terrainData.heightmapHeight) {
                clippedHeight = Mathf.RoundToInt((bottomSegment + halfBrushSizeSegments) - terrainData.heightmapHeight);
            }

            clippedWidth = Mathf.Max(brushSizeSegments - clippedWidth - clippedLeft, 0);
            clippedHeight = Mathf.Max(brushSizeSegments - clippedHeight - clippedBottom, 0);

            return new TerrainPaintInfo(clippedLeft, clippedBottom, clippedWidth, clippedHeight, normalizedLeftSegment, normalizedBottomSegment);
        }

        #region GlobalTerrainModifications
        private void CreateRampCurve(float maxHeight) {
            Undo.RegisterCompleteObjectUndo(terrainData, "Created Linear Ramp");
            int terrainWidth = terrainData.heightmapWidth;
            int terrainHeight = terrainData.heightmapHeight;
            float heightCoefficient = maxHeight / terrainData.size.y;
            float height;
            
            for(int x = 0; x < terrainWidth; x++) {
                height = settings.generateRampCurve.Evaluate((float)x / terrainHeight) * heightCoefficient;

                for(int y = 0; y < terrainHeight; y++) {
                    heights[x, y] = height;
                }
            }

            terrainData.SetHeights(0, 0, heights);
        }

        private void CreateCircularRampCurve(float maxHeight) {
            Undo.RegisterCompleteObjectUndo(terrainData, "Created Circular Ramp");
            float heightCoefficient = maxHeight / terrainData.size.y;
            int width = terrainData.heightmapWidth;
            int height = terrainData.heightmapHeight;
            float halfWidth = terrainData.heightmapWidth * 0.5f;
            float halfHeight = terrainData.heightmapHeight * 0.5f;
            float distance;

            for(int x = 0; x < width; x++) {
                for(int y = 0; y < height; y++) {
                    distance = Vector2.Distance(new Vector2(x, y), new Vector2(halfWidth, halfHeight));
                    heights[x, y] = settings.generateRampCurve.Evaluate(1f - (distance / halfHeight)) * heightCoefficient;
                }
            }

            terrainData.SetHeights(0, 0, heights);
        }

        private void FlattenTerrain(float setHeight) {
            int width = terrainData.heightmapWidth;
            int height = terrainData.heightmapHeight;
            float normalizedHeight = setHeight / terrainData.size.y;
            
            for(int x = 0; x < width; x++) {
                for(int y = 0; y < height; y++) {
                    heights[x, y] = normalizedHeight;
                }
            }

            terrainData.SetHeights(0, 0, heights);
        }

        private void SmoothAll() {
            Undo.RegisterCompleteObjectUndo(terrainData, "Smooth Terrain");

            int terrainWidth = terrainData.heightmapWidth;
            int terrainHeight = terrainData.heightmapHeight;
            float[,] newHeights = new float[terrainWidth, terrainHeight];

            float heightSum;
            int neighbourCount, positionX, positionY;

            float totalOperations = settings.smoothingIterations * terrainWidth;
            float currentOperation = 0;
            
            for(int i = 0; i < settings.smoothingIterations; i++) {
                for(int x = 0; x < terrainWidth; x++) {
                    currentOperation++;

                    // Only update the progress bar every width segment, otherwise it will be called way too many times
                    if(EditorUtility.DisplayCancelableProgressBar("Smooth Entirety", "Smoothing entire terrain…", currentOperation / totalOperations) == true) {
                        // Cancel the entire smooth operation if the smooth operation was stopped
                        EditorUtility.ClearProgressBar();
                        return;
                    }

                    for(int y = 0; y < terrainHeight; y++) {
                        heightSum = 0f;
                        neighbourCount = 0;

                        for(int x2 = -settings.boxFilterSize; x2 <= settings.boxFilterSize; x2++) {
                            positionX = x + x2;
                            if(positionX < 0 || positionX >= terrainWidth) continue;
                            for(int y2 = -settings.boxFilterSize; y2 <= settings.boxFilterSize; y2++) {
                                positionY = y + y2;
                                if(positionY < 0 || positionY >= terrainHeight) continue;

                                heightSum += heights[positionY, positionX];
                                neighbourCount++;
                            }
                        }

                        newHeights[y, x] = heightSum / neighbourCount;
                    }
                }

                heights = newHeights;
            }

            EditorUtility.ClearProgressBar();

            terrainData.SetHeights(0, 0, heights);
        }
        #endregion

        private void OnAssetsImported(string[] assetPaths) {
            List<string> customBrushPaths = new List<string>();

            foreach(string path in assetPaths) {
                /**
                * Check if the terrain has been modified externally. If this terrain's path matches this terrain,
                * update the heights array.
                */
                if(terrainPath == path) {
                    heights = terrainData.GetHeights(0, 0, terrainData.heightmapWidth, terrainData.heightmapHeight);
                } 
                /**
                * If there are custom textures that have been update, keep a list of which onces have changed and update the brushCollection.
                */
                else if(path.StartsWith(BrushCollection.localCustomBrushPath)) {
                    customBrushPaths.Add(path);
                }
            }

            if(customBrushPaths.Count > 0) {
                brushCollection.RefreshCustomBrushes(customBrushPaths.ToArray());
                brushCollection.UpdatePreviewTextures();
            }
        }

        // Check if the terrain asset has been moved.
        private void OnAssetsMoved(string[] sourcePaths, string[] destinationPaths) {
            for(int i = 0; i < sourcePaths.Length; i++) {
                if(sourcePaths[i] == terrainPath) {
                    terrainPath = destinationPaths[i];
                    return;
                }
            }
        }

        private void OnAssetsDeleted(string[] paths) {
            List<string> deletedCustomBrushPaths = new List<string>();

            foreach(string path in paths) {
                if(path.StartsWith(BrushCollection.localCustomBrushPath)) {
                    deletedCustomBrushPaths.Add(path);
                }
            }

            if(deletedCustomBrushPaths.Count > 0) {
                brushCollection.RemoveDeletedBrushes(deletedCustomBrushPaths.ToArray());
                brushCollection.UpdatePreviewTextures();
            }
        }
        
        #region Utlities
        internal void UpdateBrushSizeIncrementIndex(float incrementMultiplier) {
            for(int i = 0; i < brushSizeIncrementValues.Length; i++) {
                if(incrementMultiplier == brushSizeIncrementValues[i]) {
                    brushSizeIncrementIndex = i;
                    return;
                }
            }

            brushSizeIncrementIndex = 0;
        }

        // Gets the position of the cursor depending on the raycast mouse option
        private bool GetCursorPosition(out Vector3 pos) {
            bool hitTerrain;
            
            if(settings.raycastOntoFlatPlane) {
                hitTerrain = LinePlaneIntersection(out pos);
            } else {
                Vector2 uv;
                hitTerrain = Raycast(out pos, out uv);
            }

            return hitTerrain;
        }

        /**
        * A modified version of the LinePlaneIntersection method from the 3D Math Functions script found on the Unify site 
        * Credit to Bit Barrel Media: http://wiki.unity3d.com/index.php?title=3d_Math_functions
        * This code has been modified to fit my needs and coding style
        * Get the intersection between a line and a plane. 
        * If the line and plane are not parallel, the function outputs true, otherwise false.
        */
        private bool LinePlaneIntersection(out Vector3 intersectingPoint) {
            Vector3 planePoint = new Vector3(0f, lastClickPosition.y, 0f);

            Ray mouseRay = HandleUtility.GUIPointToWorldRay(mousePosition);

            // Calculate the distance between the linePoint and the line-plane intersection point
            float dotNumerator = Vector3.Dot((planePoint - mouseRay.origin), Vector3.up);
            float dotDenominator = Vector3.Dot(mouseRay.direction, Vector3.up);

            // Check if the line and plane are not parallel
            if(dotDenominator != 0.0f) {
                float length = dotNumerator / dotDenominator;

                // Create a vector from the linePoint to the intersection point and set the vector length by normalizing and multiplying by the length
                Vector3 vector = Vector3.Normalize(mouseRay.direction) * length;
                
                // Get the coordinates of the line-plane intersection point
                intersectingPoint = mouseRay.origin + vector;

                return true;
            } else {
                intersectingPoint = Vector3.zero;
                return false;
            }
        }

        // Checks if the cursor is hovering over the terrain
        private bool Raycast() {
            RaycastHit hitInfo;
            return terrainCollider.Raycast(HandleUtility.GUIPointToWorldRay(mousePosition), out hitInfo, float.PositiveInfinity);
        }

        // Checks if the cursor is hovering over the terrain
        private bool Raycast(out Vector3 pos, out Vector2 uv) {
            RaycastHit hitInfo;
            if(terrainCollider.Raycast(HandleUtility.GUIPointToWorldRay(mousePosition), out hitInfo, float.PositiveInfinity)) {
                pos = hitInfo.point;
                uv = hitInfo.textureCoord;
                return true;
            } else {
                pos = Vector3.zero;
                uv = Vector2.zero;
                return false;
            }
        }

        internal void UpdateSetHeightAtMousePosition() {
            float height;
            if(GetTerrainHeightAtMousePosition(out height)) {
                settings.setHeight = height;
                Repaint();
            }
        }

        private bool GetTerrainHeightAtMousePosition(out float height) {
            Vector3 position;
            Vector2 uv;

            if(Raycast(out position, out uv)) {
                int leftSegment = Mathf.RoundToInt(uv.x * terrainData.heightmapWidth);
                int bottomSegment = Mathf.RoundToInt(uv.y * terrainData.heightmapHeight);
                height = terrainData.GetHeight(leftSegment, bottomSegment);
                return true;
            } else {
                height = 0f;
                return false;
            }
        }

        /**
        * Gets the mouse position in world space. This is a utlity method used to automatically get the position of 
        * the mouse depending on if it's being held down or not. Returns true if the terrain or plane was hit, 
        * returns false otherwise.
        */
        private bool GetMousePositionInWorldSpace(out Vector3 position) {
            // If the user is sampling height while in Set Height with Shift, only use a Raycast.
            if(mouseIsDown && CurrentMode == TerrainMode.SetHeight && Event.current.shift) {
                Vector2 uv;
                if(Raycast(out position, out uv) == false) {
                    SetCursorEnabled(false);
                    return false;
                }
            }
            // SetHeight and Flatten modes will always use plane projection, it makes no sense to raycast onto the current terrain point.
            else if(mouseIsDown && (CurrentMode == TerrainMode.SetHeight || CurrentMode == TerrainMode.Flatten)) {
                if(LinePlaneIntersection(out position) == false) {
                    SetCursorEnabled(false);
                    return false;
                }
            } 
            else if(mouseIsDown && settings.raycastOntoFlatPlane) {
                if(LinePlaneIntersection(out position) == false) {
                    SetCursorEnabled(false);
                    return false;
                }
            } else {
                Vector2 uv;
                if(Raycast(out position, out uv) == false) {
                    SetCursorEnabled(false);
                    return false;
                }
            }

            return true;
        }

        private void SetCursorEnabled(bool enabled) {
            brushProjectorGameObject.SetActive(enabled);
            topPlaneGameObject.SetActive(enabled);
        }

        private int GetSegmentsFromUnits(float units) {
            /**
            * HACK: When this is called from OnInspectorGUI, terrainData is present during the initial checks, but down the stacktrace to this function, terrainData is null.
            * Reinitialize here if necessary.
            */
            //Initialize();

            float segmentDensity = Mathf.Min(terrainData.heightmapWidth, terrainData.heightmapHeight) / Mathf.Min(terrainData.size.x, terrainData.size.z);

            return Mathf.RoundToInt(units * segmentDensity);
        }

        private bool VerifyMainDirectory() {
            if(Directory.Exists(editorDirectory) == false) return false;
            if(Directory.Exists(shadersDirectory) == false) return false;
            if(Directory.Exists(texturesDirectory) == false) return false;
            return true;
        }

        // This should be called every time the suspected mainDirectory has been changed
        private void UpdateSubDirectoryPaths(string mainDirectory) {
            editorDirectory = mainDirectory + "Editor";
            shadersDirectory = mainDirectory + "Shaders";
            texturesDirectory = mainDirectory + "Textures";
        }
        #endregion
    }
}