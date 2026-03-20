using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeaWars.PlayerDataService.Data.Migrations
{
    public partial class ReplaceWorldObjectOwnerEntityId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_world_objects_CreatorUserId",
                table: "world_objects");

            migrationBuilder.RenameColumn(
                name: "CreatorUserId",
                table: "world_objects",
                newName: "OwnerEntityId");

            migrationBuilder.Sql(
                "ALTER TABLE world_objects ALTER COLUMN \"OwnerEntityId\" TYPE character varying(128) USING \"OwnerEntityId\"::text;");

            migrationBuilder.CreateIndex(
                name: "IX_world_objects_OwnerEntityId",
                table: "world_objects",
                column: "OwnerEntityId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_world_objects_OwnerEntityId",
                table: "world_objects");

            migrationBuilder.Sql(
                "ALTER TABLE world_objects ALTER COLUMN \"OwnerEntityId\" TYPE uuid USING \"OwnerEntityId\"::uuid;");

            migrationBuilder.RenameColumn(
                name: "OwnerEntityId",
                table: "world_objects",
                newName: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_world_objects_CreatorUserId",
                table: "world_objects",
                column: "CreatorUserId");
        }
    }
}
