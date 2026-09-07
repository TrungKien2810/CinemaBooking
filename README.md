# CinemaBooking.Smart

Hệ thống đặt vé xem phim với **giữ ghế real-time** bằng SignalR.
ASP.NET Core MVC · .NET 9 · EF Core 9 · SQL Server.

- 📄 Đặc tả đầy đủ: [`General.md`](General.md)

---

## Chạy dự án

**Yêu cầu:** .NET SDK 9.0+, SQL Server LocalDB (kèm Visual Studio) hoặc SQL Server.

```bash
# 1. Khôi phục và build
dotnet build

# 2. Tạo database (lần đầu)
dotnet tool install --global dotnet-ef          # nếu chưa có
dotnet ef migrations add Initial -p src/CinemaBooking.Infrastructure -s src/CinemaBooking.Web
dotnet ef database update           -p src/CinemaBooking.Infrastructure -s src/CinemaBooking.Web

# 3. Chạy web (cửa sổ 1)
dotnet run --project src/CinemaBooking.Web

# 4. Chạy worker (cửa sổ 2)
dotnet run --project src/CinemaBooking.Worker

# 5. Chạy test
dotnet test
```

Chuỗi kết nối nằm ở `ConnectionStrings:Default` trong `src/CinemaBooking.Web/appsettings.json`.
Muốn đổi cho riêng máy mình thì tạo `appsettings.Development.json` — file này đã bị `.gitignore`
bỏ qua, không commit chuỗi kết nối thật.

---

## Cấu trúc

| Dự án                              | Vai trò                                                                        |
| ---------------------------------- | ------------------------------------------------------------------------------ |
| `src/CinemaBooking.Domain`         | Entity, enum, interface — không phụ thuộc hạ tầng                              |
| `src/CinemaBooking.Infrastructure` | `CinemaDbContext`, `InMemorySeatLockManager`, `MockPaymentService`, đăng ký DI |
| `src/CinemaBooking.Web`            | MVC controllers, Razor views, `SeatHub` tại `/hubs/seat`                       |
| `src/CinemaBooking.Worker`         | `ExpiredSeatCleaner`, `EmailOutboxWorker`, `DailyReportWorker`                 |
| `tests/CinemaBooking.Tests`        | xUnit                                                                          |

---

## Ba điều dễ làm sai

1. **Khóa ghế theo cặp `(ShowtimeId, SeatId)`**, không phải theo `SeatId` — cùng một ghế ở hai
   suất chiếu là hai lock độc lập.
2. **`ISeatLockManager` là Singleton** → không được inject `DbContext` (Scoped) vào nó.
   Cần DB thì dùng `IServiceScopeFactory`.
3. **Web không gửi email trực tiếp.** Web ghi vào bảng `OutboxEmails`, Worker mới gửi.

Chi tiết: [`General.md` §7](General.md#7-cài-đặt-kỹ-thuật-cần-lưu-ý).
