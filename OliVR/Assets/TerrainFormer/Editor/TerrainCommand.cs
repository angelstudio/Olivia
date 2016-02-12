using UnityEngine;
using UnityEditor;

namespace JesseStiller.TerrainFormerExtension {
    internal abstract class TerrainCommand {
        protected abstract string Name { get; }

        protected TerrainData terrainData;
        public float[,] heights;
        protected float[,] unmodifiedHeights;
        public float[,] brushSamples;

        public TerrainCommand(TerrainData terrainData, float[,] heights, float[,] unmodifiedHeights, float[,] brushSamples) {
            this.terrainData = terrainData;
            this.heights = heights;
            this.unmodifiedHeights = unmodifiedHeights;
            this.brushSamples = brushSamples;
        }

        public void Execute(Event currentEvent, TerrainPaintInfo paintInfo) {
            /**
            * IMPORTANT: "(UnityEngine.Object)this" MAY NEED TO BE ADDED TO THE UNDO OBJECT ARRAY IF ANYTHING BAD MIGHT HAPPEN AS A 
            * RESULT OF IT NOT BEING PART OF THE UNDO REGISTRATION ALREADY
            */
            Undo.RegisterCompleteObjectUndo(new Object[] {terrainData}, Name);

            float brushSample;
            if(currentEvent.control) {
                for(int x = 0; x < paintInfo.clippedWidth; x++) {
                    for(int y = 0; y < paintInfo.clippedHeight; y++) {
                        brushSample = brushSamples[x + paintInfo.clippedLeft, y + paintInfo.clippedBottom];
                        if(brushSample == 0f) continue;

                        OnControlClick(x + paintInfo.normalizedLeftOffset, y + paintInfo.normalizedBottomOffset, brushSample);
                    }
                }
            } else if(currentEvent.shift) {
                OnShiftClickDown();
                for(int x = 0; x < paintInfo.clippedWidth; x++) {
                    for(int y = 0; y < paintInfo.clippedHeight; y++) {
                        brushSample = brushSamples[x + paintInfo.clippedLeft, y + paintInfo.clippedBottom];
                        if(brushSample == 0f) continue;

                        OnShiftClick(x + paintInfo.normalizedLeftOffset, y + paintInfo.normalizedBottomOffset, brushSample);
                    }
                }
            } else {
                for(int x = 0; x < paintInfo.clippedWidth; x++) {
                    for(int y = 0; y < paintInfo.clippedHeight; y++) {
                        brushSample = brushSamples[x + paintInfo.clippedLeft, y + paintInfo.clippedBottom];
                        if(brushSample == 0f) continue;

                        heights[y + paintInfo.normalizedBottomOffset, x + paintInfo.normalizedLeftOffset] = 
                            Mathf.Clamp01(OnClick(x + paintInfo.normalizedLeftOffset, y + paintInfo.normalizedBottomOffset, brushSample));
                    }
                }
            }
        }

        protected abstract float OnClick(int normalizedX, int normalizedY, float brushSample);
        protected abstract void OnShiftClick(int normalizedX, int normalizedY, float brushSample);
        protected abstract void OnShiftClickDown();
        protected abstract void OnControlClick(int normalizedX, int normalizedY, float brushSample);
    }
}