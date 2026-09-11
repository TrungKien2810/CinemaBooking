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

> **Nhánh `feat/s1-c-ui-auth-foundation`:** file migration do Dev A sở hữu và còn nằm ở nhánh
> `feat/s1-a-data-migration-seed`, chưa merge vào `dev`. Muốn chạy thử nhánh này thì tự sinh
> migration ở máy mình bằng lệnh ở bước 2; sau khi nhánh của A merge vào `dev` thì bỏ bước đó,
> web tự áp migration lúc khởi động. Trang chủ đọc phim thẳng từ DB nên khi chưa có
> `CatalogSeeder` của A, nó hiện đúng trạng thái "Chưa có phim nào".

Web **tự tạo vai trò và tài khoản admin** lúc khởi động (`IdentitySeeder`). Nếu database chưa có
migration nào được áp, ứng dụng dừng ngay với thông báo chỉ thẳng câu lệnh cần chạy, thay vì để
EF ném `Invalid object name 'AspNetRoles'` khó đoán.

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

## Tài khoản và vai trò

`IdentitySeeder` chạy lúc khởi động web, tạo hai vai trò `Admin` / `Customer` và một tài khoản
quản trị nếu chưa có. Chạy lại bao nhiêu lần cũng được — mỗi bước đều kiểm tra tồn tại trước.

| | Giá trị |
| --- | --- |
| Email admin | `admin@cinemabooking.local` (đổi ở `SeedAdmin:Email`) |
| Mật khẩu admin | `Admin@12345` — **mặc định trong mã nguồn, chỉ để demo** |
| Người đăng ký qua form | tự động vào vai trò `Customer` |

Mật khẩu admin **không** nằm trong `appsettings.json`. Muốn đổi thì đặt ở
`appsettings.Development.json` (đã bị `.gitignore` bỏ qua) hoặc user-secrets:

```bash
dotnet user-secrets set "SeedAdmin:Password" "MatKhauCuaBan" --project src/CinemaBooking.Web
```

Khi còn dùng mật khẩu mặc định, log khởi động sẽ in một dòng `warn` nhắc việc này.

Dùng hằng số `AppRoles.Admin` / `AppRoles.Customer` trong `[Authorize(Roles = ...)]`, đừng gõ
chuỗi tay: gõ sai thì build vẫn xanh, lỗi chỉ lộ lúc chạy dưới dạng "bị chặn truy cập".

---

## Giao diện

- Toàn bộ token màu, khoảng cách, bo góc khai báo một lần ở `:root` trong
  `src/CinemaBooking.Web/wwwroot/css/site.css`, rồi nối vào biến Bootstrap 5.3
  (`--bs-body-bg`, `--bs-link-color`…) để component có sẵn ăn theme. **View chỉ dùng `var()`,
  không hardcode mã màu** — Sprint 3 sẽ thêm token trạng thái ghế vào đúng khối đó.
- Partial dùng lại: `_Layout` (navbar + footer), `_LoginPartial` (khu tài khoản trên navbar),
  `_MovieCard` (thẻ phim — Sprint 2 dùng tiếp cho tìm kiếm và lọc thể loại).
- Poster có hai lớp dự phòng: phim chưa có `PosterUrl` thì view model trả sẵn
  `/img/poster-placeholder.svg`; poster có đường dẫn nhưng tải hỏng thì `site.js` đổi `src`.
  Vì vậy **thiếu file poster không làm vỡ layout**, cứ thêm ảnh thật vào `wwwroot/img/posters/`
  theo đúng đường dẫn `CatalogSeeder` khai báo là hiện ra.
- Lưới phim dùng số cột cố định theo breakpoint (2 → 3 → 4 → 5), không dùng `auto-fill minmax`:
  poster tỉ lệ 2:3 mà để tự chia thì màn rộng ra 7-8 cột poster gầy nhẳng.

---

## Ba điều dễ làm sai

1. **Khóa ghế theo cặp `(ShowtimeId, SeatId)`**, không phải theo `SeatId` — cùng một ghế ở hai
   suất chiếu là hai lock độc lập.
2. **`ISeatLockManager` là Singleton** → không được inject `DbContext` (Scoped) vào nó.
   Cần DB thì dùng `IServiceScopeFactory`.
3. **Web không gửi email trực tiếp.** Web ghi vào bảng `OutboxEmails`, Worker mới gửi.

Chi tiết: [`General.md` §7](General.md#7-cài-đặt-kỹ-thuật-cần-lưu-ý).
