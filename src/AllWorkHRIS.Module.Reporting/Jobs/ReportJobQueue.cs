using System.Threading.Channels;

namespace AllWorkHRIS.Module.Reporting.Jobs;

/// <summary>
/// Module-owned wrapper around the report-generation channel. Exists so the
/// container has a distinct service type for this queue — registering bare
/// <see cref="Channel{T}"/> of <see cref="Guid"/> would collide with the
/// Payroll module's run queue (Autofac last-wins on identical service
/// types), routing payroll run ids into the report-generation job and
/// stalling payroll calc.
/// </summary>
public sealed class ReportJobQueue
{
    public Channel<Guid> Channel { get; } =
        System.Threading.Channels.Channel.CreateUnbounded<Guid>();
}
