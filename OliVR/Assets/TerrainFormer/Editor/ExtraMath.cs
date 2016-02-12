using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class ExtraMath {
        public static Vector2 RotatePointAroundPoint(Vector2 point, Vector2 pivotPoint, float angle, float sineOfAngle, float cosineOfAngle) {
            point -= pivotPoint;

            point = new Vector2(point.x * cosineOfAngle - point.y * sineOfAngle,
                point.x * sineOfAngle + point.y * cosineOfAngle);

            point += pivotPoint;

            return point;
        }
    }
}
