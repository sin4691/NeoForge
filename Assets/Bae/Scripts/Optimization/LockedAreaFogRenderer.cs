using UnityEngine;
using UnityEngine.Rendering;

namespace Optimization
{
    /// <summary>
    /// Draws overlapping cloud particles over the locked map. Only the area near
    /// the camera is populated, so the effect stays inexpensive on mobile.
    /// </summary>
    public sealed class LockedAreaFogRenderer : MonoBehaviour
    {
        [Header("Cloud Particles")]
        [SerializeField] private Color fogColor = new Color(1f, 1f, 1f, 0.68f);
        [SerializeField, Min(2f)] private float particleSpacing = 4.2f;
        [SerializeField] private Vector2 particleSizeRange = new Vector2(9f, 15f);
        [SerializeField, Min(0f)] private float heightAboveFloor = 0.7f;
        [SerializeField, Min(0f)] private float driftAmount = 1.5f;
        [SerializeField, Min(0f)] private float driftSpeed = 0.22f;
        [SerializeField, Min(0f)] private float unlockTransitionSpeed = 35f;

        private const int MaxParticles = 1200;
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[MaxParticles];

        private FloorSystemManager floor;
        private ParticleSystem cloudSystem;
        private Material cloudMaterial;
        private Texture2D cloudTexture;
        private float displayedUnlockedSize;
        private float fogWorldY;

        public void Initialize(FloorSystemManager owner)
        {
            floor = owner;
            displayedUnlockedSize = owner.unlockedSize;
            fogWorldY = ResolveFogWorldY(owner);
            CreateParticleSystem();
            RebuildParticles();
        }

        private void LateUpdate()
        {
            if (floor == null || cloudSystem == null) return;
            float target = Mathf.Clamp(floor.unlockedSize, 0f, Mathf.Min(floor.mapWidth, floor.mapLength));
            displayedUnlockedSize = unlockTransitionSpeed <= 0f
                ? target
                : Mathf.MoveTowards(displayedUnlockedSize, target, unlockTransitionSpeed * Time.deltaTime);
            RebuildParticles();
        }

        private void OnDestroy()
        {
            if (cloudMaterial != null) Destroy(cloudMaterial);
            if (cloudTexture != null) Destroy(cloudTexture);
        }

        private void CreateParticleSystem()
        {
            Shader shader = Resources.Load<Shader>("Shaders/LockedAreaFog");
            if (shader == null)
            {
                Debug.LogWarning("[LockedAreaFogRenderer] 구름 파티클 셰이더를 찾지 못했습니다.", this);
                enabled = false;
                return;
            }

            GameObject clouds = new GameObject("Locked Map Cloud Particles");
            clouds.transform.SetParent(transform, false);
            cloudSystem = clouds.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = cloudSystem.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticles;
            main.startLifetime = 2f;
            ParticleSystem.EmissionModule emission = cloudSystem.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = cloudSystem.shape;
            shape.enabled = false;

            cloudTexture = CreateCloudTexture(96);
            cloudMaterial = new Material(shader) { name = "Locked Map Clouds (Runtime)" };
            cloudMaterial.SetTexture(BaseMapId, cloudTexture);

            ParticleSystemRenderer renderer = clouds.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = cloudMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void RebuildParticles()
        {
            Camera camera = floor.targetCamera != null ? floor.targetCamera : Camera.main;
            if (camera == null) return;

            float radius = floor.viewRadius + particleSizeRange.y;
            Vector3 cameraPosition = camera.transform.position;
            float mapHalfWidth = floor.mapWidth * floor.tileSize * 0.5f;
            float mapHalfLength = floor.mapLength * floor.tileSize * 0.5f;
            float openHalf = (displayedUnlockedSize * floor.tileSize + floor.tileSize) * 0.5f;
            float centerX = floor.globalOffset.x;
            float centerZ = floor.globalOffset.z;
            float time = Time.time * driftSpeed;
            int count = 0;

            int minGridX = Mathf.FloorToInt((cameraPosition.x - radius) / particleSpacing);
            int maxGridX = Mathf.CeilToInt((cameraPosition.x + radius) / particleSpacing);
            int minGridZ = Mathf.FloorToInt((cameraPosition.z - radius) / particleSpacing);
            int maxGridZ = Mathf.CeilToInt((cameraPosition.z + radius) / particleSpacing);

            for (int gridX = minGridX; gridX <= maxGridX && count < MaxParticles; gridX++)
            {
                for (int gridZ = minGridZ; gridZ <= maxGridZ && count < MaxParticles; gridZ++)
                {
                    uint seed = Hash(gridX, gridZ);
                    float jitterX = (ToUnit(seed) - 0.5f) * particleSpacing * 0.9f;
                    float jitterZ = (ToUnit(Hash(seed, 17u)) - 0.5f) * particleSpacing * 0.9f;
                    float phase = ToUnit(Hash(seed, 31u)) * Mathf.PI * 2f;
                    float x = gridX * particleSpacing + jitterX + Mathf.Sin(time + phase) * driftAmount;
                    float z = gridZ * particleSpacing + jitterZ + Mathf.Cos(time * 0.73f + phase) * driftAmount;

                    if (Mathf.Abs(x - centerX) > mapHalfWidth || Mathf.Abs(z - centerZ) > mapHalfLength) continue;
                    if (Mathf.Abs(x - centerX) <= openHalf && Mathf.Abs(z - centerZ) <= openHalf) continue;

                    float size = Mathf.Lerp(particleSizeRange.x, particleSizeRange.y, ToUnit(Hash(seed, 53u)));
                    Color color = fogColor;
                    color.a *= Mathf.Lerp(0.78f, 1f, ToUnit(Hash(seed, 79u)));
                    particles[count++] = new ParticleSystem.Particle
                    {
                        position = new Vector3(x, fogWorldY + ToUnit(Hash(seed, 97u)) * 1.2f, z),
                        startSize = size,
                        startColor = color,
                        remainingLifetime = 2f,
                        startLifetime = 2f,
                        randomSeed = seed
                    };
                }
            }

            cloudSystem.SetParticles(particles, count);
        }

        private static Texture2D CreateCloudTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Procedural Soft Cloud",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            float inv = 1f / (size - 1f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = x * inv * 2f - 1f;
                    float ny = y * inv * 2f - 1f;
                    float radial = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                    float noise = Mathf.PerlinNoise(x * 0.075f + 8.1f, y * 0.075f + 3.7f);
                    float alpha = Mathf.SmoothStep(0f, 1f, radial) * Mathf.Lerp(0.72f, 1f, noise);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private float ResolveFogWorldY(FloorSystemManager owner)
        {
            Renderer floorRenderer = owner.floorPrefab != null
                ? owner.floorPrefab.GetComponentInChildren<Renderer>(true)
                : null;
            float floorTop = floorRenderer != null ? floorRenderer.bounds.max.y : owner.globalOffset.y;
            return Mathf.Max(owner.globalOffset.y, floorTop) + heightAboveFloor;
        }

        private static uint Hash(int x, int y) => Hash(unchecked((uint)x), unchecked((uint)y));

        private static uint Hash(uint value, uint salt)
        {
            uint hash = value ^ (salt * 0x9E3779B9u);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            return hash ^ (hash >> 16);
        }

        private static float ToUnit(uint value) => (value & 0x00FFFFFFu) / 16777215f;
    }
}
