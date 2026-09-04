using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerritoryBoundaryAlignment : Migration
    {
        private static readonly Guid[] EmptyNeighborhoodIds = [];
        private static readonly string[] HealthUnitCodeColumns = ["HealthUnitId", "Code"];
        private static readonly string[] NeighborhoodCodeColumns = ["NeighborhoodId", "Code"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_microregions_neighborhoods_NeighborhoodId",
                table: "microregions");

            migrationBuilder.DropIndex(
                name: "IX_microregions_NeighborhoodId_Code",
                table: "microregions");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "neighborhoods",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#2e8b72");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "neighborhood_versions",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#2e8b72");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "microregions",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#4f9a7d");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "microregion_versions",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#4f9a7d");

            migrationBuilder.AddColumn<Guid[]>(
                name: "NeighborhoodIds",
                table: "microregion_versions",
                type: "uuid[]",
                nullable: false,
                defaultValue: EmptyNeighborhoodIds);

            migrationBuilder.CreateTable(
                name: "microregion_neighborhoods",
                columns: table => new
                {
                    MicroregionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NeighborhoodId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_microregion_neighborhoods", x => new { x.MicroregionId, x.NeighborhoodId });
                    table.ForeignKey(
                        name: "FK_microregion_neighborhoods_microregions_MicroregionId",
                        column: x => x.MicroregionId,
                        principalTable: "microregions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_microregion_neighborhoods_neighborhoods_NeighborhoodId",
                        column: x => x.NeighborhoodId,
                        principalTable: "neighborhoods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Preserve every existing relationship before removing the former
            // one-neighborhood-per-microregion columns.
            migrationBuilder.Sql(
                """
                INSERT INTO microregion_neighborhoods ("MicroregionId", "NeighborhoodId")
                SELECT "Id", "NeighborhoodId"
                FROM microregions;

                UPDATE microregion_versions
                SET "NeighborhoodIds" = ARRAY["NeighborhoodId"]::uuid[];
                """);

            // Give existing territories distinguishable colors. Historical
            // versions inherit the current territory color when available.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "Id", row_number() OVER (ORDER BY "CreatedAtUtc", "Id") AS position
                    FROM neighborhoods
                )
                UPDATE neighborhoods AS territory
                SET "Color" = (ARRAY[
                    '#2e8b72', '#2563eb', '#d97706', '#7c3aed', '#dc2626',
                    '#0891b2', '#65a30d', '#c026d3', '#0f766e', '#9333ea'
                ])[(((ranked.position - 1) % 10) + 1)::integer]
                FROM ranked
                WHERE territory."Id" = ranked."Id";

                WITH ranked AS (
                    SELECT "Id", row_number() OVER (ORDER BY "CreatedAtUtc", "Id") AS position
                    FROM microregions
                )
                UPDATE microregions AS territory
                SET "Color" = (ARRAY[
                    '#4f9a7d', '#3b82f6', '#f59e0b', '#8b5cf6', '#ef4444',
                    '#06b6d4', '#84cc16', '#d946ef', '#14b8a6', '#a855f7'
                ])[(((ranked.position - 1) % 10) + 1)::integer]
                FROM ranked
                WHERE territory."Id" = ranked."Id";

                UPDATE neighborhood_versions AS version
                SET "Color" = territory."Color"
                FROM neighborhoods AS territory
                WHERE version."NeighborhoodId" = territory."Id";

                UPDATE microregion_versions AS version
                SET "Color" = territory."Color"
                FROM microregions AS territory
                WHERE version."MicroregionId" = territory."Id";
                """);

            migrationBuilder.DropColumn(
                name: "NeighborhoodId",
                table: "microregions");

            migrationBuilder.DropColumn(
                name: "NeighborhoodId",
                table: "microregion_versions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_neighborhoods_Color",
                table: "neighborhoods",
                sql: "\"Color\" ~ '^#[0-9a-f]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_neighborhood_versions_Color",
                table: "neighborhood_versions",
                sql: "\"Color\" ~ '^#[0-9a-f]{6}$'");

            migrationBuilder.CreateIndex(
                name: "IX_microregions_HealthUnitId_Code",
                table: "microregions",
                columns: HealthUnitCodeColumns,
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_microregions_Color",
                table: "microregions",
                sql: "\"Color\" ~ '^#[0-9a-f]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_microregion_versions_Color",
                table: "microregion_versions",
                sql: "\"Color\" ~ '^#[0-9a-f]{6}$'");

            migrationBuilder.CreateIndex(
                name: "IX_microregion_neighborhoods_NeighborhoodId",
                table: "microregion_neighborhoods",
                column: "NeighborhoodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_neighborhoods_Color",
                table: "neighborhoods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_neighborhood_versions_Color",
                table: "neighborhood_versions");

            migrationBuilder.DropIndex(
                name: "IX_microregions_HealthUnitId_Code",
                table: "microregions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_microregions_Color",
                table: "microregions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_microregion_versions_Color",
                table: "microregion_versions");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "neighborhoods");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "neighborhood_versions");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "microregions");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "microregion_versions");

            migrationBuilder.AddColumn<Guid>(
                name: "NeighborhoodId",
                table: "microregions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NeighborhoodId",
                table: "microregion_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE microregions AS microregion
                SET "NeighborhoodId" = (
                    SELECT link."NeighborhoodId"
                    FROM microregion_neighborhoods AS link
                    WHERE link."MicroregionId" = microregion."Id"
                    ORDER BY link."NeighborhoodId"
                    LIMIT 1
                );

                UPDATE microregion_versions
                SET "NeighborhoodId" = "NeighborhoodIds"[1];

                ALTER TABLE microregions
                    ALTER COLUMN "NeighborhoodId" SET NOT NULL;
                ALTER TABLE microregion_versions
                    ALTER COLUMN "NeighborhoodId" SET NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "microregion_neighborhoods");

            migrationBuilder.DropColumn(
                name: "NeighborhoodIds",
                table: "microregion_versions");

            migrationBuilder.CreateIndex(
                name: "IX_microregions_NeighborhoodId_Code",
                table: "microregions",
                columns: NeighborhoodCodeColumns,
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_microregions_neighborhoods_NeighborhoodId",
                table: "microregions",
                column: "NeighborhoodId",
                principalTable: "neighborhoods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
