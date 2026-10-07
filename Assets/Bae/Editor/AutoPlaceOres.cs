using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Factory.Simulation;
using System.Collections.Generic;

namespace Factory.EditorScripts
{
    public class AutoPlaceOres : Editor
    {
        [MenuItem("Tools/기획 밸런스 광맥 자동 배치 (Auto Place)")]
        public static void PlaceOres()
        {
            GameObject parent = GameObject.Find("OreDeposits_AutoPlaced");
            if (parent != null)
            {
                DestroyImmediate(parent);
            }
            parent = new GameObject("OreDeposits_AutoPlaced");

            // 0티어 (반경 30~45) - 100x100 해금구역 (maxBound: 50)
            PlaceRing(parent, "CopperOreDeposit", 38, 3, new Vector2Int(2, 3), 50);
            PlaceRing(parent, "CoalDeposit", 42, 3, new Vector2Int(3, 3), 50);
            PlaceRing(parent, "ConcreteDeposit", 42, 1, new Vector2Int(4, 5), 50); 

            // 1티어 (반경 75~95) - 200x200 해금구역 (maxBound: 100)
            PlaceRing(parent, "IronOreDeposit", 80, 5, new Vector2Int(3, 3), 100); 
            PlaceRing(parent, "QuartzOreDeposit", 90, 3, new Vector2Int(2, 3), 100); 
            PlaceRing(parent, "CopperOreDeposit", 85, 2, new Vector2Int(2, 2), 100); 
            PlaceRing(parent, "CoalDeposit", 90, 2, new Vector2Int(2, 2), 100); 

            // 2티어 (반경 125~145) - 300x300 해금구역 (maxBound: 150)
            PlaceRing(parent, "IronOreDeposit", 130, 4, new Vector2Int(3, 4), 150);
            PlaceRing(parent, "QuartzOreDeposit", 140, 5, new Vector2Int(3, 3), 150); 
            PlaceRing(parent, "GoldOreDeposit", 135, 3, new Vector2Int(2, 2), 150); 

            // 3티어 (반경 175~195) - 400x400 최외곽 구역 (maxBound: 200)
            PlaceRing(parent, "GoldOreDeposit", 180, 4, new Vector2Int(3, 3), 200);
            PlaceRing(parent, "UraniumOreDeposit", 190, 4, new Vector2Int(3, 2), 200); 
            PlaceRing(parent, "QuartzOreDeposit", 185, 4, new Vector2Int(4, 4), 200);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("<color=cyan>[자동 광맥 배치 완료]</color> 사방팔방(360도) 빈틈없이 광맥이 풍부하게 채워졌습니다!");
        }

        // 반경(radius)을 기준으로 지정된 개수(count)만큼 원형으로 배치하는 함수
        private static void PlaceRing(GameObject parent, string depositId, float radius, int count, Vector2Int size, int maxBound)
        {
            float angleStep = 360f / count;
            float startAngle = Random.Range(0f, 360f);

            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + (i * angleStep);
                float rad = angle * Mathf.Deg2Rad;
                
                float randomRadius = radius + Random.Range(-5f, 5f);
                
                int x = Mathf.RoundToInt(Mathf.Cos(rad) * randomRadius);
                int y = Mathf.RoundToInt(Mathf.Sin(rad) * randomRadius);

                // 타일 덩어리가 다음 티어 금지구역(검은색)을 침범하지 않도록 안전하게 가두기
                x = Mathf.Clamp(x, -(maxBound - 1), (maxBound - 1) - size.x);
                y = Mathf.Clamp(y, -(maxBound - 1), (maxBound - 1) - size.y);

                CreateMarker(parent, depositId, new Vector2Int(x, y), size);
            }
        }

        private static void CreateMarker(GameObject parent, string depositId, Vector2Int cellPosition, Vector2Int size)
        {
            GameObject go = new GameObject($"Marker_{depositId}_{cellPosition.x}_{cellPosition.y}");
            go.transform.SetParent(parent.transform);
            
            go.transform.position = GridUtility.CellToWorldCenter(cellPosition, 0f); 

            OreDepositMarker marker = go.AddComponent<OreDepositMarker>();
            marker.depositId = depositId;
            marker.size = size;
            
            typeof(OreDepositMarker).GetMethod("SnapToCell", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(marker, null);
        }
    }
}
