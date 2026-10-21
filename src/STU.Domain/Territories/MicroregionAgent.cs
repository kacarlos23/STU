using STU.Domain.Common;

namespace STU.Domain.Territories;

public sealed class MicroregionAgent : Entity
{
    private MicroregionAgent() { }
    public Guid HealthUnitId { get; private set; }
    public Guid MicroregionId { get; private set; }
    public Guid AgentId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public Guid? EndedByUserId { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public static MicroregionAgent Create(Guid unitId, Guid microregionId, Guid agentId, Guid? actorId) =>
        new() { HealthUnitId = unitId, MicroregionId = microregionId, AgentId = agentId, CreatedByUserId = actorId };

    public void Touch() { UpdatedAtUtc = DateTimeOffset.UtcNow; ConcurrencyToken = Guid.NewGuid(); }
    public void End(Guid actorId)
    {
        if (EndedAtUtc.HasValue) throw new InvalidOperationException("O vínculo do agente já foi encerrado.");
        EndedAtUtc = DateTimeOffset.UtcNow; EndedByUserId = actorId; Touch();
    }
}
