using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable
#pragma warning disable CA1861 // EF-generated migration arrays are used once.

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Geometry>(
                name: "Geometry",
                table: "property_versions",
                type: "geometry(Geometry,4326)",
                nullable: true,
                oldClrType: typeof(Geometry),
                oldType: "geometry(Geometry,4326)");

            migrationBuilder.AddColumn<Guid>(
                name: "MicroregionAgentId",
                table: "property_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NeighborhoodId",
                table: "property_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "property_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<Geometry>(
                name: "Geometry",
                table: "properties",
                type: "geometry(Geometry,4326)",
                nullable: true,
                oldClrType: typeof(Geometry),
                oldType: "geometry(Geometry,4326)");

            migrationBuilder.AddColumn<Guid>(
                name: "MicroregionAgentId",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NeighborhoodId",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "properties",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AlterColumn<MultiPolygon>(
                name: "Boundary",
                table: "microregions",
                type: "geometry(MultiPolygon,4326)",
                nullable: true,
                oldClrType: typeof(MultiPolygon),
                oldType: "geometry(MultiPolygon,4326)");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_microregions_Id_HealthUnitId",
                table: "microregions",
                columns: new[] { "Id", "HealthUnitId" });

            migrationBuilder.CreateTable(
                name: "microregion_agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    MicroregionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EndedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_microregion_agents", x => x.Id);
                    table.UniqueConstraint("AK_microregion_agents_Id_HealthUnitId_MicroregionId", x => new { x.Id, x.HealthUnitId, x.MicroregionId });
                    table.CheckConstraint("CK_microregion_agents_Dates", "\"EndedAtUtc\" IS NULL OR \"EndedAtUtc\" >= \"StartedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_microregion_agents_AspNetUsers_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_microregion_agents_microregions_MicroregionId_HealthUnitId",
                        columns: x => new { x.MicroregionId, x.HealthUnitId },
                        principalTable: "microregions",
                        principalColumns: new[] { "Id", "HealthUnitId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_properties_MicroregionAgentId_HealthUnitId_MicroregionId",
                table: "properties",
                columns: new[] { "MicroregionAgentId", "HealthUnitId", "MicroregionId" });

            migrationBuilder.CreateIndex(
                name: "IX_properties_NeighborhoodId",
                table: "properties",
                column: "NeighborhoodId");

            migrationBuilder.CreateIndex(
                name: "IX_microregion_agents_AgentId",
                table: "microregion_agents",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_microregion_agents_HealthUnitId_AgentId",
                table: "microregion_agents",
                columns: new[] { "HealthUnitId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_microregion_agents_MicroregionId_AgentId",
                table: "microregion_agents",
                columns: new[] { "MicroregionId", "AgentId" },
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_microregion_agents_MicroregionId_HealthUnitId",
                table: "microregion_agents",
                columns: new[] { "MicroregionId", "HealthUnitId" });

            migrationBuilder.AddForeignKey(
                name: "FK_properties_microregion_agents_MicroregionAgentId_HealthUnit~",
                table: "properties",
                columns: new[] { "MicroregionAgentId", "HealthUnitId", "MicroregionId" },
                principalTable: "microregion_agents",
                principalColumns: new[] { "Id", "HealthUnitId", "MicroregionId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_properties_neighborhoods_NeighborhoodId",
                table: "properties",
                column: "NeighborhoodId",
                principalTable: "neighborhoods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Preserve current responsibilities and identifiers; no operational row is deleted.
            migrationBuilder.Sql("""
                INSERT INTO microregion_agents
                    ("Id", "HealthUnitId", "MicroregionId", "AgentId", "StartedAtUtc", "ConcurrencyToken", "CreatedAtUtc")
                SELECT gen_random_uuid(), m."HealthUnitId", m."Id", m."AssignedAgentId", m."CreatedAtUtc", gen_random_uuid(), m."CreatedAtUtc"
                FROM microregions m JOIN "AspNetUsers" u ON u."Id" = m."AssignedAgentId"
                WHERE m."AssignedAgentId" IS NOT NULL AND u."HealthUnitId" = m."HealthUnitId";

                UPDATE properties p SET "MicroregionAgentId" = a."Id"
                FROM microregion_agents a WHERE p."MicroregionId" = a."MicroregionId" AND p."HealthUnitId" = a."HealthUnitId";

                UPDATE properties p SET "NeighborhoodId" = n."NeighborhoodId"
                FROM microregion_neighborhoods n WHERE p."MicroregionId" = n."MicroregionId"
                AND (SELECT count(*) FROM microregion_neighborhoods other WHERE other."MicroregionId" = n."MicroregionId") = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_properties_microregion_agents_MicroregionAgentId_HealthUnit~",
                table: "properties");

            migrationBuilder.DropForeignKey(
                name: "FK_properties_neighborhoods_NeighborhoodId",
                table: "properties");

            migrationBuilder.DropTable(
                name: "microregion_agents");

            migrationBuilder.DropIndex(
                name: "IX_properties_MicroregionAgentId_HealthUnitId_MicroregionId",
                table: "properties");

            migrationBuilder.DropIndex(
                name: "IX_properties_NeighborhoodId",
                table: "properties");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_microregions_Id_HealthUnitId",
                table: "microregions");

            migrationBuilder.DropColumn(
                name: "MicroregionAgentId",
                table: "property_versions");

            migrationBuilder.DropColumn(
                name: "NeighborhoodId",
                table: "property_versions");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "property_versions");

            migrationBuilder.DropColumn(
                name: "MicroregionAgentId",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "NeighborhoodId",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "properties");

            migrationBuilder.AlterColumn<Geometry>(
                name: "Geometry",
                table: "property_versions",
                type: "geometry(Geometry,4326)",
                nullable: false,
                oldClrType: typeof(Geometry),
                oldType: "geometry(Geometry,4326)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Geometry>(
                name: "Geometry",
                table: "properties",
                type: "geometry(Geometry,4326)",
                nullable: false,
                oldClrType: typeof(Geometry),
                oldType: "geometry(Geometry,4326)",
                oldNullable: true);

            migrationBuilder.AlterColumn<MultiPolygon>(
                name: "Boundary",
                table: "microregions",
                type: "geometry(MultiPolygon,4326)",
                nullable: false,
                oldClrType: typeof(MultiPolygon),
                oldType: "geometry(MultiPolygon,4326)",
                oldNullable: true);
        }
    }
}
