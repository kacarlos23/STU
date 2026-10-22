using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STU.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserAccountSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppearancePreference",
                table: "AspNetUsers",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "light");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordResetRequestedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProfilePhoto",
                table: "AspNetUsers",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoVersion",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppearancePreference",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PasswordResetRequestedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProfilePhoto",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProfilePhotoVersion",
                table: "AspNetUsers");
        }
    }
}
