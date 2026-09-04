using NetTopologySuite.Geometries;
using STU.Domain.Common;

namespace STU.Domain.Properties;

public sealed class HealthProperty : Entity
{
    private HealthProperty() { }
    private HealthProperty(Guid healthUnitId, Guid microregionId, string street, string houseNumber, string familyNumber, string? postalCode, string? complement, Geometry geometry, PropertyRegistrationStatus registrationStatus, PropertySituation situation)
    {
        HealthUnitId = healthUnitId; MicroregionId = microregionId; Street = street.Trim(); HouseNumber = Normalize(houseNumber);
        FamilyNumber = Normalize(familyNumber); PostalCode = Clean(postalCode); Complement = Clean(complement); Geometry = geometry;
        RegistrationStatus = registrationStatus; Situation = situation;
    }

    public Guid HealthUnitId { get; private set; }
    public Guid MicroregionId { get; private set; }
    public string Street { get; private set; } = string.Empty;
    public string HouseNumber { get; private set; } = string.Empty;
    public string FamilyNumber { get; private set; } = string.Empty;
    public string? PostalCode { get; private set; }
    public string? Complement { get; private set; }
    public Geometry Geometry { get; private set; } = default!;
    public PropertyRegistrationStatus RegistrationStatus { get; private set; }
    public PropertySituation Situation { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public static HealthProperty Create(Guid healthUnitId, Guid microregionId, string street, string houseNumber, string familyNumber, string? postalCode, string? complement, Geometry geometry, PropertyRegistrationStatus registrationStatus, PropertySituation situation)
    {
        Validate(street, houseNumber, familyNumber, geometry);
        return new(healthUnitId, microregionId, street, houseNumber, familyNumber, postalCode, complement, geometry, registrationStatus, situation);
    }

    public void Update(Guid microregionId, string street, string houseNumber, string familyNumber, string? postalCode, string? complement, Geometry geometry, PropertyRegistrationStatus registrationStatus, PropertySituation situation)
    {
        Validate(street, houseNumber, familyNumber, geometry);
        MicroregionId = microregionId; Street = street.Trim(); HouseNumber = Normalize(houseNumber); FamilyNumber = Normalize(familyNumber);
        PostalCode = Clean(postalCode); Complement = Clean(complement); Geometry = geometry; RegistrationStatus = registrationStatus; Situation = situation; Touch();
    }

    public void Archive() { if (!IsArchived) { ArchivedAtUtc = DateTimeOffset.UtcNow; Touch(); } }
    public void Restore() { if (IsArchived) { ArchivedAtUtc = null; Touch(); } }
    private void Touch() { UpdatedAtUtc = DateTimeOffset.UtcNow; ConcurrencyToken = Guid.NewGuid(); }
    private static void Validate(string street, string house, string family, Geometry geometry) { ArgumentException.ThrowIfNullOrWhiteSpace(street); ArgumentException.ThrowIfNullOrWhiteSpace(house); ArgumentException.ThrowIfNullOrWhiteSpace(family); ArgumentNullException.ThrowIfNull(geometry); }
    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
