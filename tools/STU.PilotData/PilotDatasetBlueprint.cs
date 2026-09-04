using NetTopologySuite.Geometries;
using STU.Application.Security;

namespace STU.PilotData;

public sealed class PilotDatasetBlueprint
{
    public const int NeighborhoodCount = 3;
    public const int MicroregionCount = 20;
    public const int PropertyCount = 3_000;
    public const int VisitCount = 4_500;
    public const int UserCount = 100;
    public const int PropertiesPerMicroregion = PropertyCount / MicroregionCount;
    public static readonly DateTimeOffset ReferenceDate = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private const double MinimumLongitude = -39.770;
    private const double MaximumLongitude = -39.700;
    private const double MinimumLatitude = -17.570;
    private const double MaximumLatitude = -17.500;

    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    public IReadOnlyList<PilotNeighborhoodSpec> Neighborhoods { get; } = CreateNeighborhoods();
    public IReadOnlyList<PilotMicroregionSpec> Microregions { get; } = CreateMicroregions();
    public IReadOnlyList<PilotAccountSpec> Accounts { get; } = CreateAccounts();

    public static IEnumerable<PilotPropertySpec> GetProperties(PilotMicroregionSpec microregion)
    {
        const int columns = 15;
        const int rows = 10;
        var envelope = microregion.Boundary.EnvelopeInternal;
        var longitudeStep = envelope.Width / (columns + 1);
        var latitudeStep = envelope.Height / (rows + 1);

        for (var localIndex = 0; localIndex < PropertiesPerMicroregion; localIndex++)
        {
            var column = localIndex % columns;
            var row = localIndex / columns;
            var globalIndex = microregion.Index * PropertiesPerMicroregion + localIndex;
            var point = GeometryFactory.CreatePoint(new Coordinate(
                envelope.MinX + longitudeStep * (column + 1),
                envelope.MinY + latitudeStep * (row + 1)));

            yield return new PilotPropertySpec(
                globalIndex,
                $"Rua Sintética {(globalIndex % 30) + 1:D2}",
                $"{globalIndex + 1}",
                $"F-{globalIndex + 1:D5}",
                $"4599{globalIndex % 100:D2}-000",
                point);
        }
    }

    private static PilotNeighborhoodSpec[] CreateNeighborhoods()
    {
        var width = (MaximumLongitude - MinimumLongitude) / NeighborhoodCount;
        var names = new[] { "Bairro Sintético Norte", "Bairro Sintético Central", "Bairro Sintético Sul" };
        var colors = new[] { "#2563eb", "#d97706", "#7c3aed" };

        return Enumerable.Range(0, NeighborhoodCount)
            .Select(index => new PilotNeighborhoodSpec(
                index,
                names[index],
                CreateRectangle(
                    MinimumLongitude + width * index,
                    MinimumLatitude,
                    MinimumLongitude + width * (index + 1),
                    MaximumLatitude),
                colors[index]))
            .ToArray();
    }

    private static PilotMicroregionSpec[] CreateMicroregions()
    {
        const int columns = 5;
        const int rows = 4;
        var width = (MaximumLongitude - MinimumLongitude) / columns;
        var height = (MaximumLatitude - MinimumLatitude) / rows;
        var colors = new[]
        {
            "#4f9a7d", "#3b82f6", "#f59e0b", "#8b5cf6", "#ef4444",
            "#06b6d4", "#84cc16", "#d946ef", "#14b8a6", "#a855f7",
        };

        return Enumerable.Range(0, MicroregionCount)
            .Select(index =>
            {
                var column = index % columns;
                var row = index / columns;
                var polygon = CreateRectangle(
                    MinimumLongitude + width * column,
                    MinimumLatitude + height * row,
                    MinimumLongitude + width * (column + 1),
                    MinimumLatitude + height * (row + 1));

                return new PilotMicroregionSpec(
                    index,
                    $"MR-{index + 1:D2}",
                    $"Microrregião Sintética {index + 1:D2}",
                    GeometryFactory.CreateMultiPolygon([polygon]),
                    colors[index % colors.Length]);
            })
            .ToArray();
    }

    private static PilotAccountSpec[] CreateAccounts()
    {
        var accounts = new List<PilotAccountSpec>(UserCount);
        AddAccounts(accounts, "agent", "Agente sintético", SystemRoles.HealthAgent, 50);
        AddAccounts(accounts, "reception", "Recepcionista sintético", SystemRoles.Receptionist, 19);
        AddAccounts(accounts, "doctor", "Médico sintético", SystemRoles.Doctor, 20);
        AddAccounts(accounts, "manager", "Gerente sintético", SystemRoles.HealthUnitManager, 10);
        accounts.Add(new PilotAccountSpec(
            "pilot.isolation.001",
            "Recepcionista de isolamento 001",
            SystemRoles.Receptionist,
            IsIsolationAccount: true));
        return [.. accounts];
    }

    private static void AddAccounts(List<PilotAccountSpec> accounts, string prefix, string displayName, string role, int count)
    {
        for (var index = 1; index <= count; index++)
        {
            accounts.Add(new PilotAccountSpec($"pilot.{prefix}.{index:D3}", $"{displayName} {index:D3}", role));
        }
    }

    private static Polygon CreateRectangle(double minX, double minY, double maxX, double maxY) =>
        GeometryFactory.CreatePolygon(
        [
            new(minX, minY),
            new(maxX, minY),
            new(maxX, maxY),
            new(minX, maxY),
            new(minX, minY),
        ]);
}

public sealed record PilotNeighborhoodSpec(int Index, string Name, Polygon Geometry, string Color);
public sealed record PilotMicroregionSpec(int Index, string Code, string Name, MultiPolygon Boundary, string Color);
public sealed record PilotAccountSpec(string UserName, string DisplayName, string Role, bool IsIsolationAccount = false);
public sealed record PilotPropertySpec(int Index, string Street, string HouseNumber, string FamilyNumber, string PostalCode, Point Geometry);
