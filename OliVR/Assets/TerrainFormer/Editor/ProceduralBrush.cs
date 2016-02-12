using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class ProceduralBrush : TerrainBrush {
        public ProceduralBrush(string name) {
            this.name = name;
        }

        internal override float[,] GenerateTextureSamples(int pixelsPerAxis, bool previewTexture) {
            return GenerateFalloff(pixelsPerAxis);
        }
    }
}