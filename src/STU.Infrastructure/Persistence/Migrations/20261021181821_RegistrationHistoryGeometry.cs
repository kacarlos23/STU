using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationHistoryGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<MultiPolygon>(
                name: "Boundary",
                table: "microregion_versions",
                type: "geometry(MultiPolygon,4326)",
                nullable: true,
                oldClrType: typeof(MultiPolygon),
                oldType: "geometry(MultiPolygon,4326)");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION stu_family_link_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Histórico residencial imutável' USING ERRCODE='23514'; END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF OLD."EndedAtUtc" IS NOT NULL OR NEW."EndedAtUtc" IS NULL OR NEW."EndedByUserId" IS NULL
                            OR (to_jsonb(NEW) - ARRAY['EndedAtUtc','EndedByUserId','EndReason','UpdatedAtUtc']) IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['EndedAtUtc','EndedByUserId','EndReason','UpdatedAtUtc']) THEN
                            RAISE EXCEPTION 'Somente o encerramento do vínculo atual é permitido' USING ERRCODE='23514';
                        END IF;
                    ELSE
                        PERFORM 1 FROM families WHERE "Id"=NEW."FamilyId" AND "HealthUnitId"=NEW."HealthUnitId" AND "ArchivedAtUtc" IS NULL FOR UPDATE;
                        IF NOT FOUND THEN RAISE EXCEPTION 'Família indisponível' USING ERRCODE='23514'; END IF;
                        PERFORM 1 FROM properties p JOIN microregions m ON m."Id"=p."MicroregionId"
                            WHERE p."Id"=NEW."PropertyId" AND p."HealthUnitId"=NEW."HealthUnitId" AND p."ArchivedAtUtc" IS NULL AND p."RegistrationStatus"='Active'
                              AND m."ArchivedAtUtc" IS NULL AND m."HealthUnitId"=NEW."HealthUnitId"
                              AND ((p."Geometry" IS NOT NULL AND ST_Covers(m."Boundary",p."Geometry")) OR
                                   (p."Geometry" IS NULL AND EXISTS(SELECT 1 FROM microregion_agents a WHERE a."Id"=p."MicroregionAgentId"
                                       AND a."MicroregionId"=p."MicroregionId" AND a."HealthUnitId"=p."HealthUnitId" AND a."EndedAtUtc" IS NULL))) FOR UPDATE OF p;
                        IF NOT FOUND OR NEW."EndedAtUtc" IS NOT NULL THEN RAISE EXCEPTION 'Imóvel indisponível ou vínculo inválido' USING ERRCODE='23514'; END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<MultiPolygon>(
                name: "Boundary",
                table: "microregion_versions",
                type: "geometry(MultiPolygon,4326)",
                nullable: false,
                oldClrType: typeof(MultiPolygon),
                oldType: "geometry(MultiPolygon,4326)",
                oldNullable: true);
        }
    }
}
