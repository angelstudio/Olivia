using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class FlattenCommand : TerrainCommand {
        public FlattenMode mode;
        public float flattenHeight;

        protected override string Name {
            get { return "Flatten"; }
        }

        public FlattenCommand(TerrainData terrainData, float[,] heights, float[,] unmodifiedHeights, float[,] brushSamples) : 
            base(terrainData, heights, unmodifiedHeights, brushSamples) { }

        protected override float OnClick(int x, int y, float brushSample) {
            switch(mode) {
                case FlattenMode.Flatten:
                    if(heights[y, x] < flattenHeight) return unmodifiedHeights[y, x];
                    break;
                case FlattenMode.Extend:
                    if(heights[y, x] > flattenHeight) return unmodifiedHeights[y, x];
                    break;
            }

            return Mathf.Clamp01(heights[y, x] + (flattenHeight - heights[y, x]) * brushSample * 0.5f);
        }

        protected override void OnControlClick(int x, int y, float brushSample) {
            switch(mode) {
                case FlattenMode.Flatten:
                    if(heights[y, x] < flattenHeight)
                        return;
                    break;
                case FlattenMode.Extend:
                    if(heights[y, x] > flattenHeight)
                        return;
                    break;
            }
            heights[y, x] = Mathf.Lerp(unmodifiedHeights[y, x], flattenHeight, -TerrainFormerInspector.Instance.CurrentTotalMouseDelta * brushSample * 0.02f); 
        }

        protected override void OnShiftClick(int x, int y, float brushSample) { }

        protected override void OnShiftClickDown() { }
    }
}