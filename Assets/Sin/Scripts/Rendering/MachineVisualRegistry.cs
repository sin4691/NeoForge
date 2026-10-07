using System.Collections.Generic;
using Factory.Buildings;
using UnityEngine;

namespace Factory.Rendering
{
    // 기계/채굴기 비주얼의 (종류, 인덱스) -> GameObject 역방향 레지스트리. 비주얼을 만드는
    // 곳이 두 군데다(MachineGhostTool.SpawnMachineVisual — 새로 지을 때, FactorySaveBridge의
    // 세이브 복원 경로 — 불러올 때) — 뷰포트 컬링(FactoryViewportCuller)이 "지금 화면 밖인
    // 기계를 꺼야/다시 켜야" 하는데, 꺼둔(SetActive(false)) 오브젝트는 GameObject.Find로 못
    // 찾는다(벨트 풀링 때 이미 겪은 함정 — BeltDragTool.beltVisualRoots와 같은 이유로 여기도
    // 딕셔너리가 필요하다).
    public static class MachineVisualRegistry
    {
        private static readonly Dictionary<(MachineInstanceKind, int), GameObject> visuals = new();

        public static void Register(MachineInstanceKind kind, int index, GameObject go) => visuals[(kind, index)] = go;

        public static void Unregister(MachineInstanceKind kind, int index) => visuals.Remove((kind, index));

        // Destroy된 오브젝트를 가리키는 묵은 항목은 "못 찾음"으로 취급한다(유니티가 그런
        // 참조를 == null로 판정해주는 걸 그대로 이용) — 세이브 로드로 기존 비주얼이 통째로
        // 지워졌는데 여기서 Unregister를 미처 안 부른 경우에도 안전하게 넘어간다.
        public static bool TryGet(MachineInstanceKind kind, int index, out GameObject go)
        {
            if (visuals.TryGetValue((kind, index), out go) && go != null) return true;
            go = null;
            return false;
        }

        // 세이브 로드(ClearCurrentFactory)처럼 기존 비주얼을 통째로 지우고 다시 깔 때, 죽은
        // 오브젝트를 가리키는 묵은 항목이 안 남게 비운다.
        public static void Clear() => visuals.Clear();
    }
}
