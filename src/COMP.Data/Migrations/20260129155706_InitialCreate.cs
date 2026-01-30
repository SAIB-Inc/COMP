using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comp.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "ReducerStates",
                schema: "public",
                columns: table => new
                {
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LatestIntersectionsJson = table.Column<string>(type: "text", nullable: false),
                    StartIntersectionJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReducerStates", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "SyncState",
                schema: "public",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncState", x => x.Hash);
                });

            migrationBuilder.CreateTable(
                name: "TokenMetadata",
                schema: "public",
                columns: table => new
                {
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Ticker = table.Column<string>(type: "text", nullable: false),
                    PolicyId = table.Column<string>(type: "text", nullable: false),
                    Decimals = table.Column<int>(type: "integer", nullable: false),
                    Policy = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "text", nullable: true),
                    Logo = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenMetadata", x => x.Subject);
                });

            migrationBuilder.CreateTable(
                name: "TokenMetadataOnChain",
                schema: "public",
                columns: table => new
                {
                    Subject = table.Column<string>(type: "text", nullable: false),
                    PolicyId = table.Column<string>(type: "text", nullable: false),
                    AssetName = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Logo = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false),
                    Decimals = table.Column<int>(type: "integer", nullable: false),
                    TokenType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenMetadataOnChain", x => x.Subject);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TokenMetadata_Name_Description_Ticker",
                schema: "public",
                table: "TokenMetadata",
                columns: new[] { "Name", "Description", "Ticker" });

            migrationBuilder.CreateIndex(
                name: "IX_TokenMetadataOnChain_PolicyId",
                schema: "public",
                table: "TokenMetadataOnChain",
                column: "PolicyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReducerStates",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SyncState",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TokenMetadata",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TokenMetadataOnChain",
                schema: "public");
        }
    }
}
