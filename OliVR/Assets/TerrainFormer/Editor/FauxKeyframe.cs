using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class FauxKeyframe {
        public float inTangent;
        public float outTangent;
        public int tangentMode;
        public float time;
        public float value;

        public FauxKeyframe() { }

        public FauxKeyframe(Keyframe keyframe) {
            time = keyframe.time;
            value = keyframe.value;
            tangentMode = keyframe.tangentMode;
            inTangent = keyframe.inTangent;
            outTangent = keyframe.outTangent;
        }

        /*
        public FauxKeyframe(float time, float value) {
            this.time = time;
            this.value = value;
        }

        public FauxKeyframe(float time, float value, float inTangent, float outTangent) {
            this.time = time;
            this.value = value;
            this.inTangent = inTangent;
            this.outTangent = outTangent;
        }
        */
    }
}
