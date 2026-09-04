using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackupOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "backup_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduleKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FileName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FilePrunedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_runs", x => x.Id);
                    table.CheckConstraint("CK_backup_runs_Attempts", "\"AttemptCount\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_backup_runs_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "backup_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DayOfWeek = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LocalHour = table.Column<int>(type: "integer", nullable: false),
                    RetentionCount = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_settings", x => x.Id);
                    table.CheckConstraint("CK_backup_settings_LocalHour", "\"LocalHour\" BETWEEN 0 AND 23");
                    table.CheckConstraint("CK_backup_settings_RetentionCount", "\"RetentionCount\" BETWEEN 1 AND 52");
                });

            migrationBuilder.CreateIndex(
                name: "IX_backup_runs_RequestedByUserId",
                table: "backup_runs",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_backup_runs_ScheduleKey",
                table: "backup_runs",
                column: "ScheduleKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_backup_runs_Status_RequestedAtUtc",
                table: "backup_runs",
                columns: ["Status", "RequestedAtUtc"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backup_runs");

            migrationBuilder.DropTable(
                name: "backup_settings");
        }
    }
}
