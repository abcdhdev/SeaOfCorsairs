using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeaWars.PlayerDataService.Data.Migrations
{
    public partial class AddWorldObjects : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "world_objects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_world_objects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_world_objects_CreatorUserId",
                table: "world_objects",
                column: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_world_objects_ObjectType",
                table: "world_objects",
                column: "ObjectType");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "world_objects");
        }
    }
}
