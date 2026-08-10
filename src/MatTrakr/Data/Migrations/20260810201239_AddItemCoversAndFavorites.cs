using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatTrakr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemCoversAndFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Lists",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Existing default lists (Movies/Series/Books) start out pinned.
            migrationBuilder.Sql("UPDATE Lists SET IsFavorite = 1 WHERE IsDefault = 1;");

            migrationBuilder.CreateTable(
                name: "ItemCovers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ListItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreateUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemCovers_ListItems_ListItemId",
                        column: x => x.ListItemId,
                        principalTable: "ListItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemCovers_ListItemId",
                table: "ItemCovers",
                column: "ListItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemCovers");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "Lists");
        }
    }
}
