namespace STU.Application.Security;

public static class StuPermissions
{
    public const string All = "*";
    public const string MapView = "map.view";
    public const string PropertiesView = "properties.view";
    public const string PropertiesManage = "properties.manage";
    public const string VisitsView = "visits.view";
    public const string VisitsManage = "visits.manage";
    public const string TerritoryManage = "territory.manage";
    public const string HealthUnitUsersManage = "health_unit.users.manage";
    public const string ReportsExport = "reports.export";

    public static IReadOnlyList<StuPermissionDefinition> Catalog { get; } =
    [
        new(MapView, "Visualizar mapa", "Território"),
        new(TerritoryManage, "Gerenciar territórios", "Território"),
        new(PropertiesView, "Consultar imóveis", "Imóveis"),
        new(PropertiesManage, "Gerenciar imóveis", "Imóveis"),
        new(VisitsView, "Consultar visitas", "Visitas"),
        new(VisitsManage, "Registrar e editar visitas", "Visitas"),
        new(HealthUnitUsersManage, "Gerenciar servidores da UBS", "Gestão"),
        new(ReportsExport, "Exportar relatórios", "Gestão"),
    ];

    public static bool IsConfigurable(string permission) =>
        Catalog.Any(definition => definition.Value == permission);
}

public sealed record StuPermissionDefinition(string Value, string DisplayName, string Category);
