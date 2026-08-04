using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatTrakr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCardStatusPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardStatusPosition",
                table: "Users",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Above");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardStatusPosition",
                table: "Users");
        }
    }
}
