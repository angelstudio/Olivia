using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class RaiseOrLowerCommand : TerrainCommand {
        protected override string Name {
            get { return "Raise/Lower"; }
        }

        public RaiseOrLowerCommand(TerrainData terrainData, float[,] heights, float[,] unmodifiedHeights, float[,] brushSamples) : 
            base(terrainData, heights, unmodifiedHeights, brushSamples) { }

        protected override float OnClick(int x, int y, float brushSample) {
            return heights[y, x] + brushSample * 0.01f;
        }

        protected override void OnControlClick(int x, int y, float brushSample) {
            heights[y, x] = Mathf.Clamp01(unmodifiedHeights[y, x] + brushSample * -TerrainFormerInspector.Instance.CurrentTotalMouseDelta * 0.005f);
        }

        protected override void OnShiftClick(int x, int y, float brushSample) {
            heights[y, x] -= brushSample * 0.01f;
        }

        protected override void OnShiftClickDown() { }
    }
}