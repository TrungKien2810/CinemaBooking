using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CinemaBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyRevenueReportTheater : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Báo cáo là dữ liệu dẫn xuất, Worker tổng hợp lại được. Dòng cũ không biết thuộc chi nhánh
            // nào — giữ lại thì TheaterId = 0 vi phạm FK ngay khi thêm cột.
            migrationBuilder.Sql("DELETE FROM DailyRevenueReports");

            migrationBuilder.AddColumn<int>(
                name: "TheaterId",
                table: "DailyRevenueReports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_DailyRevenueReports_ReportDate_TheaterId_MovieId",
                table: "DailyRevenueReports",
                columns: new[] { "ReportDate", "TheaterId", "MovieId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyRevenueReports_TheaterId",
                table: "DailyRevenueReports",
                column: "TheaterId");

            migrationBuilder.AddForeignKey(
                name: "FK_DailyRevenueReports_Theaters_TheaterId",
                table: "DailyRevenueReports",
                column: "TheaterId",
                principalTable: "Theaters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyRevenueReports_Theaters_TheaterId",
                table: "DailyRevenueReports");

            migrationBuilder.DropIndex(
                name: "IX_DailyRevenueReports_ReportDate_TheaterId_MovieId",
                table: "DailyRevenueReports");

            migrationBuilder.DropIndex(
                name: "IX_DailyRevenueReports_TheaterId",
                table: "DailyRevenueReports");

            migrationBuilder.DropColumn(
                name: "TheaterId",
                table: "DailyRevenueReports");
        }
    }
}
