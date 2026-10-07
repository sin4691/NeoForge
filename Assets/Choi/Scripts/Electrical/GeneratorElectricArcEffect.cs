using UnityEngine;

namespace Choi.SaveLoad
{
    public sealed class GeneratorElectricArcEffect : MonoBehaviour
    {
        private readonly LineRenderer[] arcs = new LineRenderer[4];
        private PowerGridSystem grid;
        private int nodeId;
        private float redrawTimer;

        public void Initialize(PowerGridSystem powerGrid, int ownerNodeId)
        {
            grid = powerGrid; nodeId = ownerNodeId;
            Shader shader = Shader.Find("Sprites/Default");
            for (int i = 0; i < arcs.Length; i++)
            {
                var child = new GameObject($"ElectricArc_{i}");
                child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.useWorldSpace = false; line.positionCount = 6;
                line.startWidth = 0.035f; line.endWidth = 0.012f; line.numCapVertices = 2;
                line.material = new Material(shader);
                line.startColor = new Color(0.25f, 0.9f, 1f, 1f);
                line.endColor = new Color(0.75f, 0.35f, 1f, 0.15f);
                line.enabled = false; arcs[i] = line;
            }
        }

        private void Update()
        {
            bool active = grid != null && grid.IsGeneratorActive(nodeId);
            for (int i = 0; i < arcs.Length; i++) arcs[i].enabled = active;
            if (!active || (redrawTimer -= Time.deltaTime) > 0f) return;
            redrawTimer = 0.07f;
            for (int a = 0; a < arcs.Length; a++)
            {
                float angle = a * Mathf.PI * 0.5f + Random.Range(-0.25f, 0.25f);
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
                Vector3 start = radial * Random.Range(0.25f, 0.42f) + Vector3.up * Random.Range(0.25f, 0.85f);
                Vector3 end = radial * Random.Range(0.65f, 0.9f) + Vector3.up * Random.Range(0.25f, 1.05f);
                for (int p = 0; p < 6; p++)
                {
                    float t = p / 5f; Vector3 point = Vector3.Lerp(start, end, t);
                    if (p > 0 && p < 5) point += tangent * Random.Range(-0.13f, 0.13f) + Vector3.up * Random.Range(-0.1f, 0.1f);
                    arcs[a].SetPosition(p, point);
                }
            }
        }
    }
}
