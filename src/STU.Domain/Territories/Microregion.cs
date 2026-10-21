using NetTopologySuite.Geometries;
using STU.Domain.Common;

namespace STU.Domain.Territories;

public sealed class Microregion : Entity
{
    private Microregion() { }
    private Microregion(string code, string name, Guid healthUnitId, Guid? assignedAgentId, MultiPolygon? boundary, TerritorySource source, string? color)
    {
        Code = NormalizeCode(code); Name = name.Trim(); NormalizedName = NormalizeName(name); HealthUnitId = healthUnitId;
        AssignedAgentId = assignedAgentId; Boundary = boundary; Source = source;
        Color = TerritoryColor.Normalize(color, TerritoryColor.DefaultMicroregion);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public Guid HealthUnitId { get; private set; }
    public Guid? AssignedAgentId { get; private set; }
    public MultiPolygon? Boundary { get; private set; }
    public TerritorySource Source { get; private set; }
    public string Color { get; private set; } = TerritoryColor.DefaultMicroregion;
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public static Microregion Create(string code, string name, Guid healthUnitId, Guid? assignedAgentId, MultiPolygon boundary, TerritorySource source, string? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code); ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(boundary);
        return new Microregion(code, name, healthUnitId, assignedAgentId, boundary, source, color);
    }

    public void Update(string code, string name, Guid healthUnitId, Guid? assignedAgentId, MultiPolygon boundary, TerritorySource source, string? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Code = NormalizeCode(code); Name = name.Trim(); NormalizedName = NormalizeName(name); HealthUnitId = healthUnitId;
        AssignedAgentId = assignedAgentId; Boundary = boundary; Source = source;
        Color = TerritoryColor.Normalize(color, TerritoryColor.DefaultMicroregion); Touch();
    }

    public void Archive() { if (!IsArchived) { ArchivedAtUtc = DateTimeOffset.UtcNow; Touch(); } }
    public static Microregion CreateRegistration(string code, string name, Guid healthUnitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(code, name, healthUnitId, null, null, TerritorySource.Manual, null);
    }
    public void UpdateRegistration(string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Code = NormalizeCode(code); Name = name.Trim(); NormalizedName = NormalizeName(name); Touch();
    }
    public void SetPrimaryAgent(Guid? agentId) { AssignedAgentId = agentId; Touch(); }
    public void Restore() { if (IsArchived) { ArchivedAtUtc = null; Touch(); } }
    private void Touch() { UpdatedAtUtc = DateTimeOffset.UtcNow; ConcurrencyToken = Guid.NewGuid(); }
    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string NormalizeName(string name) => name.Trim().ToUpperInvariant();
}
