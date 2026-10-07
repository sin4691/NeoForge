using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Factory.Rendering
{
    // 기계/광맥 루트에 붙어서 Addressables 키로 모델 프리팹을 비동기 로드해 자식으로 얹는다.
    // 모델의 방향/크기/높이는 프리팹(배리언트) 자체에 저장돼 있으므로 여기서는 손대지 않고
    // 그대로 인스턴스화만 한다 — 조정은 Assets/Sin/Prefabs/Machines|Deposits 의 배리언트를
    // 에디터에서 눈으로 편집한다.
    //
    // 로드 전/실패 시엔 스폰 코드가 미리 만들어둔 폴백 박스가 그대로 보인다. 오브젝트가
    // 파괴되면 반드시 핸들을 해제한다 — Addressables 인스턴스는 레퍼런스 카운팅이라 누수된다.
    [DisallowMultipleComponent]
    public sealed class AddressableModelMount : MonoBehaviour
    {
        private AsyncOperationHandle<GameObject> handle;
        private bool hasHandle;
        private GameObject placeholder;
        private bool destroyed;
        private bool alignToGround;
        private Action onLoaded;

        // alignToGround: 기계/광맥처럼 "칸 중심 높이에 뜬 스폰 루트" 밑에 모델을 지면(y=0)까지
        // 내려서 맞출 때만 true. 벨트 위 아이템처럼 루트 자체가 이미 원하는 위치(벨트 표면 위)를
        // 매 프레임 그대로 따라가야 하는 경우엔 false로 둬서 모델이 루트 기준 그대로 놓이게 한다.
        // onLoaded: 실제 모델이 다 로드/배치된 직후 호출된다 — 예를 들어 배치 고스트는 폴백
        // 박스를 미리 초록/빨강으로 칠해두더라도, 그 뒤 비동기로 얹히는 진짜 모델은 원래
        // 색(칠 안 된 상태)으로 나타나서 "설치된 것처럼" 보이는 문제가 있었다. 이 콜백으로
        // 로드 완료 시점에 다시 칠할 수 있게 한다.
        public void Mount(string addressableKey, GameObject fallbackPlaceholder, bool alignToGround = true, Action onLoaded = null)
        {
            placeholder = fallbackPlaceholder;
            this.alignToGround = alignToGround;
            this.onLoaded = onLoaded;
            if (string.IsNullOrEmpty(addressableKey)) return; // 키 없음 -> 폴백 박스 유지

            handle = Addressables.InstantiateAsync(addressableKey, transform, instantiateInWorldSpace: false);
            hasHandle = true;
            handle.Completed += OnLoaded;
        }

        private void OnLoaded(AsyncOperationHandle<GameObject> op)
        {
            if (destroyed) return; // 로드 도중 파괴됨 — OnDestroy가 이미 해제 처리
            if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null) return; // 폴백 박스 유지

            if (alignToGround)
            {
                // 배리언트는 "월드 원점에 놨을 때 밑면이 지면(y=0)"으로 저장돼 있다(MachineVariantGenerator).
                // 스폰 루트는 칸 중심 높이(y=0.5 등)에 있으므로, 그 높이만큼 내려 모델 밑면을 지면에 맞춘다.
                Transform model = op.Result.transform;
                model.position -= new Vector3(0f, transform.position.y, 0f);
            }

            if (placeholder != null) placeholder.SetActive(false);
            onLoaded?.Invoke();
        }

        private void OnDestroy()
        {
            destroyed = true;
            if (hasHandle && handle.IsValid())
            {
                Addressables.ReleaseInstance(handle);
                hasHandle = false;
            }
        }
    }
}
