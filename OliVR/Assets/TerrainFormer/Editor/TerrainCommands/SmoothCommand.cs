using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class SmoothCommand : TerrainCommand {
        private float[,] copiedHeights;
        private int heightmapWidth;
        private int heightmapHeight;

        public int boxFilterSize;

        protected override string Name {
            get { return "Smooth"; }
        }

        public SmoothCommand(TerrainData terrainData, float[,] heights, float[,] unmodifiedHeights, float[,] brushSamples) : 
            base(terrainData, heights, unmodifiedHeights, brushSamples) {
            copiedHeights = new float[heights.GetLength(0), heights.GetLength(1)];
            heightmapWidth = terrainData.heightmapWidth;
            heightmapHeight = terrainData.heightmapHeight;
            for(int x = 0; x < heightmapWidth; x++) {
                for(int y = 0; y < heightmapHeight; y++) {
                    copiedHeights[x, y] = heights[x, y];
                }
            }
        }
        
        protected override float OnClick(int x, int y, float brushSample) {
            float heightSum = 0f;
            int neighbourCount = 0;
            int positionX, positionY;

            for(int x2 = -boxFilterSize; x2 <= boxFilterSize; x2++) {
                positionX = x + x2;

                // TODO: This neighbour finding should be calculated, not checked every time the command is run
                if(positionX < 0 || positionX >= heightmapWidth) continue;

                for(int y2 = -boxFilterSize; y2 <= boxFilterSize; y2++) {
                    positionY = y + y2;

                    if(positionY < 0 || positionY >= heightmapHeight) continue;

                    heightSum += copiedHeights[positionY, positionX];
                    neighbourCount++;
                }
            }

            /**
            * Apply the smoothed height by performing the following:
            * 1) Get the current height that is being smoothed
            * 2) Calculated the average by dividing neighbourCount by the heightSum
            * 3) Get the difference between the average value and the current value
            * 4) Multiply the difference by the terrain brush samples
            * 5) Add the result onto the existing height value
            *
            * By calculating the difference and multiplying it by a coefficient (the brush samples), this elimates the need for
            * a Lerp function, and makes the smoothing itself a bit quicker.
            */
            copiedHeights[y, x] = copiedHeights[y, x] - ((copiedHeights[y, x] - (heightSum / neighbourCount)) * brushSample * 0.5f);
            return copiedHeights[y, x] - ((copiedHeights[y, x] - (heightSum / neighbourCount)) * brushSample * 0.5f);
        }

        protected override void OnControlClick(int x, int y, float brushSample) {
            heights[y, x] = Mathf.Lerp(unmodifiedHeights[y, x], OnClick(x, y, brushSample), -TerrainFormerInspector.Instance.CurrentTotalMouseDelta * brushSample * 0.015f);
        }

        protected override void OnShiftClick(int x, int y, float brushSample) { }

        protected override void OnShiftClickDown() { }
    }
}