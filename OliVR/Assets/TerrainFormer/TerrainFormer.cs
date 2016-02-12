using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    [RequireComponent(typeof(Terrain))]
    public class TerrainFormer : MonoBehaviour {
        void Awake() {
            Destroy(this);
        }
    }
}