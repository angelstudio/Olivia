using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal static class Utilities {
        internal static string GetLocalAssetPath(string path) {
            return "Assets" + path.Replace(Application.dataPath, "").Replace('\\', '/');
        }

        internal static bool AnimationCurvesEqual(AnimationCurve curveA, AnimationCurve curveB) {
            if(curveA.keys.Length != curveB.keys.Length) return false;

            for(int i = 0; i < curveA.keys.Length; i++) {
                if(curveA.keys[i].inTangent != curveB.keys[i].inTangent) return false;
                if(curveA.keys[i].outTangent != curveB.keys[i].outTangent) return false;
                if(curveA.keys[i].tangentMode != curveB.keys[i].tangentMode) return false;
                if(curveA.keys[i].time != curveB.keys[i].time) return false;
                if(curveA.keys[i].value != curveB.keys[i].value) return false;
            }

            return true;
        }

        // Clamp the falloff curve's values from time 0-1 and value 0-1
        internal static void ClampAnimationCurve(AnimationCurve curve) {
            for(int i = 0; i < curve.keys.Length; i++) {
                Keyframe keyframe = curve.keys[i];
                curve.MoveKey(i, new Keyframe(Mathf.Clamp01(keyframe.time), Mathf.Clamp01(keyframe.value), keyframe.inTangent, keyframe.outTangent));
            }
        }
    }
}
