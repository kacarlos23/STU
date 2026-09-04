using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace STU.Domain.Territories;

public sealed record TerritoryBoundaryFit(Geometry? Geometry, bool Adjusted)
{
    public bool IsEmpty => Geometry is null || Geometry.IsEmpty;
}

public static class TerritoryBoundaryFitter
{
    public static TerritoryBoundaryFit Fit(Geometry candidate, IEnumerable<Geometry> occupiedAreas)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(occupiedAreas);

        if (candidate is Point)
        {
            return new TerritoryBoundaryFit((Geometry)candidate.Copy(), false);
        }

        if (candidate is not (Polygon or MultiPolygon))
        {
            throw new ArgumentException("O ajuste automático aceita somente ponto, polígono ou multipolígono.", nameof(candidate));
        }

        var occupied = occupiedAreas
            .Where(item => item is Polygon or MultiPolygon && !item.IsEmpty)
            .Select(item => (Geometry)item.Copy())
            .ToArray();

        if (occupied.Length == 0)
        {
            return new TerritoryBoundaryFit((Geometry)candidate.Copy(), false);
        }

        var occupiedUnion = UnaryUnionOp.Union(occupied);
        var difference = candidate.Difference(occupiedUnion);
        var polygonal = ExtractPolygonal(candidate.Factory, difference, candidate.SRID);
        if (polygonal is null || polygonal.IsEmpty)
        {
            return new TerritoryBoundaryFit(null, true);
        }

        return new TerritoryBoundaryFit(polygonal, !candidate.EqualsTopologically(polygonal));
    }

    public static MultiPolygon? AsMultiPolygon(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty) return null;
        if (geometry is MultiPolygon multi) return multi;
        if (geometry is not Polygon polygon) return null;

        var result = geometry.Factory.CreateMultiPolygon([polygon]);
        result.SRID = geometry.SRID;
        return result;
    }

    private static Geometry? ExtractPolygonal(GeometryFactory factory, Geometry geometry, int srid)
    {
        if (geometry is Polygon polygon)
        {
            polygon.SRID = srid;
            return polygon;
        }

        if (geometry is MultiPolygon multiPolygon)
        {
            multiPolygon.SRID = srid;
            return multiPolygon;
        }

        var polygons = new List<Polygon>();
        CollectPolygons(geometry, polygons);
        if (polygons.Count == 0) return null;
        if (polygons.Count == 1)
        {
            polygons[0].SRID = srid;
            return polygons[0];
        }

        var result = factory.CreateMultiPolygon(polygons.ToArray());
        result.SRID = srid;
        return result;
    }

    private static void CollectPolygons(Geometry geometry, ICollection<Polygon> polygons)
    {
        switch (geometry)
        {
            case Polygon polygon:
                polygons.Add((Polygon)polygon.Copy());
                break;
            case GeometryCollection collection:
                for (var index = 0; index < collection.NumGeometries; index++)
                {
                    CollectPolygons(collection.GetGeometryN(index), polygons);
                }
                break;
        }
    }
}
