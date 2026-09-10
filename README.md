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

# 2. Chạy web (cửa sổ 1) — tự áp migration và seed dữ liệu mẫu ở lần chạy đầu
dotnet run --project src/CinemaBooking.Web

# 3. Chạy worker (cửa sổ 2)
dotnet run --project src/CinemaBooking.Worker

# 4. Chạy test
dotnet test
```

Không cần chạy `dotnet ef database update` bằng tay: web gọi `Database.MigrateAsync()` rồi
`CatalogSeeder` lúc khởi động, nên `git pull` + `dotnet run` là đủ để có schema và dữ liệu
giống cả nhóm.

Chuỗi kết nối nằm ở `ConnectionStrings:Default` trong `src/CinemaBooking.Web/appsettings.json`.
Muốn đổi cho riêng máy mình thì tạo `appsettings.Development.json` — file này đã bị `.gitignore`
bỏ qua, không commit chuỗi kết nối thật.

### Ba dev đồng bộ database thế nào

**Mỗi người một database riêng trên máy mình, không dùng chung một DB.** Dùng chung sẽ hỏng
đúng Sprint 3: ba người cùng giữ ghế trên một suất chiếu thì không ai phân biệt được bug thật
với người bên cạnh đang click, và không ai chạy được migration khi hai người kia đang mở kết nối.

Thứ được đồng bộ là **schema và dữ liệu mẫu**, đi qua git:

| Cần giống nhau | Đi qua đâu |
| --- | --- |
| Schema | File migration trong `src/CinemaBooking.Infrastructure/Persistence/Migrations/` |
| Dữ liệu mẫu | `CatalogSeeder` (phim, rạp, phòng, ghế, suất chiếu) và `IdentitySeeder` (vai trò, tài khoản) |

**Chỉ Dev A chạy `dotnet ef migrations add`.** Hai người tạo migration song song trên cùng một
gốc sẽ làm `CinemaDbContextModelSnapshot.cs` xung đột và rất mất công gỡ. Ai cần đổi schema thì
báo Dev A.

Làm lại database từ đầu khi dữ liệu bẩn:

```bash
dotnet ef database drop -f -p src/CinemaBooking.Infrastructure -s src/CinemaBooking.Web
dotnet run --project src/CinemaBooking.Web        # tự tạo lại và seed
```

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

---

## Quyết định về schema

- **Unique index `BookingSeat(ShowtimeId, SeatId)`** là chốt chặn cuối chống đặt trùng ghế. Lock
  trong RAM có thể sai sau khi process restart; DB thì không. `ShowtimeId` cố ý lặp lại từ
  `Booking` xuống `BookingSeat` chỉ để đặt được index này.
- **Unique index `Seat(RoomId, RowNumber, ColumnNumber)`** chặn sinh trùng ghế khi admin đổi
  layout phòng.
- **`Showtime.EndTimeUtc` lưu lại** thay vì tính từ `Movie.DurationMinutes` mỗi lần đọc, để câu
  truy vấn phát hiện trùng lịch chiếu (A2.8) chạy được hoàn toàn trong SQL.
- **`Theater.TotalRooms` không lưu** — là dữ liệu dẫn xuất, đọc bằng `Rooms.Count`.
- **Không cascade delete từ `Seat` xuống `BookingSeat`** (`DeleteBehavior.Restrict`): SQL Server
  từ chối nhiều đường cascade cùng trỏ tới một bảng.
- **Ghế sinh bằng `SeatGenerator`, không nhập tay.** Hàng cuối là ghế đôi, phần ba giữa phòng là
  ghế VIP, còn lại ghế thường — cùng một phòng luôn cho ra cùng một sơ đồ.
- **Test unique index chạy trên SQLite in-memory, không phải InMemory provider.** InMemory
  provider không áp unique index, test sẽ pass giả.

Chi tiết: [`General.md` §7](General.md#7-cài-đặt-kỹ-thuật-cần-lưu-ý).
