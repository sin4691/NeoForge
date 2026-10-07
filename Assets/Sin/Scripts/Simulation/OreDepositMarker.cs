using System.Collections.Generic;
using UnityEngine;

namespace Factory.Simulation
{
    // 씬에 직접 놓는 광물 노드 마커. OreDepositSpawner.cs 코드를 열어 좌표를 손으로 적어넣는
    // 대신, 이 컴포넌트를 씬에 배치하고 Scene 뷰에서 위치를 옮긴 뒤 Inspector에서 depositId만
    // 골라주면 게임 시작 시 그 칸에 자동 등록된다.
    //
    // 쓰는 법: 빈 GameObject 만들고 이 컴포넌트 추가 → Scene 뷰에서 원하는 칸 위로 드래그 →
    // depositId에 Assets/Resources/GameData/OreDeposits 안 애셋의 depositId 문자열 입력
    // (예: "CopperOreDeposit"). 우클릭 메뉴의 "칸 중앙에 맞추기"로 정확히 칸 중앙에 스냅 가능.
    //
    // 한 칸이 아니라 여러 칸을 깔고 싶을 때 두 가지 방법이 있다:
    //  - size: 사각형 범위(가로, 세로 칸 수). 마커가 놓인 칸이 왼쪽 아래 모서리. 기본값 (1,1)은 한 칸.
    //  - 붓으로 칠하기: Inspector의 "칠하기 모드"를 켜고 Scene 뷰에서 칸을 클릭/드래그해서 임의의
    //    모양으로 칠한다(Shift로 지우기). 칠한 칸은 마커 칸 기준 오프셋으로 저장되고
    //    (OreDepositMarkerEditor 참고), 하나라도 칠해져 있으면 size는 무시된다.
    public class OreDepositMarker : MonoBehaviour
    {
        [Tooltip("Assets/Resources/GameData/OreDeposits 안 OreDepositDef 애셋의 depositId와 정확히 같은 문자열.")]
        public string depositId;

        [Tooltip("이 마커가 채우는 사각형 범위(가로, 세로 칸 수). 마커가 놓인 칸이 왼쪽 아래 모서리. 1x1이면 한 칸. 칠한 칸이 있으면 무시된다.")]
        public Vector2Int size = Vector2Int.one;

        // 붓으로 칠한 칸들 — 마커가 놓인 칸을 (0,0)으로 본 상대 좌표라 마커를 옮기면 모양째 같이
        // 움직인다. 음수 좌표(마커 왼쪽/아래로 칠한 칸)도 가능하다. 손으로 직접 편집하지 말고
        // Inspector의 칠하기 모드를 쓰는 걸 권장한다.
        [SerializeField] private List<Vector2Int> paintedOffsets = new List<Vector2Int>();

        public List<Vector2Int> PaintedOffsets => paintedOffsets;

        public Vector2Int Cell => GridUtility.WorldToCell(transform.position);

        // 이 마커가 덮는 모든 칸. 칠한 칸이 있으면 그것만, 없으면 size 사각형.
        // size가 0 이하로 잘못 들어가도 최소 한 칸은 보장한다.
        public IEnumerable<Vector2Int> Cells
        {
            get
            {
                Vector2Int origin = Cell;

                if (paintedOffsets != null && paintedOffsets.Count > 0)
                {
                    for (int i = 0; i < paintedOffsets.Count; i++)
                    {
                        yield return origin + paintedOffsets[i];
                    }
                    yield break;
                }

                int width = Mathf.Max(1, size.x);
                int height = Mathf.Max(1, size.y);
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        yield return new Vector2Int(origin.x + x, origin.y + y);
                    }
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            size = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Vector3 labelPos = GridUtility.CellToWorldCenter(Cell, 0.45f);
            int count = 0;
            foreach (var cell in Cells)
            {
                Gizmos.DrawWireCube(GridUtility.CellToWorldCenter(cell, 0.05f),
                    new Vector3(0.95f, 0.1f, 0.95f) * GridUtility.CellSize);
                count++;
            }

            UnityEditor.Handles.Label(labelPos,
                (string.IsNullOrEmpty(depositId) ? "(depositId 비어있음)" : depositId)
                + (count > 1 ? $"  ({count}칸)" : string.Empty));
        }

        [ContextMenu("칸 중앙에 맞추기")]
        private void SnapToCell()
        {
            transform.position = GridUtility.CellToWorldCenter(Cell, 0f);
        }
#endif
    }
}
