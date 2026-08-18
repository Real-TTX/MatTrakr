using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatTrakr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Lists",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Seed a stable initial order per owner matching the previous Type→Name sort.
            migrationBuilder.Sql(@"
                UPDATE Lists
                SET SortOrder = (
                    SELECT COUNT(*) FROM Lists l2
                    WHERE l2.OwnerUserId = Lists.OwnerUserId
                      AND (l2.Type < Lists.Type
                           OR (l2.Type = Lists.Type AND l2.Name < Lists.Name))
                );");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Lists");
        }
    }
}
