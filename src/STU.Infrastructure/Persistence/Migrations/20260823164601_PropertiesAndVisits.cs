using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#pragma warning disable CA1861
#nullable disable

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PropertiesAndVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coverage_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    MicroregionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxDaysWithoutVisit = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coverage_rules", x => x.Id);
                    table.CheckConstraint("CK_coverage_rules_MaxDays", "\"MaxDaysWithoutVisit\" BETWEEN 1 AND 730");
                    table.ForeignKey(
                        name: "FK_coverage_rules_health_units_HealthUnitId",
                        column: x => x.HealthUnitId,
                        principalTable: "health_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_coverage_rules_microregions_MicroregionId",
                        column: x => x.MicroregionId,
                        principalTable: "microregions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operational_tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operational_tags", x => x.Id);
                    table.CheckConstraint("CK_operational_tags_Color", "\"Color\" ~ '^#[0-9a-f]{6}$'");
                    table.ForeignKey(
                        name: "FK_operational_tags_health_units_HealthUnitId",
                        column: x => x.HealthUnitId,
                        principalTable: "health_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "properties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    MicroregionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Street = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    HouseNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FamilyNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Complement = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Geometry = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: false),
                    RegistrationStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Situation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_properties", x => x.Id);
                    table.CheckConstraint("CK_properties_GeometryType", "ST_GeometryType(\"Geometry\") IN ('ST_Point','ST_Polygon','ST_MultiPolygon')");
                    table.CheckConstraint("CK_properties_GeometryValid", "ST_IsValid(\"Geometry\")");
                    table.ForeignKey(
                        name: "FK_properties_health_units_HealthUnitId",
                        column: x => x.HealthUnitId,
                        principalTable: "health_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_properties_microregions_MicroregionId",
                        column: x => x.MicroregionId,
                        principalTable: "microregions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "property_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    MicroregionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Street = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    HouseNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FamilyNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Complement = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Geometry = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: false),
                    RegistrationStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Situation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_property_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "property_tags",
                columns: table => new
                {
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_property_tags", x => new { x.PropertyId, x.TagId });
                    table.ForeignKey(
                        name: "FK_property_tags_operational_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "operational_tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_property_tags_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "property_visits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ObservedSituation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AccessDifficulty = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_property_visits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_property_visits_AspNetUsers_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_property_visits_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_coverage_rules_HealthUnitId",
                table: "coverage_rules",
                column: "HealthUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_coverage_rules_MicroregionId",
                table: "coverage_rules",
                column: "MicroregionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operational_tags_HealthUnitId_Name",
                table: "operational_tags",
                columns: new[] { "HealthUnitId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_properties_Geometry",
                table: "properties",
                column: "Geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_properties_HealthUnitId_FamilyNumber",
                table: "properties",
                columns: new[] { "HealthUnitId", "FamilyNumber" },
                unique: true,
                filter: "\"ArchivedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_properties_MicroregionId",
                table: "properties",
                column: "MicroregionId");

            migrationBuilder.CreateIndex(
                name: "IX_property_tags_TagId",
                table: "property_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_property_versions_HealthUnitId_ChangedAtUtc",
                table: "property_versions",
                columns: new[] { "HealthUnitId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_property_versions_PropertyId_VersionNumber",
                table: "property_versions",
                columns: new[] { "PropertyId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_property_visits_AgentId",
                table: "property_visits",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_property_visits_HealthUnitId_VisitedAtUtc",
                table: "property_visits",
                columns: new[] { "HealthUnitId", "VisitedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_property_visits_PropertyId_VisitedAtUtc",
                table: "property_visits",
                columns: new[] { "PropertyId", "VisitedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coverage_rules");

            migrationBuilder.DropTable(
                name: "property_tags");

            migrationBuilder.DropTable(
                name: "property_versions");

            migrationBuilder.DropTable(
                name: "property_visits");

            migrationBuilder.DropTable(
                name: "operational_tags");

            migrationBuilder.DropTable(
                name: "properties");
        }
    }
}
#pragma warning restore CA1861
