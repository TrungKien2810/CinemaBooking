using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CinemaBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoomNameUniqueAndShowtimeIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rooms_TheaterId",
                table: "Rooms");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Showtimes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_TheaterId_RoomName",
                table: "Rooms",
                columns: new[] { "TheaterId", "RoomName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rooms_TheaterId_RoomName",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Showtimes");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_TheaterId",
                table: "Rooms",
                column: "TheaterId");
        }
    }
}
