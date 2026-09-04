using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#pragma warning disable CA1861

#nullable disable

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerritoryMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "microregion_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MicroregionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NeighborhoodId = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Boundary = table.Column<MultiPolygon>(type: "geometry(MultiPolygon,4326)", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_microregion_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "neighborhood_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NeighborhoodId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Geometry = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_neighborhood_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "neighborhoods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Geometry = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_neighborhoods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "microregions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NeighborhoodId = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Boundary = table.Column<MultiPolygon>(type: "geometry(MultiPolygon,4326)", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_microregions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_microregions_AspNetUsers_AssignedAgentId",
                        column: x => x.AssignedAgentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_microregions_health_units_HealthUnitId",
                        column: x => x.HealthUnitId,
                        principalTable: "health_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_microregions_neighborhoods_NeighborhoodId",
                        column: x => x.NeighborhoodId,
                        principalTable: "neighborhoods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_microregion_versions_HealthUnitId_ChangedAtUtc",
                table: "microregion_versions",
                columns: new[] { "HealthUnitId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_microregion_versions_MicroregionId_VersionNumber",
                table: "microregion_versions",
                columns: new[] { "MicroregionId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_microregions_AssignedAgentId",
                table: "microregions",
                column: "AssignedAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_microregions_Boundary",
                table: "microregions",
                column: "Boundary")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_microregions_HealthUnitId",
                table: "microregions",
                column: "HealthUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_microregions_NeighborhoodId_Code",
                table: "microregions",
                columns: new[] { "NeighborhoodId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_neighborhood_versions_ChangedAtUtc",
                table: "neighborhood_versions",
                column: "ChangedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_neighborhood_versions_NeighborhoodId_VersionNumber",
                table: "neighborhood_versions",
                columns: new[] { "NeighborhoodId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_neighborhoods_Geometry",
                table: "neighborhoods",
                column: "Geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_neighborhoods_Name",
                table: "neighborhoods",
                column: "Name");

            migrationBuilder.Sql("ALTER TABLE neighborhoods ADD CONSTRAINT ck_neighborhoods_geometry_valid CHECK (ST_IsValid(\"Geometry\"));");
            migrationBuilder.Sql("ALTER TABLE neighborhoods ADD CONSTRAINT ck_neighborhoods_geometry_type CHECK (GeometryType(\"Geometry\") IN ('POINT','POLYGON','MULTIPOLYGON'));");
            migrationBuilder.Sql("ALTER TABLE microregions ADD CONSTRAINT ck_microregions_boundary_valid CHECK (ST_IsValid(\"Boundary\"));");
            migrationBuilder.Sql("ALTER TABLE neighborhood_versions ADD CONSTRAINT ck_neighborhood_versions_geometry_valid CHECK (ST_IsValid(\"Geometry\"));");
            migrationBuilder.Sql("ALTER TABLE microregion_versions ADD CONSTRAINT ck_microregion_versions_boundary_valid CHECK (ST_IsValid(\"Boundary\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "microregion_versions");

            migrationBuilder.DropTable(
                name: "microregions");

            migrationBuilder.DropTable(
                name: "neighborhood_versions");

            migrationBuilder.DropTable(
                name: "neighborhoods");
        }
    }
}
