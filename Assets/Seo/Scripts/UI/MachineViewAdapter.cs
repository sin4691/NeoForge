using System.Reflection;
using Factory.Buildings;
using Factory.Simulation;

namespace Seo.UI
{
    // MachineView에 getter를 추가하지 않고 Seo UI에서 필요한 선택 정보만 읽는 격리 어댑터.
    // 공동 클래스의 private 필드 구조가 바뀌면 이 파일 하나만 맞추면 된다.
    internal static class MachineViewAdapter
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo KindField = typeof(MachineView).GetField("kind", PrivateInstance);
        private static readonly FieldInfo IndexField = typeof(MachineView).GetField("instanceIndex", PrivateInstance);
        private static readonly FieldInfo DriverField = typeof(MachineView).GetField("driver", PrivateInstance);

        public static bool TryRead(MachineView view, out MachineSelection selection)
        {
            selection = default;
            if (view == null || KindField == null || IndexField == null || DriverField == null) return false;

            var driver = DriverField.GetValue(view) as SimulationDriver;
            if (driver == null) return false;

            selection = new MachineSelection(
                (MachineInstanceKind)KindField.GetValue(view),
                (int)IndexField.GetValue(view),
                driver);
            return true;
        }
    }

    internal readonly struct MachineSelection
    {
        public readonly MachineInstanceKind Kind;
        public readonly int InstanceIndex;
        public readonly SimulationDriver Driver;

        public MachineSelection(MachineInstanceKind kind, int instanceIndex, SimulationDriver driver)
        {
            Kind = kind;
            InstanceIndex = instanceIndex;
            Driver = driver;
        }
    }
}
