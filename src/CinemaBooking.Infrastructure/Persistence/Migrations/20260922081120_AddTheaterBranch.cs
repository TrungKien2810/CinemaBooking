using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CinemaBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTheaterBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Theaters",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Theaters",
                type: "bit",
                nullable: false,
                // Chi nhánh có sẵn trước migration này đang hoạt động — mặc định false sẽ ẩn hết.
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Theaters_City",
                table: "Theaters",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_Theaters_Name",
                table: "Theaters",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Theaters_City",
                table: "Theaters");

            migrationBuilder.DropIndex(
                name: "IX_Theaters_Name",
                table: "Theaters");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Theaters");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Theaters");
        }
    }
}
