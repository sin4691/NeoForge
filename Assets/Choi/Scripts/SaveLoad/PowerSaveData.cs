using System;
using System.Collections.Generic;

namespace Choi.SaveLoad
{
    [Serializable]
    public sealed class PowerSaveFile
    {
        public int schemaVersion = PowerSaveManager.CurrentSchemaVersion;
        public string savedAtUtc;
        public List<PowerSaveEntry> systems = new List<PowerSaveEntry>();
    }

    [Serializable]
    public sealed class PowerSaveEntry
    {
        public string saveId;
        public string saveType;
        public int restoreOrder;
        public string stateJson;
    }
}
