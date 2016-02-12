using System;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class CustomBrush : TerrainBrush {
        public Texture2D sourceTexture;

        public CustomBrush(string name, Texture2D sourceTexture) {
            this.sourceTexture = sourceTexture;
            this.name = sourceTexture.name;
        }
        
        internal override float[,] GenerateTextureSamples(int size, bool previewTexture) {
            // When adding and deleting brushes at once, the add event is called first and as such might try to update a destroyed texture
            if(sourceTexture == null) return null;

            float angle = TerrainFormerInspector.Instance.CurrentBrushSettings.BrushAngle;

            Vector2 currentPoint;
            float[,] samples;
            if(TerrainFormerInspector.Instance.CurrentBrushSettings.UseFalloffForCustomBrushes) {
                samples = GenerateFalloff(size);
            } else {
                samples = new float[size, size];
            }

            Vector2 newPoint;
            Vector2 midPoint = new Vector2(size * 0.5f, size * 0.5f);
            float sineOfAngle = Mathf.Sin(angle * Mathf.Deg2Rad);
            float cosineOfAngle = Mathf.Cos(angle * Mathf.Deg2Rad);

            bool useFalloffForCustomBrushes = TerrainFormerInspector.Instance.CurrentBrushSettings.UseFalloffForCustomBrushes;
            bool useAlphaFalloff = TerrainFormerInspector.Instance.CurrentBrushSettings.UseAlphaFalloff;
            
            for(int x = 0; x < size; x++) {
                for(int y = 0; y < size; y++) {
                    currentPoint = new Vector2(x, y);

                    // NOTE: Moving these checks out of the loop would easily save ~2ms even on a relatively small brush
                    if(angle == 0f) {
                        newPoint = currentPoint;
                    } else {
                        newPoint = ExtraMath.RotatePointAroundPoint(currentPoint, midPoint, angle, sineOfAngle, cosineOfAngle);
                    }

                    // TODO: This is inverting the grayscale image, it could be baked as inverted
                    if(useFalloffForCustomBrushes && useAlphaFalloff) {
                        samples[x, y] = (1f - sourceTexture.GetPixelBilinear(newPoint.x / size, newPoint.y / size).grayscale) * samples[x, y];
                    } else {
                        samples[x, y] = 1f - sourceTexture.GetPixelBilinear(newPoint.x / size, newPoint.y / size).grayscale * (1f - samples[x, y]);
                    }
                }
            }
            
            return samples;
        }
    }
}