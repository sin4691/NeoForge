using UnityEngine;

namespace Factory.Buildings
{
    // 모든 기계가 공유하는 연기 파티클 시스템. 기계마다 ParticleSystem을 따로 만들면
    // 대수만큼 GameObject/컴포넌트/드로우콜이 늘어난다(100대 = 최대 100드로우콜) — 대신
    // 이 시스템 하나만 계속 살아있게 두고, 각 기계는 EmitParams로 위치/속도만 던져서
    // 쏘게 한다. 렌더러/재질이 하나뿐이라 몇 백 대가 동시에 연기를 뿜어도 드로우콜은 1개.
    public static class MachineSmokeEmitter
    {
        private static ParticleSystem shared;
        private static Material materialCache;
        private static Texture2D textureCache;

        // worldPosition/velocity는 호출한 쪽(MachineActivityIndicator)이 자기 기계 위치와
        // 튜닝한 방향/속도로 미리 계산해서 넘긴다 — 이 클래스는 "어디서 어느 쪽으로"는
        // 전혀 모르고 그냥 그대로 쏘기만 한다.
        public static void Puff(Vector3 worldPosition, Vector3 velocity, int count)
        {
            EnsureShared();
            for (int i = 0; i < count; i++)
            {
                // 완전히 겹쳐 보이지 않게 위치/속도/크기에 아주 약간씩 무작위 편차를 준다.
                var emitParams = new ParticleSystem.EmitParams
                {
                    position = worldPosition + Random.insideUnitSphere * 0.08f,
                    velocity = velocity + Random.insideUnitSphere * 0.05f,
                    startSize = Random.Range(0.3f, 0.55f),
                    startColor = new Color(0.55f, 0.55f, 0.55f, 0.9f),
                    startLifetime = 1.8f,
                };
                shared.Emit(emitParams, 1);
            }
        }

        private static void EnsureShared()
        {
            if (shared != null) return;

            var go = new GameObject("[Shared] MachineSmoke");
            Object.DontDestroyOnLoad(go);

            shared = go.AddComponent<ParticleSystem>();
            var main = shared.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.8f;
            main.startSpeed = 0f; // 속도는 매 Emit 호출의 EmitParams.velocity가 담당.
            main.startSize = 0.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 2000; // 넉넉하게 — 기계 수가 아무리 많아도 시스템은 이거 하나뿐.

            // 전부 수동 Emit()으로만 쏜다 — 이 시스템 자체의 rateOverTime/shape는 안 쓴다.
            var emission = shared.emission;
            emission.rateOverTime = 0f;

            var shape = shared.shape;
            shape.enabled = false; // EmitParams.position/velocity를 그대로 쓰기 위해 shape의 영향을 끈다.

            // 위로 갈수록 옅어지다 사라지게.
            var colorOverLifetime = shared.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            // 위로 갈수록 커지게(연기가 퍼지는 모양).
            var sizeOverLifetime = shared.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.enableGPUInstancing = true; // SRP 배처/GPU 인스턴싱 여지를 열어둔다.
            renderer.material = GetMaterial();

            shared.Play();
        }

        private static Material GetMaterial()
        {
            if (materialCache != null) return materialCache;

            // Sprites/Default는 URP에서도 별도 서페이스 설정 없이 바로 알파블렌드+버텍스컬러
            // 곱연산이 되는 가벼운 셰이더라, 파티클용 텍스처 하나 물리는 용도로 딱 맞는다.
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = new Material(shader);
            var tex = GetTexture();
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            else if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);

            materialCache = mat;
            return mat;
        }

        // 32x32 흰색 원 + 가장자리로 갈수록 부드럽게 흐려지는 알파. 에셋 없이 매끈한 연기
        // 입자 하나를 코드로 그려서 쓴다.
        private static Texture2D GetTexture()
        {
            if (textureCache != null) return textureCache;

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Vector2 center = new Vector2(size - 1, size - 1) * 0.5f;
            float maxDist = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    // 중심 60%는 꽉 찬 알파로 두고, 바깥 40%에서만 가장자리로 흐려지게.
                    float alpha = Mathf.Clamp01(1f - Mathf.InverseLerp(maxDist * 0.6f, maxDist, dist));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();

            textureCache = tex;
            return tex;
        }
    }
}
