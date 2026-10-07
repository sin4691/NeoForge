using Factory.Simulation;
using UnityEditor;
using UnityEngine;

namespace Factory.EditorTools
{
    // OreDepositMarker용 Scene 뷰 붓 도구. "칠하기 모드"를 켜고 Scene 뷰에서 칸을 클릭/드래그하면
    // 그 칸이 노드 범위에 추가되고, Shift를 누른 채면 지워진다. 칠한 칸은 마커 칸을 (0,0)으로 본
    // 오프셋 리스트(OreDepositMarker.PaintedOffsets)에 저장된다.
    [CustomEditor(typeof(OreDepositMarker))]
    public class OreDepositMarkerEditor : Editor
    {
        private static bool paintMode;

        private void OnEnable()
        {
            // paintMode는 static이라 다른 마커를 선택해도 유지된다 — 새로 선택했을 때도 핸들을 다시 숨겨준다.
            if (paintMode) Tools.hidden = true;
        }

        private void OnDisable()
        {
            // 다른 오브젝트를 선택하거나 인스펙터가 닫힐 때 이동 핸들이 숨겨진 채로 남지 않게.
            if (paintMode) Tools.hidden = false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDepositIdPopup();
            // 칠한 칸 목록(paintedOffsets)은 수백 줄이 될 수 있어 Inspector에 안 펼치고 아래에
            // 개수만 보여준다 — 편집은 칠하기 모드로만 한다.
            DrawPropertiesExcluding(serializedObject, "m_Script", "depositId", "paintedOffsets");
            serializedObject.ApplyModifiedProperties();

            var marker = (OreDepositMarker)target;
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "칠하기 모드: Scene 뷰에서 클릭/드래그로 칸 추가, Shift+클릭/드래그로 지우기. " +
                "칠한 칸이 하나라도 있으면 Size는 무시됩니다. 마커가 놓인 칸이 (0,0) 기준입니다.",
                MessageType.None);

            bool newPaintMode = GUILayout.Toggle(paintMode, paintMode ? "칠하기 모드 켜짐 (끄려면 클릭)" : "칠하기 모드 켜기", "Button");
            if (newPaintMode != paintMode)
            {
                paintMode = newPaintMode;
                Tools.hidden = paintMode;
                SceneView.RepaintAll();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Size 범위를 칠한 칸으로 변환"))
                {
                    Undo.RecordObject(marker, "Convert size to painted cells");
                    ConvertSizeToPainted(marker);
                    EditorUtility.SetDirty(marker);
                }

                if (GUILayout.Button("칠한 칸 모두 지우기"))
                {
                    Undo.RecordObject(marker, "Clear painted cells");
                    marker.PaintedOffsets.Clear();
                    EditorUtility.SetDirty(marker);
                }
            }

            EditorGUILayout.LabelField($"현재 칠한 칸: {marker.PaintedOffsets.Count}");
        }

        // depositId를 글자로 직접 치지 않고 드롭다운에서 고르게 한다. 목록은 OreDepositDef
        // 애셋을 프로젝트에서 직접 찾아서 만들기 때문에(Assets/Resources/GameData/OreDeposits에
        // 애셋을 새로 추가하면) 여기 코드를 안 고쳐도 자동으로 목록에 나온다.
        private void DrawDepositIdPopup()
        {
            SerializedProperty prop = serializedObject.FindProperty("depositId");

            var ids = new System.Collections.Generic.List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:OreDepositDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<Factory.Data.OreDepositDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && !string.IsNullOrEmpty(def.depositId)) ids.Add(def.depositId);
            }
            ids.Sort(System.StringComparer.Ordinal);

            // 0번은 "선택 안 됨" 자리. 현재 값이 목록에 없으면(애셋이 지워졌거나 오타) 그 값도 그대로
            // 보여줘서 조용히 사라지지 않게 한다.
            string current = prop.stringValue;
            var options = new System.Collections.Generic.List<string> { "(선택 안 됨)" };
            options.AddRange(ids);
            int selected = 0;
            if (!string.IsNullOrEmpty(current))
            {
                selected = ids.IndexOf(current) + 1;
                if (selected == 0)
                {
                    options.Add($"{current} (애셋 없음)");
                    selected = options.Count - 1;
                }
            }

            int picked = EditorGUILayout.Popup("Deposit Id", selected, options.ToArray());
            if (picked != selected)
            {
                prop.stringValue = picked == 0 ? string.Empty : options[picked];
            }
        }

        private void OnSceneGUI()
        {
            if (!paintMode) return;

            var marker = (OreDepositMarker)target;
            Event e = Event.current;

            // 칠하는 동안 Scene 뷰 클릭이 다른 오브젝트 선택으로 새지 않게 컨트롤을 가로챈다.
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);

            if (TryGetHoverCell(e, out Vector2Int hoverCell))
            {
                bool erase = e.shift;
                Handles.color = erase ? new Color(1f, 0.2f, 0.2f, 0.9f) : new Color(0.2f, 1f, 0.3f, 0.9f);
                Handles.DrawWireCube(GridUtility.CellToWorldCenter(hoverCell, 0.05f),
                    new Vector3(1f, 0.1f, 1f) * GridUtility.CellSize);

                bool paintEvent = (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
                    && e.button == 0 && !e.alt;
                if (paintEvent)
                {
                    ApplyBrush(marker, hoverCell, erase);
                    e.Use();
                }
            }

            if (e.type == EventType.MouseMove) SceneView.RepaintAll();
        }

        private static bool TryGetHoverCell(Event e, out Vector2Int cell)
        {
            cell = default;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float distance)) return false;
            cell = GridUtility.WorldToCell(ray.GetPoint(distance));
            return true;
        }

        private static void ApplyBrush(OreDepositMarker marker, Vector2Int cell, bool erase)
        {
            // 칠하기를 처음 시작하는 순간 size 사각형이 통째로 사라지지 않게(칠한 칸이 하나라도
            // 생기면 size는 무시되므로), 아직 칠한 칸이 없으면 지금 보이는 범위를 먼저 옮겨 담는다.
            if (marker.PaintedOffsets.Count == 0 && !erase)
            {
                Undo.RecordObject(marker, "Paint ore deposit");
                ConvertSizeToPainted(marker);
            }

            Vector2Int offset = cell - marker.Cell;
            int index = marker.PaintedOffsets.IndexOf(offset);

            if (erase)
            {
                if (index < 0) return;
                Undo.RecordObject(marker, "Erase ore deposit cell");
                marker.PaintedOffsets.RemoveAt(index);
            }
            else
            {
                if (index >= 0) return;
                Undo.RecordObject(marker, "Paint ore deposit");
                marker.PaintedOffsets.Add(offset);
            }

            EditorUtility.SetDirty(marker);
        }

        private static void ConvertSizeToPainted(OreDepositMarker marker)
        {
            marker.PaintedOffsets.Clear();
            int width = Mathf.Max(1, marker.size.x);
            int height = Mathf.Max(1, marker.size.y);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    marker.PaintedOffsets.Add(new Vector2Int(x, y));
                }
            }
        }
    }
}
