using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoFailureReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VideoFailureReason",
                table: "Matches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoFailureReason",
                table: "Matches");
        }
    }
}
