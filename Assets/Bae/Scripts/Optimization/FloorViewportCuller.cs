using System.Collections.Generic;
using UnityEngine;

namespace Optimization
{
    public class FloorViewportCuller : MonoBehaviour
    {
        public Camera viewCamera;
        public FloorChunkManager chunkManager;
        public float viewRadius = 45f; // Increased so generating happens OFF-SCREEN
        public float updateInterval = 0.05f; // Update much faster to catch up with camera

        private float timer = 0f;
        private HashSet<Vector2Int> currentlyVisibleChunks = new HashSet<Vector2Int>();
        private bool isCullingEnabled = false;

        public void SetCullingEnabled(bool enabled)
        {
            isCullingEnabled = enabled;
            if (!enabled)
            {
                chunkManager.UnloadAllChunks();
                currentlyVisibleChunks.Clear();
            }
            else
            {
                ForceUpdate();
            }
        }

        private void Update()
        {
            if (!isCullingEnabled) return;

            timer += Time.deltaTime;
            if (timer >= updateInterval)
            {
                timer = 0f;
                UpdateVisibleChunks();
            }
        }

        public void ForceUpdate()
        {
            if (!isCullingEnabled) return;
            UpdateVisibleChunks();
        }

        private void UpdateVisibleChunks()
        {
            if (viewCamera == null || chunkManager == null) return;

            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            
            // Get screen corners
            Ray blRay = viewCamera.ViewportPointToRay(new Vector3(0, 0, 0));
            Ray trRay = viewCamera.ViewportPointToRay(new Vector3(1, 1, 0));
            Ray brRay = viewCamera.ViewportPointToRay(new Vector3(1, 0, 0));
            Ray tlRay = viewCamera.ViewportPointToRay(new Vector3(0, 1, 0));

            Vector3[] hitPoints = new Vector3[4];
            bool hitAny = false;

            if (groundPlane.Raycast(blRay, out float d1)) { hitPoints[0] = blRay.GetPoint(d1); hitAny = true; }
            if (groundPlane.Raycast(trRay, out float d2)) { hitPoints[1] = trRay.GetPoint(d2); hitAny = true; }
            if (groundPlane.Raycast(brRay, out float d3)) { hitPoints[2] = brRay.GetPoint(d3); hitAny = true; }
            if (groundPlane.Raycast(tlRay, out float d4)) { hitPoints[3] = tlRay.GetPoint(d4); hitAny = true; }

            // If looking at the horizon or invalid, fallback to center point
            if (!hitAny)
            {
                Ray centerRay = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                if (groundPlane.Raycast(centerRay, out float centerD))
                {
                    hitPoints[0] = centerRay.GetPoint(centerD);
                    hitPoints[1] = hitPoints[0];
                    hitPoints[2] = hitPoints[0];
                    hitPoints[3] = hitPoints[0];
                }
                else return;
            }

            float minX = float.MaxValue; float maxX = float.MinValue;
            float minZ = float.MaxValue; float maxZ = float.MinValue;

            for (int i = 0; i < 4; i++)
            {
                // If a corner didn't hit (e.g. looking at sky), skip it, but we handle it by clamping
                if (hitPoints[i] != Vector3.zero || hitAny) 
                {
                    if (hitPoints[i].x < minX) minX = hitPoints[i].x;
                    if (hitPoints[i].x > maxX) maxX = hitPoints[i].x;
                    if (hitPoints[i].z < minZ) minZ = hitPoints[i].z;
                    if (hitPoints[i].z > maxZ) maxZ = hitPoints[i].z;
                }
            }

            // Map offsets
            float offsetX = chunkManager.mapWidth * chunkManager.tileSize / 2f;
            float offsetZ = chunkManager.mapLength * chunkManager.tileSize / 2f;

            // Apply offsets to bounds
            minX += offsetX; maxX += offsetX;
            minZ += offsetZ; maxZ += offsetZ;

            // Extra padding to prevent seeing the edge when panning fast (add viewRadius as padding)
            minX -= viewRadius; maxX += viewRadius;
            minZ -= viewRadius; maxZ += viewRadius;

            // Convert to chunk coordinates
            float chunkWorldSize = chunkManager.chunkSize * chunkManager.tileSize;
            
            int startChunkX = Mathf.FloorToInt(minX / chunkWorldSize);
            int endChunkX = Mathf.FloorToInt(maxX / chunkWorldSize);
            int startChunkZ = Mathf.FloorToInt(minZ / chunkWorldSize);
            int endChunkZ = Mathf.FloorToInt(maxZ / chunkWorldSize);

            HashSet<Vector2Int> newVisibleChunks = new HashSet<Vector2Int>();

            for (int x = startChunkX; x <= endChunkX; x++)
            {
                for (int z = startChunkZ; z <= endChunkZ; z++)
                {
                    newVisibleChunks.Add(new Vector2Int(x, z));
                }
            }

            // 5. Unload chunks that are no longer visible
            List<Vector2Int> chunksToUnload = new List<Vector2Int>();
            foreach (var chunk in currentlyVisibleChunks)
            {
                if (!newVisibleChunks.Contains(chunk))
                {
                    chunkManager.UnloadChunk(chunk);
                    chunksToUnload.Add(chunk);
                }
            }
            foreach (var chunk in chunksToUnload)
            {
                currentlyVisibleChunks.Remove(chunk);
            }

            // 6. Load new visible chunks
            foreach (var chunk in newVisibleChunks)
            {
                if (!currentlyVisibleChunks.Contains(chunk))
                {
                    chunkManager.LoadChunk(chunk);
                    currentlyVisibleChunks.Add(chunk);
                }
            }
        }
    }
}
