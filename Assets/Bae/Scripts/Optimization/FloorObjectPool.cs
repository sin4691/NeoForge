using System.Collections.Generic;
using UnityEngine;

namespace Optimization
{
    public class FloorObjectPool : MonoBehaviour
    {
        public GameObject prefab;
        private Queue<GameObject> pool = new Queue<GameObject>();

        public void Initialize(GameObject prefabToPool, int initialSize = 1000)
        {
            prefab = prefabToPool;
            for (int i = 0; i < initialSize; i++)
            {
                CreateNewObject();
            }
        }

        private void CreateNewObject()
        {
            if (prefab == null) return;
            GameObject obj = Instantiate(prefab, transform);
            obj.SetActive(false);
            pool.Enqueue(obj);
        }

        public GameObject GetObject(Vector3 position)
        {
            if (pool.Count == 0)
            {
                CreateNewObject();
            }
            
            GameObject obj = pool.Dequeue();
            obj.transform.position = position;
            obj.SetActive(true);
            return obj;
        }

        public void ReturnObject(GameObject obj)
        {
            obj.SetActive(false);
            pool.Enqueue(obj);
        }

        public void ClearPool()
        {
            while (pool.Count > 0)
            {
                GameObject obj = pool.Dequeue();
                if (obj != null) Destroy(obj);
            }
        }
    }
}
