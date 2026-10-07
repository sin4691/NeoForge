using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Choi.SaveLoad
{
    /// <summary>
    /// 씬에 등록된 모든 IPowerSaveParticipant를 하나의 JSON 파일로 저장하고 복원합니다.
    /// UI Button에서는 Save 또는 Load를 직접 연결할 수 있습니다.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class PowerSaveManager : MonoBehaviour
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField] private string fileName = "power_save.json";
        [SerializeField] private bool prettyPrint = true;

        public string SavePath => Path.Combine(Application.persistentDataPath, SanitizeFileName(fileName));
        public string BackupPath => SavePath + ".bak";
        public bool HasLoadedSave { get; private set; }

        public event Action BeforeSave;
        public event Action AfterSave;
        public event Action BeforeLoad;
        public event Action AfterLoad;

        private void Start()
        {
            // 코어, 광맥, 바닥의 Start 초기화가 끝난 뒤 첫 Update 전에 복원한다.
            if (HasSave()) Load();
        }

        public void Save()
        {
            BeforeSave?.Invoke();

            List<IPowerSaveParticipant> participants = FindParticipants();
            var saveFile = new PowerSaveFile
            {
                schemaVersion = CurrentSchemaVersion,
                savedAtUtc = DateTime.UtcNow.ToString("O"),
            };

            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < participants.Count; i++)
            {
                IPowerSaveParticipant participant = participants[i];
                if (!participant.CanSave) continue;

                if (!usedIds.Add(participant.SaveId))
                {
                    Debug.LogError($"[PowerSave] Duplicate save ID skipped: {participant.SaveId}");
                    continue;
                }

                try
                {
                    saveFile.systems.Add(new PowerSaveEntry
                    {
                        saveId = participant.SaveId,
                        saveType = participant.SaveType,
                        restoreOrder = participant.SaveOrder,
                        stateJson = participant.CaptureStateJson(),
                    });
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[PowerSave] Failed to capture '{participant.SaveId}': {exception}");
                }
            }

            string json = JsonUtility.ToJson(saveFile, prettyPrint);
            WriteWithBackup(SavePath, BackupPath, json);
            AfterSave?.Invoke();
            Debug.Log($"[PowerSave] Saved {saveFile.systems.Count} systems to {SavePath}");
        }

        public bool Load()
        {
            BeforeLoad?.Invoke();

            if (!TryReadSave(out PowerSaveFile saveFile, out string loadedPath))
            {
                Debug.LogWarning($"[PowerSave] No valid save file found at {SavePath}");
                return false;
            }

            if (saveFile.schemaVersion > CurrentSchemaVersion)
            {
                Debug.LogError($"[PowerSave] Save version {saveFile.schemaVersion} is newer than supported version {CurrentSchemaVersion}.");
                return false;
            }

            List<IPowerSaveParticipant> participants = FindParticipants();
            var entriesById = new Dictionary<string, PowerSaveEntry>(StringComparer.Ordinal);
            for (int i = 0; i < saveFile.systems.Count; i++)
            {
                PowerSaveEntry entry = saveFile.systems[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.saveId)) continue;
                entriesById[entry.saveId] = entry;
            }

            int restoredCount = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                IPowerSaveParticipant participant = participants[i];
                if (!participant.CanSave || !entriesById.TryGetValue(participant.SaveId, out PowerSaveEntry entry)) continue;

                if (!string.Equals(participant.SaveType, entry.saveType, StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[PowerSave] Type mismatch for '{participant.SaveId}'. Saved: {entry.saveType}, Current: {participant.SaveType}");
                    continue;
                }

                try
                {
                    participant.RestoreStateJson(entry.stateJson);
                    restoredCount++;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[PowerSave] Failed to restore '{participant.SaveId}': {exception}");
                }
            }

            HasLoadedSave |= restoredCount > 0;
            AfterLoad?.Invoke();
            Debug.Log($"[PowerSave] Restored {restoredCount} systems from {loadedPath}");
            return true;
        }

        public bool HasSave()
        {
            return File.Exists(SavePath) || File.Exists(BackupPath);
        }

        private bool TryReadSave(out PowerSaveFile saveFile, out string loadedPath)
        {
            if (TryReadFile(SavePath, out saveFile))
            {
                loadedPath = SavePath;
                return true;
            }

            if (TryReadFile(BackupPath, out saveFile))
            {
                loadedPath = BackupPath;
                Debug.LogWarning("[PowerSave] Main save was unavailable or invalid; loaded backup instead.");
                return true;
            }

            loadedPath = string.Empty;
            saveFile = null;
            return false;
        }

        private static bool TryReadFile(string path, out PowerSaveFile saveFile)
        {
            saveFile = null;
            if (!File.Exists(path)) return false;

            try
            {
                string json = File.ReadAllText(path);
                saveFile = JsonUtility.FromJson<PowerSaveFile>(json);
                return saveFile != null && saveFile.systems != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PowerSave] Could not read '{path}': {exception.Message}");
                return false;
            }
        }

        private static void WriteWithBackup(string path, string backupPath, string contents)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, contents);

            if (!File.Exists(path))
            {
                File.Move(temporaryPath, path);
                return;
            }

            try
            {
                File.Replace(temporaryPath, path, backupPath);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(path, backupPath, true);
                File.Delete(path);
                File.Move(temporaryPath, path);
            }
        }

        private static List<IPowerSaveParticipant> FindParticipants()
        {
            MonoBehaviour[] behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            var participants = new List<IPowerSaveParticipant>();

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || !behaviour.gameObject.scene.IsValid()) continue;
                if (behaviour is IPowerSaveParticipant participant) participants.Add(participant);
            }

            participants.Sort((left, right) =>
            {
                int orderComparison = left.SaveOrder.CompareTo(right.SaveOrder);
                return orderComparison != 0
                    ? orderComparison
                    : string.CompareOrdinal(left.SaveId, right.SaveId);
            });
            return participants;
        }

        private static string SanitizeFileName(string requestedName)
        {
            string safeName = Path.GetFileName(string.IsNullOrWhiteSpace(requestedName) ? "power_save.json" : requestedName);
            return safeName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? safeName : safeName + ".json";
        }
    }
}
