using STU.Domain.Common;

namespace STU.Domain.Properties;

public sealed class PropertyVisit : Entity
{
    private PropertyVisit() { }
    private PropertyVisit(Guid familyId,Guid propertyId,Guid healthUnitId,Guid agentId,DateTimeOffset visitedAtUtc,VisitType type,VisitOutcome outcome,PropertySituation observedSituation,bool accessDifficulty,string? note)
    {FamilyId=familyId;PropertyId=propertyId;HealthUnitId=healthUnitId;AgentId=agentId;VisitedAtUtc=visitedAtUtc;Type=type;Outcome=outcome;ObservedSituation=observedSituation;AccessDifficulty=accessDifficulty;Note=Clean(note);}
    public Guid FamilyId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid HealthUnitId { get; private set; }
    public Guid AgentId { get; private set; }
    public DateTimeOffset VisitedAtUtc { get; private set; }
    public VisitType Type { get; private set; }
    public VisitOutcome Outcome { get; private set; }
    public PropertySituation ObservedSituation { get; private set; }
    public bool AccessDifficulty { get; private set; }
    public string? Note { get; private set; }
    public Guid ConcurrencyToken { get; private set; }=Guid.NewGuid();
    public static PropertyVisit Create(Guid familyId,Guid propertyId,Guid healthUnitId,Guid agentId,DateTimeOffset date,VisitType type,VisitOutcome outcome,PropertySituation situation,bool difficulty,string? note)=>new(familyId,propertyId,healthUnitId,agentId,date,type,outcome,situation,difficulty,note);
    public void Update(DateTimeOffset date,VisitType type,VisitOutcome outcome,PropertySituation situation,bool difficulty,string? note){VisitedAtUtc=date;Type=type;Outcome=outcome;ObservedSituation=situation;AccessDifficulty=difficulty;Note=Clean(note);UpdatedAtUtc=DateTimeOffset.UtcNow;ConcurrencyToken=Guid.NewGuid();}
    public void Archive(){ArchivedAtUtc??=DateTimeOffset.UtcNow;UpdatedAtUtc=DateTimeOffset.UtcNow;ConcurrencyToken=Guid.NewGuid();}
    public void Restore(){ArchivedAtUtc=null;UpdatedAtUtc=DateTimeOffset.UtcNow;ConcurrencyToken=Guid.NewGuid();}
    private static string? Clean(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
}
