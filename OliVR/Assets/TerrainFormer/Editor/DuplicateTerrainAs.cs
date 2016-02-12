using UnityEngine;
using UnityEditor;
using System.Reflection;

/**
* NOTE:
* This script is still in a beta stage. It so far has one known issue (see below), it's possible there's more issues.
*
* Known Issue:
*  - Alphamaps are not being duplicated correctly (not sure why)
*/

namespace JesseStiller.TerrainFormerExtension {
    public class DuplicateTerrainAs : Editor {
        [MenuItem("Assets/&Duplicate Terrain…", true)]
        public static bool IsDuplicateTerrainValid() {
            if(Selection.activeGameObject == null) return false;

            Terrain terrain = Selection.activeGameObject.GetComponent<Terrain>();

            return terrain != null && terrain.terrainData != null;
        }

        [MenuItem("Assets/&Duplicate Terrain…", false)]
        public static void DuplicateTerrain() {
            Terrain sourceTerrain = Selection.activeGameObject.GetComponent<Terrain>();
            TerrainData sourceTerrainData = sourceTerrain.terrainData;

            string savePath = EditorUtility.SaveFilePanelInProject("Duplicate Terrain", sourceTerrainData.name, "asset", null);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(savePath);

            if(string.IsNullOrEmpty(savePath)) return;

            // Use Reflection to get detailResolutionPerPatch since it's internal
            PropertyInfo detailResolutionPerPatchPropertyInfo = typeof(TerrainData).GetProperty("detailResolutionPerPatch");
            int detailResolutionPerPatch = 8;
            if(detailResolutionPerPatchPropertyInfo != null) {
                detailResolutionPerPatch = (int)detailResolutionPerPatchPropertyInfo.GetValue(sourceTerrainData, null);
            }

            GameObject destinationTerrainGameObject = Terrain.CreateTerrainGameObject(null);

            destinationTerrainGameObject.name = fileName;
            if(fileName == sourceTerrain.name) {
                destinationTerrainGameObject.name += " (Copy)";
            }
            
            Terrain destinationTerrain = destinationTerrainGameObject.GetComponent<Terrain>();
            TerrainCollider terrainCollider = destinationTerrainGameObject.GetComponent<TerrainCollider>();
            TerrainData destinationTerrainData = new TerrainData();
            
            // Paint Texture
            destinationTerrainData.alphamapResolution   = sourceTerrainData.alphamapResolution;
            destinationTerrainData.splatPrototypes = sourceTerrainData.splatPrototypes;
            destinationTerrainData.SetAlphamaps(0, 0, sourceTerrainData.GetAlphamaps(0, 0, sourceTerrainData.alphamapWidth, sourceTerrainData.alphamapHeight));

            // Trees
            destinationTerrainData.treePrototypes = sourceTerrainData.treePrototypes;
            destinationTerrainData.treeInstances = sourceTerrainData.treeInstances;

            // Details
            destinationTerrainData.SetDetailResolution(sourceTerrainData.detailResolution, detailResolutionPerPatch);
            destinationTerrainData.detailPrototypes = sourceTerrainData.detailPrototypes;
            for(int d = 0; d < sourceTerrainData.detailPrototypes.Length; d++) {
                destinationTerrainData.SetDetailLayer(0, 0, d, sourceTerrainData.GetDetailLayer(0, 0, sourceTerrainData.detailWidth, sourceTerrainData.detailHeight, d));
            }

            // Base Terrain
            destinationTerrain.drawHeightmap            = sourceTerrain.drawHeightmap;
            destinationTerrain.heightmapPixelError      = sourceTerrain.heightmapPixelError;
            destinationTerrain.basemapDistance          = sourceTerrain.basemapDistance;
            destinationTerrain.castShadows              = sourceTerrain.castShadows;
            destinationTerrain.materialType             = sourceTerrain.materialType;
            destinationTerrain.reflectionProbeUsage     = sourceTerrain.reflectionProbeUsage;
            destinationTerrainData.thickness            = sourceTerrainData.thickness;

            // Tree & Detail Objects
            destinationTerrain.drawTreesAndFoliage      = sourceTerrain.drawTreesAndFoliage;
            destinationTerrain.bakeLightProbesForTrees  = sourceTerrain.bakeLightProbesForTrees;
            destinationTerrain.detailObjectDistance     = sourceTerrain.detailObjectDistance;
            destinationTerrain.collectDetailPatches     = sourceTerrain.collectDetailPatches;
            destinationTerrain.detailObjectDensity      = sourceTerrain.detailObjectDensity;
            destinationTerrain.treeDistance             = sourceTerrain.treeDistance;
            destinationTerrain.treeBillboardDistance    = sourceTerrain.treeBillboardDistance;
            destinationTerrain.treeCrossFadeLength      = sourceTerrain.treeCrossFadeLength;
            destinationTerrain.treeMaximumFullLODCount  = sourceTerrain.treeMaximumFullLODCount;

            // Wind Settings for Grass
            destinationTerrainData.wavingGrassStrength  = sourceTerrainData.wavingGrassStrength;
            destinationTerrainData.wavingGrassSpeed     = sourceTerrainData.wavingGrassSpeed;
            destinationTerrainData.wavingGrassAmount    = sourceTerrainData.wavingGrassAmount;
            destinationTerrainData.wavingGrassTint      = sourceTerrainData.wavingGrassTint;

            // Resolution
            destinationTerrainData.heightmapResolution  = sourceTerrainData.heightmapResolution;
            destinationTerrainData.baseMapResolution    = sourceTerrainData.baseMapResolution;
            destinationTerrainData.size = sourceTerrainData.size;
            destinationTerrainData.SetHeights(0, 0, sourceTerrainData.GetHeights(0, 0, sourceTerrainData.heightmapWidth, sourceTerrainData.heightmapHeight));

            destinationTerrain.terrainData = destinationTerrainData;
            terrainCollider.terrainData = destinationTerrainData;

            AssetDatabase.CreateAsset(destinationTerrainData, savePath);
        }
    }
}