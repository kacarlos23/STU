namespace STU.Domain.Territories;

public sealed class MicroregionNeighborhood
{
    private MicroregionNeighborhood() { }

    private MicroregionNeighborhood(Guid microregionId, Guid neighborhoodId)
    {
        MicroregionId = microregionId;
        NeighborhoodId = neighborhoodId;
    }

    public Guid MicroregionId { get; private init; }
    public Guid NeighborhoodId { get; private init; }

    public static MicroregionNeighborhood Create(Guid microregionId, Guid neighborhoodId)
    {
        if (microregionId == Guid.Empty) throw new ArgumentException("Informe a microrregião.", nameof(microregionId));
        if (neighborhoodId == Guid.Empty) throw new ArgumentException("Informe o bairro.", nameof(neighborhoodId));
        return new MicroregionNeighborhood(microregionId, neighborhoodId);
    }
}
