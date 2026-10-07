using System.Collections.Generic;
using UnityEngine;

namespace Factory.Building
{
    // 손가락 드래그로 들어오는 원시 셀 시퀀스를 "한 칸씩 이어지는 정직교 경로"로 바꾸는
    // 순수 로직. MonoBehaviour와 분리해서 실제 입력 없이도 유닛 테스트할 수 있게 한다.
    public static class BeltPathBuilder
    {
        // rawCells(드래그 중 지나온 원시 셀들)를 순서대로 Extend에 흘려 최종 정직교 경로를 만든다.
        // 테스트/오프라인 검증용 진입점 — 실제 드래그 툴은 프레임마다 Extend를 직접 호출한다.
        public static List<Vector2Int> BuildOrthogonalPath(IReadOnlyList<Vector2Int> rawCells)
        {
            var path = new List<Vector2Int>();
            for (int i = 0; i < rawCells.Count; i++)
            {
                Extend(path, rawCells[i]);
            }
            return path;
        }

        // path를 제자리에서 갱신한다: nextCell을 이어붙이거나(인접), 대각선으로 튀었으면
        // 우세 축 방향으로 먼저 한 칸 이어서 자동으로 코너를 만들거나, 이미 지나온 칸으로
        // 되짚어오면 그 지점까지 경로를 잘라낸다(드래그 중 취소).
        public static void Extend(List<Vector2Int> path, Vector2Int nextCell)
        {
            if (path.Count == 0)
            {
                path.Add(nextCell);
                return;
            }

            if (path[path.Count - 1] == nextCell) return; // 같은 칸에 머무름

            int backtrackIndex = path.IndexOf(nextCell);
            if (backtrackIndex >= 0)
            {
                path.RemoveRange(backtrackIndex + 1, path.Count - backtrackIndex - 1);
                return;
            }

            Vector2Int current = path[path.Count - 1];
            Vector2Int delta = nextCell - current;

            while (delta != Vector2Int.zero)
            {
                Vector2Int step = DominantAxisStep(delta);
                current += step;
                delta -= step;

                // 드래그가 빨라서 여러 칸을 건너뛰면 여기서 중간 칸들을 채워 넣는데, 그 중간
                // 칸이 이미 지나온 경로와 겹치면(자기 자신을 가로지름) 그 지점까지 잘라내고
                // 멈춘다. 안 그러면(끝 칸만 되짚기 검사하던 예전 방식) 경로가 스스로를
                // 가로질러 같은 칸이 두 번 들어가는 꼬인 경로가 되고, Commit()이 그 칸을
                // 두 번 등록해버려서 벨트가 중복 설치되는 사고로 이어진다(실제로 겪은 버그 —
                // 빠르게 드래그하면 가끔 벨트가 꼬여서 이상하게 깔림).
                int crossIndex = path.IndexOf(current);
                if (crossIndex >= 0)
                {
                    path.RemoveRange(crossIndex + 1, path.Count - crossIndex - 1);
                    return;
                }

                path.Add(current);
            }
        }

        private static Vector2Int DominantAxisStep(Vector2Int delta)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                return new Vector2Int((int)Mathf.Sign(delta.x), 0);
            }
            return new Vector2Int(0, (int)Mathf.Sign(delta.y));
        }
    }
}
