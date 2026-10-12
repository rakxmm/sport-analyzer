using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "VideoStorageKey",
                table: "Matches",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadId",
                table: "Matches",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UploadPartSizeBytes",
                table: "Matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoContentType",
                table: "Matches",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoFileName",
                table: "Matches",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VideoSizeBytes",
                table: "Matches",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UploadId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "UploadPartSizeBytes",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "VideoContentType",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "VideoFileName",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "VideoSizeBytes",
                table: "Matches");

            migrationBuilder.AlterColumn<string>(
                name: "VideoStorageKey",
                table: "Matches",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }
    }
}
