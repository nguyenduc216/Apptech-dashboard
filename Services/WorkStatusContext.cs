using ApptechDashboard.Models;

namespace ApptechDashboard.Services;

// A mutable holder flows through async calls, scoped to one request, never shared across users.
public sealed class WorkStatusContext
{
    private static readonly AsyncLocal<WorkStatusContext?> CurrentSlot = new();
    public static WorkStatusContext? Current => CurrentSlot.Value;
    public IReadOnlyList<WorkStatus> Statuses { get; set; } = [];
    public static void Begin() => CurrentSlot.Value=new WorkStatusContext();
    public static void End() => CurrentSlot.Value=null;
}
