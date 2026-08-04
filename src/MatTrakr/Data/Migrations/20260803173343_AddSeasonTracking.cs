using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatTrakr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TotalSeasons",
                table: "ListItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WatchedSeasons",
                table: "ListItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalSeasons",
                table: "ListItems");

            migrationBuilder.DropColumn(
                name: "WatchedSeasons",
                table: "ListItems");
        }
    }
}
