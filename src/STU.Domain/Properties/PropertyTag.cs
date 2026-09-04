namespace STU.Domain.Properties;

public sealed class PropertyTag
{
    private PropertyTag() { }
    private PropertyTag(Guid propertyId,Guid tagId){PropertyId=propertyId;TagId=tagId;}
    public Guid PropertyId { get; private init; }
    public Guid TagId { get; private init; }
    public static PropertyTag Create(Guid propertyId,Guid tagId)=>new(propertyId,tagId);
}
