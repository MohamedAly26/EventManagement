using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventManagement.Migrations
{
    /// <inheritdoc />
    public partial class Comments_AddFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Comments_EventId_CreatedAt",
                table: "Comments");

            migrationBuilder.AddColumn<bool>(
                name: "FromAdmin",
                table: "Comments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Comments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Comments_EventId",
                table: "Comments",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Comments_EventId",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "FromAdmin",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Comments");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_EventId_CreatedAt",
                table: "Comments",
                columns: new[] { "EventId", "CreatedAt" });
        }
    }
}
