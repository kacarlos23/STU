using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActiveMicroregionIdentifiers : Migration
    {
        private static readonly string[] CodeIndexColumns = ["HealthUnitId", "Code"];
        private static readonly string[] NameIndexColumns = ["HealthUnitId", "NormalizedName"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_microregions_HealthUnitId_Code",
                table: "microregions");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "microregions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "microregions"
                SET "NormalizedName" = upper(btrim("Name"));
                """);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "microregions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_microregions_HealthUnitId_Code",
                table: "microregions",
                columns: CodeIndexColumns,
                unique: true,
                filter: "\"ArchivedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_microregions_HealthUnitId_NormalizedName",
                table: "microregions",
                columns: NameIndexColumns,
                unique: true,
                filter: "\"ArchivedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_microregions_HealthUnitId_Code",
                table: "microregions");

            migrationBuilder.DropIndex(
                name: "IX_microregions_HealthUnitId_NormalizedName",
                table: "microregions");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "microregions");

            migrationBuilder.CreateIndex(
                name: "IX_microregions_HealthUnitId_Code",
                table: "microregions",
                columns: CodeIndexColumns,
                unique: true);
        }
    }
}
