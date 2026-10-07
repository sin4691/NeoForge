using Factory.Simulation;
using UnityEngine;

namespace Factory.Rendering
{
    // 카메라 화면 네 모서리를 바닥(y=0)에 레이캐스트해서, 지금 보이는 영역을 감싸는 셀 사각형을
    // 구한다. Bae님의 FloorViewportCuller와 같은 원리 — FactoryViewportCuller(벨트/기계 컬링)와
    // OreDepositSpawner(광물 노드 비주얼 지연 생성)가 똑같은 계산이 필요해서 한 곳에 뒀다.
    public static class CameraViewBounds
    {
        private static readonly Plane Ground = new Plane(Vector3.up, Vector3.zero);
        private static readonly Vector2[] Corners =
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
        };

        // paddingCells: 화면 경계 바깥으로 더 넓혀서 잡는 여유(칸 단위) — 카메라가 빠르게 움직일 때
        // 딱 경계에서 팝인하는 게 눈에 띄지 않도록. 하늘을 보는 등 한 모서리도 바닥에 안 맞으면 false.
        public static bool TryCompute(Camera camera, float paddingCells, out RectInt bounds)
        {
            bounds = default;
            if (camera == null) return false;

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            bool hitAny = false;

            for (int i = 0; i < Corners.Length; i++)
            {
                Ray ray = camera.ViewportPointToRay(Corners[i]);
                if (!Ground.Raycast(ray, out float distance)) continue;
                Vector3 p = ray.GetPoint(distance);
                hitAny = true;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }
            if (!hitAny) return false;

            float pad = paddingCells * GridUtility.CellSize;
            Vector2Int min = GridUtility.WorldToCell(new Vector3(minX - pad, 0f, minZ - pad));
            Vector2Int max = GridUtility.WorldToCell(new Vector3(maxX + pad, 0f, maxZ + pad));
            bounds = new RectInt(min.x, min.y, Mathf.Max(0, max.x - min.x), Mathf.Max(0, max.y - min.y));
            return true;
        }
    }
}
