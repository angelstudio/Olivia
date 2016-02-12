using UnityEngine;

namespace JesseStiller.TerrainFormerExtension { 
    internal abstract class TerrainBrush {
        private const float Pi25Percent = Mathf.PI * 0.25f;
        private const float Pi75Percent = Mathf.PI * 0.75f;
        private const float Pi125Percent = Mathf.PI * 1.25f;
        private const float Pi175Percent = Mathf.PI * 1.75f;
        private const float Pi200Percent = Mathf.PI * 2f;

        internal string name;
        internal Texture2D previewTexture;

        /* 
        * Brush Samples are used for the actual modification of the terrain. The only different between the values from 
        * GenerateBrushSamples and GenerateTextureSamples is the the TextureSamples are multiplied by the brush speed and the falloff.
        */
        internal float[,] samples;
        internal float[,] samplesWithSpeed;
        
		// Texture Samples are used for the textures. They don't include the brush speed in their values
		internal abstract float[,] GenerateTextureSamples(int pixelsPerAxis, bool previewTexture);

        protected float[,] GenerateFalloff(int size) {
            float[,] falloffSamples = new float[size, size];
            float halfSize = Mathf.Floor(size * 0.5f);
            float distance;
            float roundness = TerrainFormerInspector.Instance.CurrentBrushSettings.BrushRoundness;
            AnimationCurve falloffCurve = TerrainFormerInspector.Instance.CurrentBrushSettings.brushFalloff;

            if(roundness == 1f) {
                for(int x = 0; x < size; x++) {
                    for(int y = 0; y < size; y++) {
                        distance = GetDistance(x - halfSize, y - halfSize);

                        falloffSamples[x, y] = falloffCurve.Evaluate(1f - (distance / halfSize));
                    }
                }
            } else {
                float midPointRoundnessOffset = halfSize - ((roundness) * halfSize);
                Vector2 midPointRoundnessCircle = new Vector2(midPointRoundnessOffset, midPointRoundnessOffset);
                Vector2 newPoint;
                // If the edge points are beyond halfRoundnessDelta (the radius of the brush), then they aren't within the roundness circle
                float halfRoundnessDelta = halfSize - (roundness * halfSize);
                float roundnessHalfSize = roundness * halfSize;
                Vector2 midPoint = new Vector2(size * 0.5f, size * 0.5f);
                float angle = TerrainFormerInspector.Instance.CurrentBrushSettings.BrushAngle;
                float angleSin = Mathf.Sin(angle * Mathf.Deg2Rad);
                float angleCos = Mathf.Cos(angle * Mathf.Deg2Rad);
                
                for(int x = 0; x < size; x++) {
                    for(int y = 0; y < size; y++) {
                        if(angle != 0f) {
                            newPoint = ExtraMath.RotatePointAroundPoint(new Vector2(x, y), midPoint, angle, angleSin, angleCos);
                        } else {
                            newPoint = new Vector2(x, y);
                        }
                        
                        Vector2 edgePoint = RadialIntersectionWithRadians(Mathf.Atan2(newPoint.x - halfSize, newPoint.y - halfSize), halfSize);
                        float cornerDistance = Vector2.Distance(midPointRoundnessCircle, new Vector2(Mathf.Abs(edgePoint.x), Mathf.Abs(edgePoint.y))) - roundnessHalfSize;

                        distance = GetDistance(x - halfSize, y - halfSize);

                        /**
                        * If the edge points lay within the rounded angle, subtract the cornerDistance from the edgePoint's length. This itself
                        * is the sole reason the edges become rounded.
                        */
                        if(Mathf.Abs(edgePoint.x) >= halfRoundnessDelta && Mathf.Abs(edgePoint.y) >= halfRoundnessDelta) {
                            falloffSamples[x, y] = falloffCurve.Evaluate(1f - (distance / (edgePoint.magnitude - cornerDistance)));
                        } else {
                            falloffSamples[x, y] = falloffCurve.Evaluate(1f - distance / edgePoint.magnitude);
                        }
                    }
                }
            }

            return falloffSamples;
        }
        
        internal Texture2D UpdateSamplesAndMainTexture(int pixelsPerAxis) {
            Texture2D texture = new Texture2D(pixelsPerAxis, pixelsPerAxis, TextureFormat.Alpha8, true);
            texture.filterMode = FilterMode.Trilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            samples = GenerateTextureSamples(pixelsPerAxis, false);

            if(samplesWithSpeed == null || samplesWithSpeed.GetLength(0) != pixelsPerAxis || samplesWithSpeed.GetLength(1) != pixelsPerAxis) {
                samplesWithSpeed = new float[pixelsPerAxis, pixelsPerAxis];
            }
            
            float brushSpeed = TerrainFormerInspector.Instance.CurrentBrushSettings.BrushSpeed;
            
            for(int x = 0; x < pixelsPerAxis; x++) {
                for(int y = 0; y < pixelsPerAxis; y++) {
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, samples[x, y]));

                    samplesWithSpeed[x, y] = samples[x , y] * brushSpeed;
                }
            }
            
            texture.Apply();
            
            return texture;
        }

        internal Texture2D CreatePreviewTexture() {
            previewTexture = new Texture2D(TerrainFormerInspector.settings.brushPreviewSize, TerrainFormerInspector.settings.brushPreviewSize, TextureFormat.Alpha8, false);
            previewTexture.wrapMode = TextureWrapMode.Clamp;
			previewTexture.hideFlags = HideFlags.HideAndDontSave;            
			
			float[,] previewSamples = GenerateTextureSamples(TerrainFormerInspector.settings.brushPreviewSize, true);

            // It's possible to have a null response due to the brush being deleted at the same time others are added.
            if(previewSamples == null) return null;

            for(int x = 0; x < TerrainFormerInspector.settings.brushPreviewSize; x++) {
                for(int y = 0; y < TerrainFormerInspector.settings.brushPreviewSize; y++) {
                    previewTexture.SetPixel(x, y, new Color(1f, 1f, 1f, previewSamples[x, y]));
                }
            }

            previewTexture.Apply();

            return previewTexture;
        }

        internal void UpdateSamplesWithSpeed(int pixelsPerAxis) {
            samplesWithSpeed = GenerateTextureSamples(pixelsPerAxis, false);

            float brushSpeed = TerrainFormerInspector.Instance.CurrentBrushSettings.BrushSpeed;

            for(int x = 0; x < pixelsPerAxis; x++) {
                for(int y = 0; y < pixelsPerAxis; y++) {
                    samplesWithSpeed[x, y] *= brushSpeed;
                }
            }
        }
                
        internal static float GetDistance(float x, float y) {
            return Mathf.Sqrt(Mathf.Pow(x, 2f) + Mathf.Pow(y, 2f));
        }
        
        private static Vector2 RadialIntersectionWithRadians(float radians, float halfSize) {
            radians = (float)System.Math.IEEERemainder(radians, Pi200Percent);

            if(radians < 0) {
                radians += 2 * Mathf.PI;
            }

            return RadialIntersectionWithConstrainedRadians(radians, halfSize);
        }

        // This method requires 0 <= radians < 2 * π.
        private static Vector2 RadialIntersectionWithConstrainedRadians(float radians, float halfSize) {
            float tangent = Mathf.Tan(radians);
            float y = halfSize * tangent;
            float x = halfSize / tangent;

            // An infinite line passing through the center at angle `radians`
            // intersects the right edge at Y coordinate `y` and the left edge
            // at Y coordinate `-y`.

            // Left
            if(radians > Pi125Percent && radians < Pi175Percent) {
                return new Vector2(-halfSize, -x);
            }
            // Bottom
            else if(radians >= Pi175Percent || radians < Pi25Percent) {
                return new Vector2(y, halfSize);
            }
            // Right
            else if(radians >= Pi25Percent && radians <= Pi75Percent) {
                return new Vector2(halfSize, x);
            }
            // Top
            else {
                return new Vector2(-y, -halfSize);
            }
        }
    }
}