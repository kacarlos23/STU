using STU.Domain.Common;

namespace STU.Domain.Properties;

public sealed class CoverageRule : Entity
{
    private CoverageRule() { }
    private CoverageRule(Guid healthUnitId,Guid microregionId,int maxDays){HealthUnitId=healthUnitId;MicroregionId=microregionId;MaxDaysWithoutVisit=Validate(maxDays);}
    public Guid HealthUnitId { get; private set; }
    public Guid MicroregionId { get; private set; }
    public int MaxDaysWithoutVisit { get; private set; }
    public static CoverageRule Create(Guid unitId,Guid microregionId,int days)=>new(unitId,microregionId,days);
    public void Update(int days){MaxDaysWithoutVisit=Validate(days);UpdatedAtUtc=DateTimeOffset.UtcNow;}
    private static int Validate(int days)=>days is >=1 and <=730?days:throw new ArgumentOutOfRangeException(nameof(days),"O prazo deve estar entre 1 e 730 dias.");
}
