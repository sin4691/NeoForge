# NeoForge

컨베이어 벨트로 자원을 나르고 기계로 가공하는 **모바일 공장 시뮬레이션**입니다. 기업 협력(링크즈) 프로젝트로 4인이 개발했습니다.

| 장르 | 기간 | 인원 | 엔진 |
|---|---|---|---|
| 모바일 공장 시뮬레이션 | 2026.08.14 – 09.29 | 4인 | Unity · C# |

📂 자세한 설명: [포트폴리오 – NeoForge](https://sin4691.github.io/#factory)

## 내가 맡은 것

- **벨트 물류 시뮬레이션**: 역압, 하류 우선 처리, 분류기·합류기. 게임 규칙을 MonoBehaviour와 분리한 순수 C#으로 작성
- **고정 틱 시뮬레이션**: 프레임과 무관하게 같은 결과가 나오도록 시간을 누적해 일정 간격으로 1틱씩 계산
- **모바일 건설 UX**: 벨트 드래그, 기계 고스트 배치, 철거, 1터치 건설 / 2터치 카메라 분리
- **Addressables**: 기계 모델 비동기 로드·해제
- **최적화**: Profiler로 대형 공장(벨트 1,455칸·설비 904개)의 병목을 찾아 에디터 기준 11~15 → 30 FPS. 벨트 경로 캐시, 아이템 슬롯 풀링, 뷰포트 컬링
- **자동화 테스트 62개**: 버그를 고칠 때마다 같은 상황을 재현하는 테스트를 함께 작성

## 코드 위치

| 내용 | 경로 |
|---|---|
| 시뮬레이션 코어 | [`Assets/Sin/Scripts/Simulation`](Assets/Sin/Scripts/Simulation) (`SimulationDriver`, `SimulationWorld`, `BeltSystem`) |
| 건설 입력 | [`Assets/Sin/Scripts/Building`](Assets/Sin/Scripts/Building) |
| 뷰포트 컬링 | [`Assets/Sin/Scripts/Rendering`](Assets/Sin/Scripts/Rendering) |
| 테스트 | [`Assets/Sin/Tests/PlayMode`](Assets/Sin/Tests/PlayMode) |

## 팀원 담당

데이터 파이프라인(JSON) · 전력망 · 세이브/로드 · 생산 통계 · HUD · 연구 잠금은 팀원 작업입니다.
