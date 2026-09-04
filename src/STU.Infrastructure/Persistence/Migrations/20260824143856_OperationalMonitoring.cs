using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_heartbeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Component = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Instance = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_heartbeats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_heartbeats_Component",
                table: "system_heartbeats",
                column: "Component",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_heartbeats_LastSeenAtUtc",
                table: "system_heartbeats",
                column: "LastSeenAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_heartbeats");
        }
    }
}
