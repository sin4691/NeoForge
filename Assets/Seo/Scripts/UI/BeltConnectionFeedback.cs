using System.Collections.Generic;
using System.Reflection;
using Factory.Building;
using Factory.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PortRole = Factory.Building.BeltDragTool.EndpointRole;

namespace Seo.UI
{
    // BeltDragTool의 내부 상태를 읽기만 하는 UI 어댑터. 벨트 연결 규칙 자체(포트 판정, 자원
    // 체크)는 BeltDragTool의 공개 메서드를 그대로 가져다 쓰고, 여기서는 그 결과를 플레이어가
    public sealed class BeltConnectionFeedback : MonoBehaviour
    {
        private static readonly FieldInfo PathField = typeof(BeltDragTool).GetField("path", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PreviewStripsField = typeof(BeltDragTool).GetField("previewStrips", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DraggingField = typeof(BeltDragTool).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DriverField = typeof(BeltDragTool).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);

        private BuildInputRouter router;
        private BeltDragTool beltTool;
        private SimulationDriver driver;
        private Camera targetCamera;
        private GameObject panelRoot;
        private TMP_Text messageText;
        private EndpointBadge startBadge;
        private EndpointBadge endBadge;
        private bool wasDragging;
        private bool lastPathValid;
        private string releaseMessage;
        private Color releaseColor;
        private float releaseMessageUntil;
        private float nextDiscovery;
        private Color? lastTintColor;
        private int lastTintStripCount = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoad()
        {
            SceneManager.sceneLoaded -= CreateRuntimeInstance;
            SceneManager.sceneLoaded += CreateRuntimeInstance;
        }

        private static void CreateRuntimeInstance(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Main") return;
            if (FindFirstObjectByType<BeltConnectionFeedback>() != null) return;
            new GameObject("[Seo] Belt Connection Feedback").AddComponent<BeltConnectionFeedback>();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextDiscovery)
            {
                Discover();
                nextDiscovery = Time.unscaledTime + 0.5f;
            }

            if (router == null || beltTool == null || driver == null || driver.World == null || !EnsurePanel()) return;

            bool beltMode = router.CurrentMode == BuildInputRouter.Mode.Belt;
            panelRoot.SetActive(beltMode);
            if (!beltMode)
            {
                HideEndpointBadges();
                wasDragging = false;
                return;
            }

            bool dragging = DraggingField != null && (bool)DraggingField.GetValue(beltTool);
            var path = PathField?.GetValue(beltTool) as List<Vector2Int>;

            if (!dragging)
            {
                if (wasDragging)
                {
                    releaseMessage = lastPathValid
                        ? "벨트 연결 완료"
                        : "연결되지 않음 · 빨간 안내를 확인하세요";
                    releaseColor = lastPathValid ? SeoUITheme.Current.Success : SeoUITheme.Current.Danger;
                    releaseMessageUntil = Time.unscaledTime + 1.6f;
                }

                wasDragging = false;
                lastPathValid = false;
                lastTintColor = null;
                lastTintStripCount = -1;
                HideEndpointBadges();
                SetMessage(Time.unscaledTime < releaseMessageUntil
                    ? releaseMessage
                    : "벨트 · 기계 출력 포트에서 입력 포트까지 드래그", Time.unscaledTime < releaseMessageUntil
                        ? releaseColor
                        : SeoUITheme.Current.Primary);
                return;
            }

            wasDragging = true;
            ValidatePath(path);
        }

        private void Discover()
        {
            if (router == null) router = FindFirstObjectByType<BuildInputRouter>();
            if (beltTool == null) beltTool = FindFirstObjectByType<BeltDragTool>();
            if (driver == null && beltTool != null) driver = DriverField?.GetValue(beltTool) as SimulationDriver;
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
            if (targetCamera == null) targetCamera = Camera.main;
        }

        private bool EnsurePanel()
        {
            if (panelRoot != null) return true;
            var canvasObject = GameObject.Find("HUDCanvas");
            if (canvasObject == null) return false;

            Transform parent = canvasObject.transform.Find("SafeArea") ?? canvasObject.transform;
            var panel = SeoUIFactory.CreatePanel(parent, "SeoBeltFeedback", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 306f), new Vector2(820f, 72f),
                new Color(0.02f, 0.08f, 0.12f, 0.97f));
            panel.rectTransform.pivot = new Vector2(0.5f, 0f);
            panelRoot = panel.gameObject;
            messageText = SeoUIFactory.CreateTMPText(panel.transform, "Message", string.Empty, 20,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            messageText.rectTransform.offsetMin = new Vector2(24f, 8f);
            messageText.rectTransform.offsetMax = new Vector2(-24f, -8f);
            panelRoot.SetActive(false);
            return true;
        }

        private void ValidatePath(List<Vector2Int> path)
        {
            lastPathValid = false;
            if (path == null || path.Count == 0)
            {
                HideEndpointBadges();
                SetMessage("출력 포트 또는 기존 벨트에서 드래그를 시작하세요", SeoUITheme.Current.Warning);
                return;
            }

            var grid = driver.World.Grid;
            Vector2Int startCell = path[0];
            bool startOnBuilding = grid.IsOccupied(startCell);
            CellOccupant startOccupant = default;
            if (startOnBuilding)
            {
                // 크로스 타일(IsCrossable)은 한 칸에 독립된 두 축(BeltDragTool.Crossing.cs 참고)이
                // 있어서, 어느 방향에서 접근했는지로 실제 어느 세그먼트를 말하는지가 갈린다 —
                // BeltDragTool의 보정을 그대로 물어본다(따로 구현하면 UI만 어긋나는 버그가 난다).
                Vector2Int approachFrom = path.Count >= 2 ? path[1] : startCell;
                beltTool.TryGetOccupantForConnection(startCell, approachFrom, out startOccupant);
            }

            // 아직 방향을 모를 수 있는(1칸) 상태에서는 "이 칸 자체나 그 근처에 뭐라도
            // 있는지"만 빠르게 훑어 배지/문구를 정한다 — 진짜 역할 판정은 path.Count>=2가
            // 보장된 뒤 실제 드래그 방향으로 다시 한다(아래). BeltDragTool의 판정을 그대로
            // 물어본다 — 자기 나름으로 grid.IsOccupied만 보면 옆칸 자동연결이 가능한데도
            // UI만 "빈 공간이라 불가"로 잘못 뜨는 불일치가 난다(실제로 한 번 이렇게 어긋났었다).
            bool startHasAnchor = startOnBuilding;
            if (!startHasAnchor && beltTool != null)
            {
                Vector2Int fromCell = path.Count >= 2 ? path[1] : startCell;
                startHasAnchor = beltTool.TryFindAdjacentOccupant(startCell, fromCell, true, out _, out _, out _, out _);
            }

            ShowBadge(ref startBadge, startCell, "시작", startHasAnchor ? SeoUITheme.Current.Primary : SeoUITheme.Current.Danger);

            if (!startHasAnchor)
            {
                HideBadge(ref endBadge);
                SetMessage("연결 불가 · 빈 공간(근처에 연결 가능한 기계도 없음)에서는 벨트를 시작할 수 없습니다", SeoUITheme.Current.Danger);
                return;
            }

            if (path.Count < 2)
            {
                HideBadge(ref endBadge);

                // 두 기계 사이가 딱 한 칸이면 드래그 없이 한 칸만 놓아도 이어진다 — 판정은
                // BeltDragTool에 그대로 물어본다(따로 구현하면 UI만 어긋나는 버그가 난다).
                if (beltTool != null && beltTool.TryResolveSingleCell(out var singleStart, out _, out var singleEnd, out _))
                {
                    if (beltTool.HasValidEndpointPreview())
                    {
                        lastPathValid = true;
                        SetMessage($"연결 가능 · {EndpointName(singleStart)} → {EndpointName(singleEnd)} · 1칸"
                            + ConnectionCountSuffix(singleEnd, PortRole.Target), SeoUITheme.Current.Success);
                    }
                    else
                    {
                        SetMessage("연결 불가 · 자원이 부족합니다", SeoUITheme.Current.Danger);
                    }
                    return;
                }

                SetMessage("시작점 선택됨 · 연결할 방향으로 한 칸 이상 드래그하세요", SeoUITheme.Current.Primary);
                return;
            }

            // 발전기 연료 포트는 한쪽 면만 유효 — BeltDragTool의 판정을 그대로 물어본다
            // (여기서 따로 다시 구현하면 이 특수 케이스를 놓치기 쉽다: 실제로 한 번 놓쳤었다).
            if (beltTool != null && beltTool.TouchesGeneratorFromInvalidSide())
            {
                HideBadge(ref endBadge);
                SetMessage("연결 불가 · 발전기는 지정된 입력 면에서만 연결할 수 있습니다", SeoUITheme.Current.Danger);
                return;
            }

            // 여기부턴 path.Count>=2가 보장되니, 실제 드래그 방향(path[1])으로 최종 역할을
            // 확정한다 — 위 startHasAnchor 판정은 1칸일 때 sentinel로 대충 봤을 수 있다.
            PortRole startRole;
            bool startFixed;
            if (startOnBuilding)
            {
                startRole = beltTool.ResolveEndpointRole(startOccupant, path[1], true, out startFixed);
            }
            else
            {
                beltTool.TryFindAdjacentOccupant(startCell, path[1], true, out startOccupant, out startRole, out startFixed, out _);
            }

            UpdateBadge(startBadge, startRole == PortRole.None ? "연결 불가" : "시작",
                startRole == PortRole.None ? SeoUITheme.Current.Danger : RoleColor(startRole));

            if (startRole == PortRole.None)
            {
                SetMessage(startOnBuilding
                    ? InvalidEndpointReason(startOccupant, true)
                    : "연결 불가 · 옆 기계의 유효한 입출력 면이 아닙니다", SeoUITheme.Current.Danger);
                HideBadge(ref endBadge);
                return;
            }

            for (int i = 1; i < path.Count - 1; i++)
            {
                if (!grid.IsOccupied(path[i])) continue;
                ShowBadge(ref endBadge, path[i], "경로 충돌", SeoUITheme.Current.Danger);
                SetMessage("연결 불가 · 벨트 경로에 건물 또는 기존 벨트가 있습니다", SeoUITheme.Current.Danger);
                return;
            }

            // 시작과 대칭 — 끝도 그 칸에 직접 닿았는지(endOnBuilding)와 연결 대상을
            // 찾았는지(endResolved, 직접 닿았거나 바로 옆칸 자동연결 둘 다 포함)를 구분한다.
            // 예전엔 "그 칸 자체가 비어있으면" 무조건 "아직 드래그 중" 취급하고 끝냈는데,
            // 그러면 옆칸 자동연결로 실제로는 연결되는 경우도 계속 "출력에서 경로 생성 중"
            // 경고색만 뜨고 "연결 가능" 성공 상태로 절대 못 넘어갔다(사용자 보고: 실제로는
            // 이어지는데 미리보기가 안 보여줌).
            Vector2Int endCell = path[path.Count - 1];
            bool endOnBuilding = grid.IsOccupied(endCell);
            CellOccupant endOccupant = default;
            if (endOnBuilding) beltTool.TryGetOccupantForConnection(endCell, path[path.Count - 2], out endOccupant);
            PortRole endRole = PortRole.None;
            bool endFixed = false;
            bool endResolved = false;

            if (endOnBuilding)
            {
                endRole = beltTool.ResolveEndpointRole(endOccupant, path[path.Count - 2], false, out endFixed);
                endResolved = endRole != PortRole.None;
            }
            else if (beltTool.TryFindAdjacentOccupant(endCell, path[path.Count - 2], false, out endOccupant, out endRole, out endFixed, out _))
            {
                endResolved = true;
            }

            if (endResolved)
            {
                ShowBadge(ref endBadge, endCell, "도착", RoleColor(endRole));
            }
            else if (endOnBuilding)
            {
                ShowBadge(ref endBadge, endCell, "연결 불가", SeoUITheme.Current.Danger);
                SetMessage(InvalidEndpointReason(endOccupant, false), SeoUITheme.Current.Danger);
                return;
            }
            else
            {
                ShowBadge(ref endBadge, endCell, "도착", SeoUITheme.Current.Warning);
            }

            if (endResolved && startRole == endRole)
            {
                if (!startFixed && endFixed) startRole = BeltDragTool.Opposite(endRole);
                else if (startFixed && !endFixed) endRole = BeltDragTool.Opposite(startRole);
                else
                {
                    SetMessage(startRole == PortRole.Source
                        ? "연결 불가 · 출력 포트끼리는 연결할 수 없습니다"
                        : "연결 불가 · 입력 포트끼리는 연결할 수 없습니다", SeoUITheme.Current.Danger);
                    UpdateBadge(startBadge, "연결 불가", SeoUITheme.Current.Danger);
                    UpdateBadge(endBadge, "연결 불가", SeoUITheme.Current.Danger);
                    return;
                }
            }

            if (startOccupant.Type == CellOccupantType.Belt && startRole == PortRole.Source)
            {
                var segment = driver.World.Segments[startOccupant.InstanceIndex];
                if (segment.NextSegmentId.HasValue || segment.TargetProcessorId.HasValue)
                {
                    SetMessage("연결 불가 · 이미 출력이 연결된 벨트입니다", SeoUITheme.Current.Danger);
                    UpdateBadge(startBadge, "이미 연결됨", SeoUITheme.Current.Danger);
                    return;
                }
            }

            if (!endResolved)
            {
                // 아직 목적지를 못 찾았어도(빈 땅 위, 근처에도 유효 포트 없음) 지금 그린
                // 칸만큼 자원이 있는지는 미리 보여준다 — BeltDragTool의 판정을 그대로
                // 물어봐서 중복 구현 없이 확인.
                if (startRole == PortRole.Source && beltTool != null && !beltTool.HasValidEndpointPreview())
                {
                    SetMessage("자원 부족 · 콘크리트가 모자랍니다", SeoUITheme.Current.Danger);
                    UpdateBadge(startBadge, "자원 부족", SeoUITheme.Current.Danger);
                    return;
                }
                SetMessage(startRole == PortRole.Source
                    ? $"출력에서 경로 생성 중 · 입력 포트까지 드래그 ({path.Count - 1}칸)"
                    : "현재 시작점은 입력 포트입니다 · 반대쪽 출력 포트까지 연결하세요",
                    startRole == PortRole.Source ? SeoUITheme.Current.Warning : SeoUITheme.Current.Danger);
                return;
            }

            bool rolesValid = (startRole == PortRole.Source && endRole == PortRole.Target)
                || (startRole == PortRole.Target && endRole == PortRole.Source);
            if (!rolesValid)
            {
                SetMessage("연결 불가 · 출력과 입력 포트를 확인하세요", SeoUITheme.Current.Danger);
                return;
            }

            // 여기까지는 역할(입출력)만 봤을 뿐, 자원(콘크리트)이 부족해도 여기선 알 수 없다 —
            // BeltDragTool의 판정을 그대로 물어봐서(중복 구현 X) 최종 게이트로 삼는다.
            if (beltTool != null && !beltTool.HasValidEndpointPreview())
            {
                SetMessage("연결 불가 · 자원이 부족합니다", SeoUITheme.Current.Danger);
                UpdateBadge(startBadge, "자원 부족", SeoUITheme.Current.Danger);
                UpdateBadge(endBadge, "자원 부족", SeoUITheme.Current.Danger);
                return;
            }

            CellOccupant target = startRole == PortRole.Target ? startOccupant : endOccupant;
            PortRole targetRole = PortRole.Target;
            lastPathValid = true;
            SetMessage($"연결 가능 · {EndpointName(startOccupant)} → {EndpointName(endOccupant)} · {path.Count - 1}칸"
                + ConnectionCountSuffix(target, targetRole), SeoUITheme.Current.Success);
            UpdateBadge(startBadge, startRole == PortRole.Source ? "시작" : "도착", RoleColor(startRole));
            UpdateBadge(endBadge, endRole == PortRole.Target ? "도착" : "시작", RoleColor(endRole));
        }

        private string InvalidEndpointReason(CellOccupant occupant, bool start)
        {
            if (occupant.Type == CellOccupantType.Miner)
                return "연결 불가 · 채굴기는 벨트 포트가 없고 코어로 자동 전송합니다";
            if (occupant.Type == CellOccupantType.Belt)
                return "연결 불가 · 해당 벨트는 이미 상류가 연결되어 있습니다";
            return start
                ? "연결 불가 · 기계의 입력/출력 포트가 아닌 면입니다"
                : "연결 불가 · 도착 지점이 유효한 입력/출력 포트가 아닙니다";
        }

        private string EndpointName(CellOccupant occupant)
        {
            if (occupant.Type == CellOccupantType.Belt) return "기존 벨트";
            if (occupant.Type == CellOccupantType.Miner) return "채굴기";
            if (occupant.InstanceIndex < 0 || occupant.InstanceIndex >= driver.World.Processors.Count) return "기계";
            var processor = driver.World.Processors[occupant.InstanceIndex];
            if (processor == null) return "기계";
            string key = driver.World.Database.Machines[processor.MachineId].Key;
            return MachineInfoPresenter.GetMachineDisplayName(key);
        }

        private string ConnectionCountSuffix(CellOccupant occupant, PortRole role)
        {
            if (occupant.Type != CellOccupantType.Processor) return string.Empty;
            var processor = driver.World.Processors[occupant.InstanceIndex];
            if (processor == null) return string.Empty;

            int inputs = 0;
            int outputs = 0;
            for (int i = 0; i < driver.World.Segments.Count; i++)
            {
                var segment = driver.World.Segments[i];
                if (segment == null) continue;
                if (segment.TargetProcessorId == occupant.InstanceIndex) inputs++;
                if (segment.SourceProcessorId == occupant.InstanceIndex) outputs++;
            }

            string key = driver.World.Database.Machines[processor.MachineId].Key;
            int maxInputs;
            int maxOutputs;
            if (processor.UniversalPorts) maxInputs = maxOutputs = 4;
            else if (processor.RoutingRole == RoutingRole.Splitter) { maxInputs = 1; maxOutputs = 3; }
            else if (processor.RoutingRole == RoutingRole.Merger) { maxInputs = 3; maxOutputs = 1; }
            else
            {
                maxInputs = Mathf.Max(1, GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, false).Count);
                maxOutputs = key == "Synthesizer" ? 1 : Mathf.Max(1,
                    GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, true).Count);
            }

            return role == PortRole.Target
                ? $" · 입력 {Mathf.Min(inputs + 1, maxInputs)}/{maxInputs}"
                : $" · 출력 {Mathf.Min(outputs + 1, maxOutputs)}/{maxOutputs}";
        }

        private void SetMessage(string message, Color color)
        {
            if (messageText == null) return;
            messageText.text = message;
            messageText.color = color;
            TintPreview(color);
        }

        private void TintPreview(Color color)
        {
            if (beltTool == null || PreviewStripsField == null) return;
            var strips = PreviewStripsField.GetValue(beltTool) as List<GameObject>;
            if (strips == null) return;
            var previewColor = new Color(color.r, color.g, color.b, 0.82f);

            // Update()가 매 프레임 호출하는데, 색이 그대로면 매번 새 Material을 또 만들어 다시
            // 씌울 이유가 없다(그럼 계속 반짝여 보인다 — TintPreserveShape가 매번 새 인스턴스를
            // 만들기 때문). 실제로 색이 바뀌었거나(유효→무효 등) 조각 개수가 바뀌었을 때만
            // (=BeltDragTool이 RebuildPreview로 새로 지어서 아직 이 색이 안 입혀진 새 오브젝트가
            // 있을 때만) 다시 칠한다.
            if (lastTintColor.HasValue && lastTintColor.Value == previewColor && lastTintStripCount == strips.Count)
                return;

            for (int i = 0; i < strips.Count; i++)
            {
                // Colorize는 텍스처/모양을 무시하고 통짜 단색 머티리얼로 갈아버려서, 벨트 미리보기가
                // 매 프레임(Update) 이걸로 덮어써지며 납작한 사각형으로 보이는 버그가 있었다.
                // TintPreserveShape로 바꿔 원본 셰이더/텍스처(모양)는 유지하고 색상만 입힌다.
                if (strips[i] != null) BuildVisuals.TintPreserveShape(strips[i], previewColor);
            }

            lastTintColor = previewColor;
            lastTintStripCount = strips.Count;
        }

        private static Color RoleColor(PortRole role)
        {
            return role == PortRole.Source ? new Color(1f, 0.58f, 0.12f) : new Color(0.2f, 0.72f, 1f);
        }

        private void ShowBadge(ref EndpointBadge badge, Vector2Int cell, string label, Color color)
        {
            if (badge == null) badge = EndpointBadge.Create();
            badge.Root.SetActive(true);
            badge.Set(label, color);
            // 기계의 기존 IN/OUT 배지는 포트 높이에 있으므로, 연결점 배지는 그보다 위에 띄워
            // 두 텍스트가 같은 화면 위치에 투영되지 않게 한다.
            badge.Root.transform.position = GridUtility.CellToWorldCenter(cell, 2.15f);
            if (targetCamera != null) badge.Root.transform.rotation = targetCamera.transform.rotation;
        }

        private static void UpdateBadge(EndpointBadge badge, string label, Color color)
        {
            badge?.Set(label, color);
        }

        private void HideEndpointBadges()
        {
            HideBadge(ref startBadge);
            HideBadge(ref endBadge);
        }

        private static void HideBadge(ref EndpointBadge badge)
        {
            if (badge != null) badge.Root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (startBadge != null) Destroy(startBadge.Root);
            if (endBadge != null) Destroy(endBadge.Root);
        }

        private sealed class EndpointBadge
        {
            public readonly GameObject Root;
            private readonly Image background;
            private readonly TMP_Text label;

            private EndpointBadge(GameObject root, Image background, TMP_Text label)
            {
                Root = root;
                this.background = background;
                this.label = label;
            }

            public static EndpointBadge Create()
            {
                var root = new GameObject("BeltEndpointBadge", typeof(RectTransform), typeof(Canvas));
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 45;
                root.GetComponent<RectTransform>().sizeDelta = new Vector2(92f, 28f);
                root.transform.localScale = Vector3.one * 0.0048f;

                var backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
                backgroundObject.transform.SetParent(root.transform, false);
                var backgroundRect = backgroundObject.GetComponent<RectTransform>();
                backgroundRect.anchorMin = Vector2.zero;
                backgroundRect.anchorMax = Vector2.one;
                backgroundRect.offsetMin = Vector2.zero;
                backgroundRect.offsetMax = Vector2.zero;

                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelObject.transform.SetParent(root.transform, false);
                var labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                var text = labelObject.GetComponent<TextMeshProUGUI>();
                text.font = SeoUITheme.Current.FontAsset;
                text.fontSize = 14;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.color = Color.white;
                text.raycastTarget = false;
                return new EndpointBadge(root, backgroundObject.GetComponent<Image>(), text);
            }

            public void Set(string value, Color color)
            {
                label.text = value;
                background.color = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.96f);
            }
        }
    }
}
