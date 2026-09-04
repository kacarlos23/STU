using STU.Domain.Common;

namespace STU.Domain.Properties;

public sealed class OperationalTag : Entity
{
    private OperationalTag() { }
    private OperationalTag(Guid healthUnitId,string name,string color){HealthUnitId=healthUnitId;Name=name.Trim();Color=NormalizeColor(color);}
    public Guid HealthUnitId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Color { get; private set; } = "#4f9a7d";
    public static OperationalTag Create(Guid unitId,string name,string color){ArgumentException.ThrowIfNullOrWhiteSpace(name);return new(unitId,name,color);}
    public void Update(string name,string color){ArgumentException.ThrowIfNullOrWhiteSpace(name);Name=name.Trim();Color=NormalizeColor(color);UpdatedAtUtc=DateTimeOffset.UtcNow;}
    public void Archive(){ArchivedAtUtc??=DateTimeOffset.UtcNow;UpdatedAtUtc=DateTimeOffset.UtcNow;}
    public void Restore(){ArchivedAtUtc=null;UpdatedAtUtc=DateTimeOffset.UtcNow;}
    private static string NormalizeColor(string color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        var normalized=color.Trim().ToLowerInvariant();
        if(normalized.Length!=7||normalized[0]!='#'||normalized[1..].Any(character=>!Uri.IsHexDigit(character)))throw new ArgumentException("Informe uma cor hexadecimal válida.",nameof(color));
        return normalized;
    }
}
