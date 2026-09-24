using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated migration arrays are used once.

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndependentFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF (EXISTS (SELECT 1 FROM properties) OR EXISTS (SELECT 1 FROM property_visits) OR EXISTS (SELECT 1 FROM property_versions) OR EXISTS (SELECT 1 FROM operation_jobs))
                        AND current_setting('stu.allow_family_reset', true) IS DISTINCT FROM 'on' THEN
                        RAISE EXCEPTION 'Reinicialização operacional bloqueada. Execute a prévia de STU.FamilyReset e confirme ambiente e contagens antes da implantação.';
                    END IF;
                END $$;
                DELETE FROM property_tags;
                DELETE FROM property_visits;
                DELETE FROM property_versions;
                DELETE FROM properties;
                DELETE FROM operation_jobs WHERE "Kind" IN ('PropertyImport', 'PropertyExport');
                INSERT INTO audit_entries ("Id", "OccurredAtUtc", "ActorUserId", "ActorUserName", "Action", "EntityType", "EntityId", "Summary")
                VALUES (gen_random_uuid(), now(), '00000000-0000-0000-0000-000000000000', 'family-model-migration', 'Reset', 'FamiliesReset', 'independent-families', 'Modelo familiar adotado; dados operacionais reiniciados e aprovações anteriores invalidadas.');
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_property_visits_properties_PropertyId",
                table: "property_visits");

            migrationBuilder.DropIndex(
                name: "IX_properties_HealthUnitId_FamilyNumber",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "FamilyNumber",
                table: "property_versions");

            migrationBuilder.DropColumn(
                name: "FamilyNumber",
                table: "properties");

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "property_visits",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "FamilyCount",
                table: "operation_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LinkCount",
                table: "operation_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_properties_Id_HealthUnitId",
                table: "properties",
                columns: new[] { "Id", "HealthUnitId" });

            migrationBuilder.CreateTable(
                name: "families",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResponsibleName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_families", x => x.Id);
                    table.UniqueConstraint("AK_families_Id_HealthUnitId", x => new { x.Id, x.HealthUnitId });
                    table.ForeignKey(
                        name: "FK_families_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_families_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_families_health_units_HealthUnitId",
                        column: x => x.HealthUnitId,
                        principalTable: "health_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "family_property_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EndReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_property_links", x => x.Id);
                    table.CheckConstraint("CK_family_property_links_Period", "\"EndedAtUtc\" IS NULL OR \"EndedAtUtc\" > \"StartedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_family_property_links_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_family_property_links_AspNetUsers_EndedByUserId",
                        column: x => x.EndedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_family_property_links_families_FamilyId_HealthUnitId",
                        columns: x => new { x.FamilyId, x.HealthUnitId },
                        principalTable: "families",
                        principalColumns: new[] { "Id", "HealthUnitId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_family_property_links_properties_PropertyId_HealthUnitId",
                        columns: x => new { x.PropertyId, x.HealthUnitId },
                        principalTable: "properties",
                        principalColumns: new[] { "Id", "HealthUnitId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "family_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResponsibleName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_family_versions_families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_property_visits_FamilyId_HealthUnitId",
                table: "property_visits",
                columns: new[] { "FamilyId", "HealthUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_property_visits_FamilyId_VisitedAtUtc",
                table: "property_visits",
                columns: new[] { "FamilyId", "VisitedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_property_visits_PropertyId_HealthUnitId",
                table: "property_visits",
                columns: new[] { "PropertyId", "HealthUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_properties_HealthUnitId",
                table: "properties",
                column: "HealthUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_families_CreatedByUserId",
                table: "families",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_families_HealthUnitId_ArchivedAtUtc",
                table: "families",
                columns: new[] { "HealthUnitId", "ArchivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_families_HealthUnitId_Number",
                table: "families",
                columns: new[] { "HealthUnitId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_families_UpdatedByUserId",
                table: "families",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_family_property_links_CreatedByUserId",
                table: "family_property_links",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_family_property_links_EndedByUserId",
                table: "family_property_links",
                column: "EndedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_family_property_links_FamilyId",
                table: "family_property_links",
                column: "FamilyId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_family_property_links_FamilyId_HealthUnitId",
                table: "family_property_links",
                columns: new[] { "FamilyId", "HealthUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_family_property_links_HealthUnitId_StartedAtUtc",
                table: "family_property_links",
                columns: new[] { "HealthUnitId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_family_property_links_PropertyId",
                table: "family_property_links",
                column: "PropertyId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_family_property_links_PropertyId_HealthUnitId",
                table: "family_property_links",
                columns: new[] { "PropertyId", "HealthUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_family_versions_FamilyId_VersionNumber",
                table: "family_versions",
                columns: new[] { "FamilyId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_family_versions_HealthUnitId_ChangedAtUtc",
                table: "family_versions",
                columns: new[] { "HealthUnitId", "ChangedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_property_visits_families_FamilyId_HealthUnitId",
                table: "property_visits",
                columns: new[] { "FamilyId", "HealthUnitId" },
                principalTable: "families",
                principalColumns: new[] { "Id", "HealthUnitId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_property_visits_properties_PropertyId_HealthUnitId",
                table: "property_visits",
                columns: new[] { "PropertyId", "HealthUnitId" },
                principalTable: "properties",
                principalColumns: new[] { "Id", "HealthUnitId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION stu_family_link_guard() RETURNS trigger LANGUAGE plpgsql AS $$
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
                              AND m."ArchivedAtUtc" IS NULL AND m."HealthUnitId"=NEW."HealthUnitId" AND ST_Covers(m."Boundary",p."Geometry") FOR UPDATE OF p;
                        IF NOT FOUND OR NEW."EndedAtUtc" IS NOT NULL THEN RAISE EXCEPTION 'Imóvel indisponível ou vínculo inválido' USING ERRCODE='23514'; END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER family_link_guard BEFORE INSERT OR UPDATE OR DELETE ON family_property_links FOR EACH ROW EXECUTE FUNCTION stu_family_link_guard();
                CREATE FUNCTION stu_family_archive_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Arquive o cadastro em vez de excluir' USING ERRCODE='23514'; END IF;
                    IF TG_TABLE_NAME='families' THEN
                        IF NEW."ArchivedAtUtc" IS NOT NULL AND EXISTS(SELECT 1 FROM family_property_links WHERE "FamilyId"=NEW."Id" AND "EndedAtUtc" IS NULL) THEN
                            RAISE EXCEPTION 'Encerre o vínculo antes de arquivar a família' USING ERRCODE='23514';
                        END IF;
                    ELSE
                        IF (NEW."ArchivedAtUtc" IS NOT NULL OR NEW."RegistrationStatus"<>'Active') AND EXISTS(SELECT 1 FROM family_property_links WHERE "PropertyId"=NEW."Id" AND "EndedAtUtc" IS NULL) THEN
                            RAISE EXCEPTION 'Encerre o vínculo antes de desativar o imóvel' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER family_archive_guard BEFORE UPDATE OR DELETE ON families FOR EACH ROW EXECUTE FUNCTION stu_family_archive_guard();
                CREATE TRIGGER property_family_guard BEFORE UPDATE ON properties FOR EACH ROW EXECUTE FUNCTION stu_family_archive_guard();
                CREATE FUNCTION stu_family_version_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                    RAISE EXCEPTION 'Histórico familiar imutável' USING ERRCODE='23514';
                END $$;
                CREATE TRIGGER family_version_guard BEFORE UPDATE OR DELETE ON family_versions FOR EACH ROW EXECUTE FUNCTION stu_family_version_guard();
                CREATE FUNCTION stu_audit_history_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                    RAISE EXCEPTION 'Auditoria imutável: somente inclusão é permitida' USING ERRCODE='23514';
                END $$;
                CREATE TRIGGER audit_history_guard BEFORE UPDATE OR DELETE ON audit_entries FOR EACH ROW EXECUTE FUNCTION stu_audit_history_guard();
                CREATE FUNCTION stu_family_visit_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                    IF TG_OP='UPDATE' AND (NEW."FamilyId",NEW."PropertyId",NEW."HealthUnitId") IS DISTINCT FROM (OLD."FamilyId",OLD."PropertyId",OLD."HealthUnitId") THEN
                        RAISE EXCEPTION 'Família e imóvel da visita são imutáveis' USING ERRCODE='23514';
                    END IF;
                    IF TG_OP='INSERT' AND NOT EXISTS(SELECT 1 FROM family_property_links WHERE "FamilyId"=NEW."FamilyId" AND "PropertyId"=NEW."PropertyId" AND "HealthUnitId"=NEW."HealthUnitId" AND "EndedAtUtc" IS NULL) THEN
                        RAISE EXCEPTION 'Visita exige vínculo residencial atual' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER family_visit_guard BEFORE INSERT OR UPDATE ON property_visits FOR EACH ROW EXECUTE FUNCTION stu_family_visit_guard();
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue")
                SELECT c."RoleId", c."ClaimType", CASE c."ClaimValue" WHEN 'properties.view' THEN 'families.view' ELSE 'families.manage' END
                FROM "AspNetRoleClaims" c WHERE c."ClaimValue" IN ('properties.view','properties.manage')
                    AND NOT EXISTS (SELECT 1 FROM "AspNetRoleClaims" f WHERE f."RoleId"=c."RoleId" AND f."ClaimType"=c."ClaimType" AND f."ClaimValue"=CASE c."ClaimValue" WHEN 'properties.view' THEN 'families.view' ELSE 'families.manage' END);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("A reinicialização operacional é irreversível. Restaure o backup aprovado para retornar ao modelo anterior.");
        }
    }
}
