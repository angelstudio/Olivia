using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class TerrainPaintInfo {
        public int normalizedLeftOffset;
        public int normalizedBottomOffset;
        public int clippedLeft, clippedBottom, clippedWidth, clippedHeight;

        public TerrainPaintInfo(int clippedLeft, int clippedBottom, int clippedWidth, int clippedHeight, int normalizedLeftOffset, int normalizedBottomOffset) {
            this.clippedLeft = clippedLeft;
            this.clippedBottom = clippedBottom;
            this.clippedWidth = clippedWidth;
            this.clippedHeight = clippedHeight;
            this.normalizedLeftOffset = normalizedLeftOffset;
            this.normalizedBottomOffset = normalizedBottomOffset;
        }
    }
}