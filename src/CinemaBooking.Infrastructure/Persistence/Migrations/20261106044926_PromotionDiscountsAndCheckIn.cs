using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CinemaBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromotionDiscountsAndCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApplicableSeatType",
                table: "Promotions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DaysOfWeekMask",
                table: "Promotions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DiscountPercent",
                table: "Promotions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FixedPrice",
                table: "Promotions",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "FromTime",
                table: "Promotions",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ToTime",
                table: "Promotions",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "BookingSeats",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PromotionTitle",
                table: "BookingSeats",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckedInBy",
                table: "Bookings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInUtc",
                table: "Bookings",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicableSeatType",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "DaysOfWeekMask",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "FixedPrice",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "FromTime",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "ToTime",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "BookingSeats");

            migrationBuilder.DropColumn(
                name: "PromotionTitle",
                table: "BookingSeats");

            migrationBuilder.DropColumn(
                name: "CheckedInBy",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckedInUtc",
                table: "Bookings");
        }
    }
}
