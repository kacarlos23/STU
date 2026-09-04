using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Format = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceFileName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    StagedFileName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ResultFileName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ErrorSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordCount = table.Column<int>(type: "integer", nullable: false),
                    ValidationErrorCount = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ProgressPercentage = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_jobs", x => x.Id);
                    table.CheckConstraint("CK_operation_jobs_Attempts", "\"AttemptCount\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_operation_jobs_Progress", "\"ProgressPercentage\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_operation_jobs_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_operation_jobs_health_units_HealthUnitId",
                        column: x => x.HealthUnitId,
                        principalTable: "health_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HealthUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Link = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_notifications_health_units_HealthUnitId",
                        column: x => x.HealthUnitId,
                        principalTable: "health_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operation_jobs_CreatedByUserId",
                table: "operation_jobs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_operation_jobs_HealthUnitId_CreatedAtUtc",
                table: "operation_jobs",
                columns: new[] { "HealthUnitId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_jobs_Status_CreatedAtUtc",
                table: "operation_jobs",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_HealthUnitId",
                table: "user_notifications",
                column: "HealthUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_UserId_ReadAtUtc_CreatedAtUtc",
                table: "user_notifications",
                columns: new[] { "UserId", "ReadAtUtc", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_jobs");

            migrationBuilder.DropTable(
                name: "user_notifications");
        }
    }
}
