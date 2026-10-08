# NeoForge

컨베이어 벨트로 자원을 나르고 기계로 가공하는 **모바일 공장 시뮬레이션**입니다. 기업 협력(링크즈) 프로젝트로 4인이 개발했습니다.

| 장르 | 기간 | 인원 | 엔진 |
|---|---|---|---|
| 모바일 공장 시뮬레이션 | 2026.08.14 – 09.29 | 4인 | Unity · C# |

📂 자세한 설명: [포트폴리오 – NeoForge](https://sin4691.github.io/#factory)

## 👀 신재윤 작업만 보기

팀원마다 작업 폴더를 나눠 썼습니다. **[`Assets/Sin/`](Assets/Sin) 폴더가 제 작업**입니다. 팀원 폴더는 `Seo`, `Bae`, `Choi`입니다.

- 예외: `Assets/Sin` 안의 `FactoryStatistics.cs`와 `FactoryStatisticsTests.cs`는 팀원이 작성했습니다.
- 최적화와 기능 연동 때문에 팀원 폴더 파일도 일부 고쳤습니다. 그 부분은 [제 커밋 목록](https://github.com/sin4691/NeoForge/commits?author=sin4691)에서 보실 수 있습니다.

먼저 보시면 좋은 파일입니다.

| 기능 | 파일 |
|---|---|
| 고정 틱 시뮬레이션 (프레임과 계산 분리) | [`SimulationDriver.cs`](Assets/Sin/Scripts/Simulation/SimulationDriver.cs) · [`SimulationWorld.cs`](Assets/Sin/Scripts/Simulation/SimulationWorld.cs) |
| 벨트 이동·역압 (하류부터 처리) | [`BeltSystem.cs`](Assets/Sin/Scripts/Simulation/BeltSystem.cs) |
| 분류기·합류기 | [`RoutingSystem.cs`](Assets/Sin/Scripts/Simulation/RoutingSystem.cs) · [`BeltRouting.cs`](Assets/Sin/Scripts/Simulation/BeltRouting.cs) |
| 벨트 경로 캐시 (연결이 바뀔 때만 다시 계산) | [`BeltTopologyVersion.cs`](Assets/Sin/Scripts/Simulation/BeltTopologyVersion.cs) |
| 1터치 건설 / 2터치 카메라 분리 | [`BuildInputRouter.cs`](Assets/Sin/Scripts/Building/BuildInputRouter.cs) |
| 벨트 드래그 건설 | [`BeltDragTool.cs`](Assets/Sin/Scripts/Building/BeltDragTool.cs) (partial 5개) · [`BeltPathBuilder.cs`](Assets/Sin/Scripts/Building/BeltPathBuilder.cs) |
| 기계 고스트 배치·철거 | [`MachineGhostTool.cs`](Assets/Sin/Scripts/Building/MachineGhostTool.cs) · [`DemolishTool.cs`](Assets/Sin/Scripts/Building/DemolishTool.cs) |
| 아이템 슬롯 풀링 | [`BeltItemRenderer.cs`](Assets/Sin/Scripts/Rendering/BeltItemRenderer.cs) |
| 뷰포트 컬링 | [`FactoryViewportCuller.cs`](Assets/Sin/Scripts/Rendering/FactoryViewportCuller.cs) |
| Addressables 모델 로드·해제 | [`AddressableModelMount.cs`](Assets/Sin/Scripts/Rendering/AddressableModelMount.cs) |
| 자동화 테스트 | [`Assets/Sin/Tests/PlayMode`](Assets/Sin/Tests/PlayMode) |
| 대형 테스트 공장 생성 (성능 측정용) | [`TestFactoryBuilder.cs`](Assets/Sin/Editor/TestFactoryBuilder.cs) |

## 내가 맡은 것

- **벨트 물류 시뮬레이션**: 역압, 하류 우선 처리, 분류기·합류기를 만들었습니다. 게임 규칙은 MonoBehaviour와 분리해 순수 C#으로 작성했습니다.
- **고정 틱 시뮬레이션**: 시간을 누적해 일정 간격으로 1틱씩 계산합니다. 그래서 프레임과 무관하게 같은 결과가 나옵니다.
- **모바일 건설 UX**: 벨트 드래그, 기계 고스트 배치, 철거를 만들었습니다. 1터치는 건설, 2터치는 카메라로 나눴습니다.
- **Addressables**: 기계 모델을 비동기로 로드하고 해제합니다.
- **최적화**: Profiler로 대형 공장(벨트 1,455칸·설비 904개)의 병목을 찾았습니다. 벨트 경로 캐시, 아이템 슬롯 풀링, 뷰포트 컬링을 넣어 에디터 기준 11~15 FPS를 30 FPS로 올렸습니다.
- **자동화 테스트 62개**: 버그를 고칠 때마다 같은 상황을 재현하는 테스트를 함께 작성했습니다.

## 팀원 담당

데이터 파이프라인(JSON), 전력망, 세이브/로드, 생산 통계, HUD, 연구 잠금은 팀원이 작업했습니다.
