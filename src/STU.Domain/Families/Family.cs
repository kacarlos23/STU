using STU.Domain.Common;

namespace STU.Domain.Families;

public sealed class Family : Entity
{
    private Family() { }

    private Family(Guid healthUnitId, string number, string responsibleName, Guid actorId)
    {
        HealthUnitId = healthUnitId;
        Number = NormalizeNumber(number);
        ResponsibleName = CleanResponsibleName(responsibleName);
        CreatedByUserId = actorId;
        UpdatedByUserId = actorId;
    }

    public Guid HealthUnitId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string ResponsibleName { get; private set; } = string.Empty;
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }

    public static Family Create(Guid healthUnitId, string number, string responsibleName, Guid actorId)
    {
        Validate(number, responsibleName);
        return new(healthUnitId, number, responsibleName, actorId);
    }

    public void Update(string number, string responsibleName, Guid actorId)
    {
        Validate(number, responsibleName);
        Number = NormalizeNumber(number);
        ResponsibleName = CleanResponsibleName(responsibleName);
        Touch(actorId);
    }

    public void Archive(Guid actorId)
    {
        if (!IsArchived)
        {
            ArchivedAtUtc = DateTimeOffset.UtcNow;
            Touch(actorId);
        }
    }

    public void Restore(Guid actorId)
    {
        if (IsArchived)
        {
            ArchivedAtUtc = null;
            Touch(actorId);
        }
    }

    public void Touch(Guid actorId)
    {
        UpdatedByUserId = actorId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static void Validate(string number, string responsibleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(responsibleName);
        if (number.Any(char.IsControl) || responsibleName.Any(char.IsControl)) throw new ArgumentException("Número e responsável devem ser texto simples, sem caracteres de controle.");
        if (number.Trim().Length > 32) throw new ArgumentException("O número da família pode ter até 32 caracteres.", nameof(number));
        if (responsibleName.Trim().Length > 120) throw new ArgumentException("O nome do responsável pode ter até 120 caracteres.", nameof(responsibleName));
    }

    private static string NormalizeNumber(string value) => value.Trim().ToUpperInvariant();
    private static string CleanResponsibleName(string value) => value.Trim();
}
