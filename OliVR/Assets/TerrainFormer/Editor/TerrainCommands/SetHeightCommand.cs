using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class SetHeightCommand : TerrainCommand {
        public float normalizedHeight;

        protected override string Name {
            get { return "Set Height"; }
        }

        public SetHeightCommand(TerrainData terrainData, float[,] heights, float[,] unmodifiedHeights, float[,] brushSamples) : 
            base(terrainData, heights, unmodifiedHeights, brushSamples) { }

        protected override float OnClick(int x, int y, float brushSample) {
            return Mathf.Clamp01(heights[y, x] + (normalizedHeight - heights[y, x]) * brushSample * 0.5f);
        }

        protected override void OnControlClick(int x, int y, float brushSample) {
	        heights[y, x] = Mathf.Lerp(unmodifiedHeights[y, x], normalizedHeight, 
                -TerrainFormerInspector.Instance.CurrentTotalMouseDelta * brushSample * 0.02f); 
        }

        protected override void OnShiftClick(int x, int y, float brushSample) { }

        protected override void OnShiftClickDown() {
            TerrainFormerInspector.Instance.UpdateSetHeightAtMousePosition();
        }
    }
}