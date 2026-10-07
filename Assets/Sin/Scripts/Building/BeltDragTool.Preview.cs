using System.Collections.Generic;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 드래그 중 화면에 보여주는 미리보기 전용 — RebuildPreview가 매 드래그 스텝마다 다시
    // 그리고, 실제 배선(Commit)에는 전혀 관여하지 않는다.
    public partial class BeltDragTool
    {
        private void RebuildPreview()
        {
            ClearPreview();
            Color color = HasValidEndpointPreview() ? previewColor : invalidPreviewColor;
            List<Vector2Int> renderPath = BuildRenderPathForPreview(out int renderOffset);

            // 시작/끝이 기존 벨트 칸에 직접 겹치면(옆칸 자동연결이 아니라 그 칸 자체) 이 칸의
            // 모양은 아래 PreviewEndpointBend → PreviewOnBuildingBeltBend가 전담해서 그린다 —
            // 여기서도 똑같이 그 칸에 일반 조각을 얹으면 진짜 숨겨진 것과 무관하게 새로 그린
            // "밋밋한" 조각과 "휘어진" 조각이 같은 칸에 겹쳐 보인다(사용자 보고 버그).
            var grid = driver.World.Grid;
            int lastIdx = path.Count - 1;
            bool skipStartTile = path.Count >= 2 && grid.TryGetOccupant(path[0], out var startTileOcc) && startTileOcc.Type == CellOccupantType.Belt;
            bool skipEndTile = path.Count >= 2 && grid.TryGetOccupant(path[lastIdx], out var endTileOcc) && endTileOcc.Type == CellOccupantType.Belt;

            for (int k = 0; k < path.Count; k++)
            {
                if ((k == 0 && skipStartTile) || (k == lastIdx && skipEndTile)) continue;
                ComputeCellSpan(renderPath, k + renderOffset, out Vector3 entry, out Vector3 exit, out Vector3? bend);
                if (bend.HasValue)
                {
                    if (cornerPrefab != null)
                    {
                        previewStrips.Add(SpawnBeltCorner(bend.Value, bend.Value - entry, exit - bend.Value, color, transform, keepMaterial: false));
                    }
                    else
                    {
                        previewStrips.Add(BuildVisuals.CreateStrip(entry, bend.Value, previewThickness, color, transform, prefab: stripPrefab, flatSurfaceY: beltSurfaceY));
                        previewStrips.Add(BuildVisuals.CreateStrip(bend.Value, exit, previewThickness, color, transform, prefab: stripPrefab, flatSurfaceY: beltSurfaceY));
                    }
                }
                else
                {
                    previewStrips.Add(BuildVisuals.CreateStrip(entry, exit, previewThickness, color, transform, prefab: stripPrefab, flatSurfaceY: beltSurfaceY));
                }
            }

            if (path.Count >= 2)
            {
                int last = path.Count - 1;
                PreviewEndpointBend(path[0], path[1], true, color);
                PreviewEndpointBend(path[last], path[last - 1], false, color);
            }
        }

        // 시작/끝 처리를 분기한다 — myCell 자체가 이미 기존 벨트 칸이면(직접 겹침, 예:
        // 드래그가 남은 코너 위에서 끝남) PreviewOnBuildingBeltBend로, 빈 칸이고 옆에
        // 유효 포트가 있을 뿐이면(옆칸 자동연결) 기존 PreviewAdjacentBeltBend로 보낸다.
        // 둘을 하나로 합치지 않은 이유: "얘 입장에서 내가 새로 붙는 칸"이 전자는 myCell
        // 자신이고 후자는 neighborCell이라 기준점이 달라서, 억지로 합치면 오히려 헷갈린다.
        private void PreviewEndpointBend(Vector2Int myCell, Vector2Int fromCell, bool isStart, Color color)
        {
            if (driver.World.Grid.TryGetOccupant(myCell, out var onOccupant) && onOccupant.Type == CellOccupantType.Belt)
            {
                PreviewOnBuildingBeltBend(myCell, onOccupant, fromCell, isStart, color);
                return;
            }
            PreviewAdjacentBeltBend(myCell, fromCell, isStart, color);
        }

        // 드래그 끝점이 기존 벨트 칸에 "직접" 겹치는 경우(예: 코어→코너→직선→코너로 드래그해서
        // 남아있던 코너 자체 위에서 손을 뗄 예정) 전용. PreviewAdjacentBeltBend는 myCell이
        // 빈 칸이고 그 옆에 기존 벨트가 있는 경우만 다뤄서, 이 경우엔 아예 안 걸리는 바람에
        // 기존 벨트의 진짜 비주얼이 안 숨겨진 채 새 미리보기와 겹쳐 보이는 버그가 있었다
        // (사용자 보고: 손을 떼야만 올바른 코너로 정리되고, 드래그 중엔 겹쳐 보임).
        private void PreviewOnBuildingBeltBend(Vector2Int myCell, CellOccupant occupant, Vector2Int fromCell, bool isStart, Color color)
        {
            var segment = driver.World.Segments[occupant.InstanceIndex];
            if (segment == null) return;

            // isStart → 이 기존 벨트를 드래그의 소스로 씀 → 얘 입장에선 상류는 그대로,
            // 하류가 새 경로(fromCell)로 바뀐다. 아니면(타겟으로 씀) 하류는 그대로, 상류가
            // fromCell로 바뀐다. Commit()의 startRole==Source일 때 NextSegmentId가 이미
            // 있으면 거부하는 가드와 같은 전제라, 여기서 이미 하류가 있는 걸 가정해도 안전.
            bool hasOther = isStart
                ? TryGetUpstreamCell(segment, myCell, out var otherCell)
                : TryGetDownstreamCell(segment, myCell, out otherCell);
            if (!hasOther) return; // 반대편도 없으면 휘어질 기준 자체가 없다 — 그냥 안 건드린다.

            var miniPath = new List<Vector2Int>(3);
            if (isStart) { miniPath.Add(otherCell); miniPath.Add(myCell); miniPath.Add(fromCell); }
            else { miniPath.Add(fromCell); miniPath.Add(myCell); miniPath.Add(otherCell); }

            ComputeCellSpan(miniPath, 1, out Vector3 entry, out Vector3 exit, out Vector3? bend);
            SpawnHiddenNeighborPreview(occupant.InstanceIndex, entry, exit, bend, color);
        }

        // 옆칸 자동연결 대상이 "기존 벨트"면, 이 드래그가 실제로 이어질 경우 그 벨트가 어떻게
        // 휘어 보일지 임시로 겹쳐 보여준다 — 진짜 오브젝트(Belt_id)는 숨기기만 하고 안
        // 건드리고, ClearPreview에서 다시 보이게 복원한다(취소해도 실제 모양/데이터는
        // 그대로 남는다 — 커밋 시점에만 RerenderSegmentStrip이 진짜로 다시 그린다).
        private void PreviewAdjacentBeltBend(Vector2Int myCell, Vector2Int fromCell, bool isStart, Color color)
        {
            if (!TryFindAdjacentOccupant(myCell, fromCell, isStart, out var occupant, out var role, out _, out var neighborCell))
                return;
            if (occupant.Type != CellOccupantType.Belt || role == EndpointRole.None) return;

            var segment = driver.World.Segments[occupant.InstanceIndex];
            if (segment == null) return;

            // isStart(내가 얘를 소스로 씀) → 얘 입장에선 내 칸이 새 하류. 아니면(얘가 내
            // 타겟) 얘 입장에선 내 칸이 새 상류. 반대편(안 바뀌는 쪽)은 지금 실제 데이터
            // 그대로(TryGetUpstreamCell/TryGetDownstreamCell) 조회해도 안전하다 — 미리보기
            // 단계에선 아직 아무 것도 실제로 안 바뀌었으니까.
            bool myCellIsItsDownstream = isStart;
            bool hasOther = myCellIsItsDownstream
                ? TryGetUpstreamCell(segment, neighborCell, out var otherCell1)
                : TryGetDownstreamCell(segment, neighborCell, out otherCell1);
            if (!hasOther) return; // 반대편도 없으면 휘어질 기준 자체가 없다 — 그냥 안 건드린다.

            var miniPath = new List<Vector2Int>(3);
            if (myCellIsItsDownstream) { miniPath.Add(otherCell1); miniPath.Add(neighborCell); miniPath.Add(myCell); }
            else { miniPath.Add(myCell); miniPath.Add(neighborCell); miniPath.Add(otherCell1); }

            ComputeCellSpan(miniPath, 1, out Vector3 entry, out Vector3 exit, out Vector3? bend);
            SpawnHiddenNeighborPreview(occupant.InstanceIndex, entry, exit, bend, color);
        }

        // PreviewOnBuildingBeltBend/PreviewAdjacentBeltBend 공통 마무리 — 기존 세그먼트의
        // 진짜 오브젝트를 숨기고, 계산된 진입/이탈/꺾임 지점으로 임시 미리보기 조각을 그린다.
        private void SpawnHiddenNeighborPreview(int segmentId, Vector3 entry, Vector3 exit, Vector3? bend, Color color)
        {
            var existingVisual = GameObject.Find($"Belt_{segmentId}");
            if (existingVisual != null && existingVisual.activeSelf)
            {
                existingVisual.SetActive(false);
                hiddenNeighborVisuals[segmentId] = existingVisual;
            }

            if (bend.HasValue && cornerPrefab != null)
            {
                previewStrips.Add(SpawnBeltCorner(bend.Value, bend.Value - entry, exit - bend.Value, color, transform, keepMaterial: false));
            }
            else if (bend.HasValue)
            {
                previewStrips.Add(BuildVisuals.CreateStrip(entry, bend.Value, previewThickness, color, transform, prefab: stripPrefab, flatSurfaceY: beltSurfaceY));
                previewStrips.Add(BuildVisuals.CreateStrip(bend.Value, exit, previewThickness, color, transform, prefab: stripPrefab, flatSurfaceY: beltSurfaceY));
            }
            else
            {
                previewStrips.Add(BuildVisuals.CreateStrip(entry, exit, previewThickness, color, transform, prefab: stripPrefab, flatSurfaceY: beltSurfaceY));
            }
        }

        // Seo님의 BeltConnectionFeedback이 자기 UI 판정 뒤에 "자원은 충분한지" 마지막 게이트로
        // 그대로 불러쓴다 — 별도로 다시 구현하면 판정이 어긋나는 버그가 또 나기 때문에 공개함.
        public bool HasValidEndpointPreview()
        {
            if (driver == null || driver.World == null || path.Count < 1) return false;
            if (TouchesGeneratorFromInvalidSide()) return false;

            if (path.Count == 1)
            {
                if (ExternalCellBlocked?.Invoke(path[0]) ?? false) return false;
                if (!TryResolveSingleCell(out _, out _, out _, out _)) return false;
                return CanAffordBeltCost(1);
            }

            var grid = driver.World.Grid;
            int last = path.Count - 1;

            // 시작/끝 모두 "그 칸에 직접 닿았는지"(OnBuilding, 칸 정리용)와 "연결 대상을
            // 찾았는지"(Resolved, 직접 닿았거나 바로 옆칸이 유효 포트라 자동 연결된 경우 둘
            // 다 포함)를 구분한다 — Commit()과 반드시 같은 기준으로 판단해야 한다. 시작도
            // 끝과 대칭으로 "기계 위에서 눌러야만 시작된다"는 제약을 없앤다(사용자 요청).
            bool startOnBuilding = grid.IsOccupied(path[0]);
            CellOccupant start = default;
            if (startOnBuilding) TryGetOccupantForConnection(path[0], path[1], out start);
            EndpointRole startRole = EndpointRole.None;
            bool startFixed = false;

            if (startOnBuilding)
            {
                startRole = ResolveEndpointRole(start, path[1], true, out startFixed);
            }
            else if (TryFindAdjacentOccupant(path[0], path[1], true, out start, out startRole, out startFixed, out _))
            {
                // resolved via adjacency
            }
            if (startRole == EndpointRole.None) return false;

            bool endOnBuilding = grid.IsOccupied(path[last]);
            CellOccupant end = default;
            if (endOnBuilding) TryGetOccupantForConnection(path[last], path[last - 1], out end);
            EndpointRole endRole = EndpointRole.None;
            bool endFixed = false;
            bool endResolved = false;

            if (endOnBuilding)
            {
                endRole = ResolveEndpointRole(end, path[last - 1], false, out endFixed);
                if (endRole == EndpointRole.None) return false;
                endResolved = true;
            }
            else if (TryFindAdjacentOccupant(path[last], path[last - 1], false, out end, out endRole, out endFixed, out _))
            {
                endResolved = true;
            }

            // Commit()과 같은 기준 — 입력 포트에서 시작했는데 끝이 안 닿으면 역방향 벨트가 되므로 무효.
            if (!endResolved && startRole != EndpointRole.Source) return false;

            if (endResolved && startRole == endRole)
            {
                if (!startFixed && endFixed) startRole = Opposite(endRole);
                else if (startFixed && !endFixed) endRole = Opposite(startRole);
                else return false;
            }
            if (startRole != EndpointRole.Source && endRole == EndpointRole.Source)
            {
                (start, end) = (end, start);
                (startRole, endRole) = (endRole, startRole);
            }
            if (startRole == EndpointRole.Source && start.Type == CellOccupantType.Belt
                && driver.World.Segments[start.InstanceIndex].NextSegmentId.HasValue) return false;

            int first = startOnBuilding ? 1 : 0;
            int final = endOnBuilding ? last - 1 : last;
            for (int i = first; i <= final; i++)
            {
                if (grid.IsOccupied(path[i]) || (ExternalCellBlocked?.Invoke(path[i]) ?? false)) return false;
            }

            // 실제로 새로 놓일 칸 수만큼 콘크리트가 있는지도 미리보기에 반영 — 기계 고스트가
            // 자원 부족 시 빨갛게 뜨는 것과 같은 원칙(BuildCostUtility 공용 로직).
            if (!CanAffordBeltCost(final - first + 1)) return false;
            return true;
        }

        // 발전기 연료 포트는 본체의 한 면만 유효하다. 드래그 경로가 발전기 본체를
        // 밟을 때, 실제 포트 셀이 아닌 면에서 들어오거나 나가면 미리보기부터 거부한다.
        public bool TouchesGeneratorFromInvalidSide()
        {
            for (int p = 0; p < driver.World.Processors.Count; p++)
            {
                var processor = driver.World.Processors[p];
                if (processor == null || !processor.IsGeneratorFuelPort) continue;
                int index = path.IndexOf(processor.Anchor);
                var inputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint,
                    processor.Facing, isOutputSide: false);

                // 포트 셀은 본체와 별도 칸이라, 사용자가 그 칸에서 드래그를 끝내는 경우도
                // 반드시 입력 면인지 검사해야 한다. 이전 코드는 본체 칸을 밟은 경우만 검사했다.
                Vector2Int first = path[0];
                Vector2Int last = path[path.Count - 1];
                if (IsAdjacentToGenerator(processor, first) && !inputs.Contains(first)) return true;
                if (IsAdjacentToGenerator(processor, last) && !inputs.Contains(last)) return true;

                if (index < 0) continue;
                if (index != 0 && index != path.Count - 1) return true;

                Vector2Int touching = index == 0 ? path[1] : path[path.Count - 2];
                if (!inputs.Contains(touching)) return true;
            }
            return false;
        }

        private static bool IsAdjacentToGenerator(ProcessorInstance processor, Vector2Int cell)
        {
            foreach (Vector2Int occupied in GridUtility.GetFootprintCells(processor.Anchor, processor.Footprint))
            {
                int distance = Mathf.Abs(cell.x - occupied.x) + Mathf.Abs(cell.y - occupied.y);
                if (distance == 1) return true;
            }
            return false;
        }

        private void ClearPreview()
        {
            for (int i = 0; i < previewStrips.Count; i++) Destroy(previewStrips[i]);
            previewStrips.Clear();
            foreach (var hidden in hiddenNeighborVisuals.Values)
            {
                if (hidden != null) hidden.SetActive(true);
            }
            hiddenNeighborVisuals.Clear();
        }

        // 드래그 중 미리보기 전용 — 시작/끝이 기계 칸에 직접 안 닿고 바로 옆이면, 그 기계 칸을
        // 양 끝에 가상으로 붙인 경로를 돌려줘서 ComputeCellSpan이 기계 쪽으로 휘어 그리게 한다.
        // Commit()의 배선 판정(역할 충돌 해소, 방향 뒤집기 등)까지는 필요 없다 — 그냥 "지금 이
        // 근처에 뭐가 있어 보이는지"만 알면 되는 순수 시각용이라 훨씬 가볍다.
        private List<Vector2Int> BuildRenderPathForPreview(out int renderOffset)
        {
            renderOffset = 0;
            if (driver == null || driver.World == null) return path;

            // 한 칸짜리는 양옆 기계 칸을 가상으로 붙여서 그 사이로 이어지는 모양(직선/코너)을 그린다.
            if (path.Count == 1
                && TryResolveSingleCell(out _, out var singleStart, out _, out var singleEnd))
            {
                renderOffset = 1;
                return new List<Vector2Int> { singleStart, path[0], singleEnd };
            }

            if (path.Count < 2) return path;
            var grid = driver.World.Grid;
            int last = path.Count - 1;

            Vector2Int? startNeighbor = null;
            if (!grid.IsOccupied(path[0])
                && TryFindAdjacentOccupant(path[0], path[1], true, out _, out _, out _, out var sNeighbor))
            {
                startNeighbor = sNeighbor;
            }

            Vector2Int? endNeighbor = null;
            if (!grid.IsOccupied(path[last])
                && TryFindAdjacentOccupant(path[last], path[last - 1], false, out _, out _, out _, out var eNeighbor))
            {
                endNeighbor = eNeighbor;
            }

            if (!startNeighbor.HasValue && !endNeighbor.HasValue) return path;

            var renderPath = new List<Vector2Int>(path);
            if (endNeighbor.HasValue) renderPath.Add(endNeighbor.Value);
            if (startNeighbor.HasValue)
            {
                renderPath.Insert(0, startNeighbor.Value);
                renderOffset = 1;
            }
            return renderPath;
        }
    }
}
