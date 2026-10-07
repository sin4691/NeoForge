using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Optimization
{
    public class FloorChunkManager : MonoBehaviour
    {
        [Header("Grid Settings")]
        public int mapWidth = 400;
        public int mapLength = 400;
        public int chunkSize = 50; // Each chunk is 50x50 tiles
        public float tileSize = 1f;
        public int unlockedSize = 100; // The area that is actually playable and visually lit
        public Vector3 globalOffset = new Vector3(0.5f, 0f, 0.5f);
        public int tilesPerFrame = 150;

        private FloorObjectPool pool;
        private HashSet<Vector2Int> loadedChunks = new HashSet<Vector2Int>();
        private Dictionary<Vector2Int, List<GameObject>> activeChunkObjects = new Dictionary<Vector2Int, List<GameObject>>();
        private Queue<Vector2Int> chunksToLoad = new Queue<Vector2Int>();
        private Coroutine loadCoroutine;

        private Material unlockedMat;
        private Material lockedMat;

        public void Initialize(FloorObjectPool pool)
        {
            this.pool = pool;

            // Setup darkened material for locked areas
            if (pool.prefab != null)
            {
                Renderer r = pool.prefab.GetComponentInChildren<Renderer>();
                if (r != null && r.sharedMaterial != null)
                {
                    unlockedMat = r.sharedMaterial;
                    lockedMat = new Material(unlockedMat);
                    lockedMat.color = new Color(0.2f, 0.2f, 0.2f, 1f); // Darkened
                }
            }
        }

        public void LoadChunk(Vector2Int chunkCoord)
        {
            if (loadedChunks.Contains(chunkCoord)) return;

            int startX = chunkCoord.x * chunkSize;
            int startZ = chunkCoord.y * chunkSize;

            if (startX < 0 || startX >= mapWidth || startZ < 0 || startZ >= mapLength) return;

            loadedChunks.Add(chunkCoord);
            chunksToLoad.Enqueue(chunkCoord);

            if (loadCoroutine == null)
            {
                loadCoroutine = StartCoroutine(ProcessChunkQueue());
            }
        }

        private IEnumerator ProcessChunkQueue()
        {
            float offsetX = mapWidth * tileSize / 2f;
            float offsetZ = mapLength * tileSize / 2f;

            while (chunksToLoad.Count > 0)
            {
                Vector2Int chunkCoord = chunksToLoad.Dequeue();
                
                // Check if it was unloaded before we even started loading
                if (!loadedChunks.Contains(chunkCoord)) continue;

                int startX = chunkCoord.x * chunkSize;
                int startZ = chunkCoord.y * chunkSize;
                int endX = Mathf.Min(startX + chunkSize, mapWidth);
                int endZ = Mathf.Min(startZ + chunkSize, mapLength);

                List<GameObject> chunkPieces = new List<GameObject>();
                int count = 0;

                for (int x = startX; x < endX; x++)
                {
                    for (int z = startZ; z < endZ; z++)
                    {
                        if (!loadedChunks.Contains(chunkCoord)) break; // Cancel midway if unloaded

                        Vector3 pos = new Vector3(x * tileSize - offsetX, 0, z * tileSize - offsetZ) + globalOffset;
                        GameObject piece = pool.GetObject(pos);
                        
                        // Apply darkened material if outside unlocked area
                        if (unlockedMat != null && lockedMat != null)
                        {
                            bool isLocked = Mathf.Abs(x - mapWidth / 2) > unlockedSize / 2 || Mathf.Abs(z - mapLength / 2) > unlockedSize / 2;
                            Renderer r = piece.GetComponentInChildren<Renderer>();
                            if (r != null)
                            {
                                r.sharedMaterial = isLocked ? lockedMat : unlockedMat;
                            }
                        }

                        chunkPieces.Add(piece);

                        count++;
                        // Process tilesPerFrame tiles per frame globally (no matter how many chunks)
                        if (count >= tilesPerFrame) 
                        {
                            count = 0;
                            yield return null; 
                        }
                    }
                    if (!loadedChunks.Contains(chunkCoord)) break;
                }

                if (!loadedChunks.Contains(chunkCoord))
                {
                    foreach (var piece in chunkPieces) pool.ReturnObject(piece);
                }
                else
                {
                    activeChunkObjects[chunkCoord] = chunkPieces;
                }
            }

            loadCoroutine = null;
        }

        public void UnloadChunk(Vector2Int chunkCoord)
        {
            if (!loadedChunks.Contains(chunkCoord)) return;

            if (activeChunkObjects.TryGetValue(chunkCoord, out List<GameObject> chunkPieces))
            {
                foreach (var piece in chunkPieces)
                {
                    pool.ReturnObject(piece);
                }
                activeChunkObjects.Remove(chunkCoord);
            }

            loadedChunks.Remove(chunkCoord);
        }

        public void UnloadAllChunks()
        {
            List<Vector2Int> chunksToUnload = new List<Vector2Int>(loadedChunks);
            foreach (var chunk in chunksToUnload)
            {
                UnloadChunk(chunk);
            }
        }

        public void RefreshAllActiveChunks()
        {
            if (unlockedMat == null || lockedMat == null) return;
            
            foreach (var kvp in activeChunkObjects)
            {
                foreach (GameObject piece in kvp.Value)
                {
                    if (piece.activeInHierarchy)
                    {
                        // Calculate x and z based on position
                        float offsetX = mapWidth * tileSize / 2f;
                        float offsetZ = mapLength * tileSize / 2f;
                        int x = Mathf.RoundToInt((piece.transform.position.x - globalOffset.x + offsetX) / tileSize);
                        int z = Mathf.RoundToInt((piece.transform.position.z - globalOffset.z + offsetZ) / tileSize);
                        
                        bool isLocked = Mathf.Abs(x - mapWidth / 2f) > unlockedSize / 2f || Mathf.Abs(z - mapLength / 2f) > unlockedSize / 2f;
                        Renderer r = piece.GetComponentInChildren<Renderer>();
                        if (r != null)
                        {
                            r.sharedMaterial = isLocked ? lockedMat : unlockedMat;
                        }
                    }
                }
            }
        }
    }
}
