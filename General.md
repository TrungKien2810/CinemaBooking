# 📽️ CinemaBooking.Smart — Hệ thống đặt vé & giữ ghế real-time

> **Bản đã hiệu đính.** Bản gốc lưu tại [`docs/archive/General.original.txt`](docs/archive/General.original.txt).
> Danh sách lỗi đã sửa: [§13 Đính chính](#13-đính-chính-so-với-bản-gốc).

---

## 1. Tổng quan

| | |
|---|---|
| **Tên** | `CinemaBooking.Smart` |
| **Loại** | Web application (ASP.NET Core MVC) có tương tác real-time |
| **Nền tảng** | .NET 9 · EF Core 9 · SQL Server (LocalDB khi dev) · SignalR |
| **Mô hình** | Hệ thống đặt vé của **một rạp** (một thương hiệu) có **nhiều chi nhánh** ở nhiều thành phố (Vincom Bà Triệu, Aeon Tân Phú…): danh mục phim dùng chung, nhưng **mỗi chi nhánh có lịch chiếu và giá vé riêng**; khách tìm một phim và thấy suất chiếu ở mọi chi nhánh đang chiếu phim đó |
| **Điểm nhấn** | Giữ ghế real-time: khi một khách đang chọn ghế, khách khác thấy ghế bị khóa tạm thời (5 phút) |
| **Nhóm** | 3 người · ~8 tuần (xem [SPRINT-PLAN.md](docs/project-state/SPRINT-PLAN.md)) |

**Quyết định phạm vi:** UI dùng **MVC + SignalR JavaScript client**. Blazor được loại khỏi phạm vi
chính (bản gốc để "tùy chọn" gây mơ hồ khi chia việc) — nếu còn thời gian thì làm nhánh thử nghiệm,
xem §12.

---

## 2. Ánh xạ kiến thức theo slide

| Slide | Kỹ thuật | Nơi áp dụng trong dự án |
|-------|----------|--------------------------|
| **1 – Networking** | `IHttpClientFactory` | Gọi API thanh toán giả lập (`MockPaymentService`) |
| **2 – Async & PLINQ** | `async/await`, `Task.WhenAll` | Gửi email theo lô trong Worker; mọi I/O đều async |
| | `AsParallel()` | **Chỉ** trên collection đã ở trong RAM (xem §7.4) |
| **3 – DI** | Constructor injection, Singleton/Scoped | `ISeatLockManager` (Singleton), `IPaymentService`/`IEmailSender` (Scoped) |
| **4 & 5 – MVC** | Model Binding, Validation, Session, ViewModel | Form đặt vé, giỏ ghế tạm trong Session |
| **6 – Blazor** | *(ngoài phạm vi)* | Nhánh thử nghiệm nếu dư thời gian |
| **7 – SignalR** | Hubs, Groups | `SeatHub` — trái tim của dự án |
| **8 – Worker Service** | `BackgroundService`, `PeriodicTimer` | Dọn ghế hết hạn, gửi email từ outbox, báo cáo ngày |

---

## 3. Chức năng

### 3.1 Khách hàng

- Xem danh sách phim đang chiếu (tên, thể loại, thời lượng, poster), tìm kiếm & lọc theo thể loại.
- Tìm theo tên phim → trang chi tiết phim liệt kê suất chiếu ở **mọi chi nhánh** đang chiếu phim đó,
  gom theo ngày → chi nhánh, lọc được theo thành phố và chi nhánh.
- Chọn ghế trên sơ đồ phòng chiếu, chọn nhiều ghế.
- **Giữ ghế** ngay khi click — ghế đổi màu ở mọi máy khác trong cùng suất chiếu, khóa 5 phút.
- Đặt vé (guest checkout hoặc đã đăng nhập), thanh toán giả lập.
- Nhận email xác nhận kèm mã đặt vé.
- Bỏ chọn ghế thủ công; hoặc ghế tự trả về trống sau 5 phút.

### 3.2 Quản trị (vai trò `Admin`)

Một vai trò quản trị duy nhất, quản lý toàn bộ chi nhánh của rạp (§7.7):

- CRUD **phim** — danh mục phim dùng chung cho mọi chi nhánh; soft delete bằng `IsActive`. Cùng một phim
  chiếu ở nhiều chi nhánh chỉ có **một** bản ghi (§5.1 mục 4).
- CRUD **chi nhánh** (tên, thành phố, địa chỉ) và **phòng chiếu** (cấu hình số hàng × số cột → sinh ghế tự động).
- CRUD **suất chiếu** — **xếp lịch cho từng chi nhánh một** (§3.2.1): chọn phim từ danh mục chung
  (không gõ tên phim tự do) + phòng thuộc chi nhánh đó, đặt giá riêng cho suất.
- Quản lý đơn: xem, tìm theo mã, hủy vé.
- Thống kê doanh thu theo ngày / chi nhánh / phim / suất chiếu.

#### 3.2.1 Cái gì dùng chung, cái gì riêng theo chi nhánh

Theo cách các chuỗi rạp vận hành thật: khâu **chọn phim** làm tập trung (đàm phán với nhà phát hành),
còn khâu **xếp suất** làm ở từng rạp — dựa trên số phòng, sức chứa, định dạng phòng và nhu cầu khu vực
của chính rạp đó. Cùng một ngày, hai chi nhánh của cùng thương hiệu có danh sách suất khác hẳn nhau.

| | Dùng chung toàn hệ thống | Riêng theo chi nhánh |
|---|---|---|
| Dữ liệu | Danh mục `Movies` (tên, poster, thời lượng, thể loại) | `Showtimes` (phim nào, phòng nào, giờ nào, bao nhiêu suất) |
| | | `BasePrice` — giá khác nhau theo chi nhánh / khung giờ |
| | | `Rooms`, `Seats` — sơ đồ ghế của riêng chi nhánh |

**Hệ quả cho code:** không có bảng "lịch chiếu chuẩn" rồi nhân bản xuống chi nhánh. `Showtime` gắn
`RoomId`, phòng gắn chi nhánh — lịch **tự nhiên** đã riêng, không cần thêm ràng buộc nào. Nếu sau này
muốn đỡ nhập liệu thì làm chức năng **copy lịch** (nhân bản lịch chi nhánh A → B, hoặc tuần này →
tuần sau) rồi admin sửa tay — đó là tiện ích nhập liệu, không phải ràng buộc dữ liệu (IDEAS #6).

### 3.3 Tác vụ nền (Worker Service)

- `ExpiredSeatCleaner` — mỗi **10 giây**, xóa lock hết hạn và báo ghế trống qua SignalR.
- `EmailOutboxWorker` — mỗi 15 giây, gửi email đang chờ trong bảng `OutboxEmails`, retry tối đa 5 lần.
- `DailyReportWorker` — mỗi giờ, tổng hợp doanh thu ngày hôm trước.

---

## 4. Kiến trúc

### 4.1 Sơ đồ tổng thể

```text
┌──────────────────────────────────────────────┐
│  Web Client (Browser)                        │
│  Razor Views + SignalR JavaScript client     │
└───────────────────┬──────────────────────────┘
                    │ HTTPS / WebSocket
                    ▼
┌──────────────────────────────────────────────┐
│  CinemaBooking.Web (ASP.NET Core MVC)        │
│  Controllers · SeatHub · Identity · Session  │
└───────────────────┬──────────────────────────┘
                    │
        ┌───────────┴────────────┐
        ▼                        ▼
┌──────────────────┐   ┌──────────────────────┐
│ CinemaBooking.   │   │ CinemaBooking.Worker │
│ Infrastructure   │◀──│ (BackgroundService)  │
│ DbContext ·      │   │ Cleaner · Outbox ·   │
│ SeatLockManager  │   │ DailyReport          │
└────────┬─────────┘   └──────────────────────┘
         ▼
┌──────────────────┐
│ SQL Server       │
│ (EF Core 9)      │
└──────────────────┘
```

### 4.2 Cấu trúc solution

```text
CinemaBooking.Smart.sln
├── src/
│   ├── CinemaBooking.Domain/          # Entity, Enum, Interface — không phụ thuộc hạ tầng
│   │   ├── Entities/                  # Movie, Room, Seat, Showtime, Booking, OutboxEmail…
│   │   ├── Enums/
│   │   └── Abstractions/              # ISeatLockManager, IPaymentService, IEmailSender
│   ├── CinemaBooking.Infrastructure/  # EF Core, hiện thực service, DI
│   │   ├── Persistence/CinemaDbContext.cs
│   │   ├── Services/                  # InMemorySeatLockManager, MockPaymentService…
│   │   └── DependencyInjection.cs
│   ├── CinemaBooking.Web/             # MVC + SignalR
│   │   ├── Controllers/  Views/  ViewModels/
│   │   └── Hubs/SeatHub.cs
│   └── CinemaBooking.Worker/          # BackgroundService
│       └── Workers/
└── tests/CinemaBooking.Tests/
```

### 4.3 Thành phần chính

| Thành phần | Vòng đời | Trách nhiệm |
|---|---|---|
| `SeatHub` | *(Hub — theo invocation)* | Nhận `SelectSeat`/`ReleaseSeat`, phát `SeatLocked`/`SeatUnlocked` cho group `showtime-{id}` |
| `ISeatLockManager` | **Singleton** | Nguồn sự thật cho trạng thái "đang giữ", giữ trong `ConcurrentDictionary` |
| `IPaymentService` | Scoped (+ `HttpClient` từ factory) | Thanh toán giả lập |
| `IEmailSender` | Scoped | Gửi email — **chỉ Worker gọi**, Web chỉ ghi vào outbox |
| `BookingController` | — | Luồng đặt vé: validate lại lock → ghi DB → nhả lock → phát SignalR |

---

## 5. Mô hình dữ liệu

EF Core Code-First. Kiểu tiền tệ dùng `decimal(18,2)`, mọi mốc thời gian lưu **UTC**.

| Bảng | Cột chính |
|---|---|
| `Movies` | Id, Title, Description, Genre, **DurationMinutes**, ReleaseDate, PosterUrl, IsActive |
| `Theaters` *(chi nhánh)* | Id, Name *(unique)*, **City**, Address, IsActive |
| `Rooms` | Id, TheaterId, RoomName, TotalRows, TotalColumns |
| `Seats` | Id, RoomId, RowNumber, ColumnNumber, SeatType · *unique (RoomId, Row, Column)* |
| `Showtimes` | Id, MovieId, RoomId, StartTimeUtc, EndTimeUtc, BasePrice |
| `Bookings` | Id, **BookingCode** *(unique)*, UserId?, ShowtimeId, CustomerName/Email/Phone, BookingTimeUtc, TotalAmount, Status |
| `BookingSeats` | Id, BookingId, SeatId, **ShowtimeId**, PriceAtBooking · **unique (ShowtimeId, SeatId)** |
| `Payments` | Id, BookingId, Amount, PaymentMethod, Status, TransactionId, CreatedUtc |
| `OutboxEmails` | Id, ToAddress, Subject, Body, Status, AttemptCount, LastError, CreatedUtc, SentUtc |
| `DailyRevenueReports` | Id, ReportDate, **TheaterId**, MovieId, TicketsSold, Revenue, GeneratedUtc |
| *(Identity)* | `AspNetUsers` / `AspNetRoles` / … — sinh bởi `IdentityDbContext<ApplicationUser>`; `ApplicationUser` thêm FullName |

### 5.1 Bốn ràng buộc quan trọng

1. **Không lưu trạng thái "đang giữ" xuống DB.** Lock sống trong RAM (`SeatLockManager`) —
   ghi/xóa liên tục xuống DB vừa chậm vừa để lại rác khi process chết.
2. **`BookingSeats` có unique index `(ShowtimeId, SeatId)`.** Đây là *chốt chặn cuối*: nếu lock RAM
   sai (nhiều process, bug, restart) thì DB vẫn từ chối đặt trùng. Vì vậy `ShowtimeId` được lặp lại
   xuống `BookingSeats` dù đã có ở `Bookings`.
3. **Không có cột `Theaters.TotalRooms`.** Đó là dữ liệu dẫn xuất — đọc bằng `Rooms.Count`,
   tránh lệch dữ liệu.
4. **Chi nhánh chỉ xuất hiện ở `Rooms.TheaterId`.** Suất chiếu và đơn suy ra chi nhánh qua
   `Room → Theater`, không lặp `TheaterId` xuống `Showtimes`/`Bookings` (cùng lý do bỏ `TotalRooms`).
   Ngoại lệ: `DailyRevenueReports` là bảng tổng hợp nên lưu sẵn. `Movies` **không** gắn chi nhánh —
   cả rạp dùng **một danh mục phim chung**, chi nhánh chọn phim theo `MovieId` khi tạo suất; nhờ vậy
   tìm "Avatar" ra đúng một phim và trang chi tiết gom được suất của mọi chi nhánh.

---

## 6. Luồng nghiệp vụ

### 6.1 Chọn & giữ ghế (real-time)

1. Client mở trang đặt vé của một suất chiếu → controller trả về sơ đồ ghế + **danh sách ghế đã
   đặt** (đọc từ DB, `Status = Confirmed`).
2. SignalR kết nối `/hubs/seat`, gọi `JoinShowtime(showtimeId)` → vào group `showtime-{id}`,
   nhận `SeatSnapshot` là các ghế **đang bị giữ** ngay lúc đó.
3. Người dùng click ghế A5 → `SelectSeat(showtimeId, seatId)`.
4. Hub gọi `SeatLockManager.TryLock(new SeatKey(showtimeId, seatId), connectionId, ttl: 5 phút)`:
   - `Acquired` → gửi `SeatHeldByMe` cho người chọn, `SeatLocked` cho **những người khác trong group**.
   - `HeldByOther` → gửi `SeatRejected` kèm thông báo "Ghế đang được người khác chọn".
5. Bỏ chọn → `ReleaseSeat` → phát `SeatUnlocked` cho cả group.

### 6.2 Xác nhận đặt vé

1. Người dùng nhấn "Đặt vé"; danh sách ghế lấy từ **Session** (server-side, không tin payload client).
2. `BookingController` gọi lại `ValidateLocks(keys, connectionId)` — lock có thể đã hết hạn
   trong lúc người dùng điền form.
3. Trong **một transaction**: tạo `Booking` (`Pending`) + `BookingSeats` + `Payment` + hàng
   `OutboxEmails`. Nếu unique index `(ShowtimeId, SeatId)` nổ → báo lỗi "ghế vừa bị người khác đặt".
4. Gọi `IPaymentService.ChargeAsync` (giả lập). Thành công → `Booking.Status = Confirmed`.
5. `SeatLockManager` nhả lock; phát `SeatBooked` để client khác vẽ lại các ghế đó thành **đã đặt**
   (đỏ, không chọn được).
6. Trả về trang xác nhận. **Email do Worker gửi**, không gửi trong request.

### 6.3 Tự giải phóng ghế hết hạn

`ExpiredSeatCleaner` chạy mỗi 10 giây → `RemoveExpiredLocks(DateTime.UtcNow)` → với mỗi ghế bị xóa,
phát `SeatUnlocked` tới group tương ứng.

---

## 7. Cài đặt kỹ thuật cần lưu ý

### 7.1 Khóa ghế phải theo cặp `(ShowtimeId, SeatId)`

Ghế A5 của phòng 1 ở suất 19:00 và suất 21:00 là **hai lock độc lập**. Khóa theo `seatId` đơn lẻ sẽ
làm suất này chặn nhầm suất kia.

### 7.2 `ConcurrentDictionary` đã atomic — không cần `SemaphoreSlim`

`TryAdd` / `TryUpdate` / `TryRemove` đảm bảo chỉ một luồng thắng. Thêm semaphore cho từng ghế chỉ
làm chậm và tăng nguy cơ deadlock.

### 7.3 `HttpClient` luôn lấy từ `IHttpClientFactory`

`new HttpClient()` trong service Scoped gây cạn socket (TIME_WAIT) và giữ DNS cũ.
Đăng ký: `services.AddHttpClient<IPaymentService, MockPaymentService>(...)`.

### 7.4 PLINQ dùng đúng chỗ

`AsParallel()` **không** hoạt động trên `IQueryable` của EF Core — nó là LINQ-to-Objects.

- ❌ `db.Movies.AsParallel().Where(...)` — hoặc lỗi, hoặc kéo cả bảng về RAM rồi mới lọc.
- ✅ Lọc/nhóm ở DB bằng SQL; chỉ dùng `AsParallel()` cho xử lý CPU-bound trên `List<T>` đã có
  (ví dụ tính điểm gợi ý phim trên tập đã tải).

### 7.5 `OnDisconnectedAsync` và giai đoạn thanh toán

Rớt connection → nhả toàn bộ lock của connection đó. Vì vậy khi người dùng đã sang trang thanh toán,
chỗ ngồi được giữ bằng **bản ghi `Booking` trạng thái `Pending` + unique index**, không phải bằng
lock RAM.

### 7.6 Vòng đời DI

`ISeatLockManager` là **Singleton** nên **không được** inject `DbContext` (Scoped) vào nó —
sẽ ném `InvalidOperationException`. Cần DB thì inject `IServiceScopeFactory` và tự tạo scope
(xem `EmailOutboxWorker`).

### 7.7 Phân quyền khu vực quản trị

Ẩn nút trên giao diện **không** phải là phân quyền: khách gõ thẳng `/Admin/Showtimes/Edit/12` vẫn vào
được nếu controller không kiểm tra.

- Toàn bộ khu quản trị đặt `[Authorize(Roles = "Admin")]` ở **cấp controller**, không rải trên từng action
  — thêm action mới mà quên attribute là lỗ hổng.
- Vai trò lấy từ **tài khoản đang đăng nhập** (server-side), không bao giờ nhận từ form/query string.
- Tạo/sửa suất chiếu vẫn phải kiểm tra `RoomId` (và `MovieId`) có thật và còn `IsActive` — form có thể bị
  sửa để gửi id bất kỳ.
- Khu khách và `SeatHub` đọc dữ liệu của **mọi chi nhánh**, không lọc theo tài khoản — chi nhánh chỉ là
  một tiêu chí lọc do khách chọn, không phải ranh giới bảo mật.

---

## 8. Giao diện

| Trang | Nội dung |
|---|---|
| Trang chủ | Phim nổi bật, tìm kiếm, lọc theo thể loại |
| Chi tiết phim | Thông tin phim + suất chiếu theo ngày, gom theo chi nhánh; lọc theo thành phố và chi nhánh |
| Chọn ghế | Sơ đồ dạng lưới CSS Grid; 4 trạng thái: trống / mình đang giữ / người khác giữ / đã đặt. Đồng hồ đếm ngược 5 phút |
| Thanh toán | Form thông tin khách + tóm tắt ghế, validate bằng Data Annotations + jQuery Unobtrusive |
| Xác nhận | Mã đặt vé, thông báo email đang được gửi |
| Quản trị | CRUD phim, chi nhánh, phòng, suất chiếu; danh sách đơn; dashboard doanh thu (lọc theo chi nhánh) |

---

## 9. Kế hoạch phát triển

3 người · 8 tuần · 4 sprint × 2 tuần. Chi tiết đầu việc, phân công và lịch commit:
**[SPRINT-PLAN.md](docs/project-state/SPRINT-PLAN.md)**.

| Sprint | Tuần | Mục tiêu | Kết quả bàn giao |
|---|---|---|---|
| 1 | 1–2 | Nền tảng | Solution, Domain, EF Core + migration, seed, Identity, CI |
| 2 | 3–4 | Catalog & Admin | Chi nhánh + `City`, CRUD phim/chi nhánh/phòng/suất chiếu, chi tiết phim gom suất nhiều chi nhánh |
| 3 | 5–6 | Real-time | `SeatHub`, `SeatLockManager`, sơ đồ ghế live |
| 4 | 7–8 | Đặt vé & Worker | Booking + thanh toán, 3 worker, thống kê, test & tài liệu |

---

## 10. Rủi ro & cách xử lý

| Rủi ro | Cách xử lý |
|---|---|
| Nhiều người click cùng một ghế | `ConcurrentDictionary.TryAdd` (atomic) + unique index ở DB làm chốt chặn cuối |
| Client mất kết nối giữa chừng | `OnDisconnectedAsync` nhả lock; giai đoạn thanh toán dựa vào `Booking` `Pending` |
| Khách gõ thẳng URL khu quản trị | `[Authorize(Roles = "Admin")]` ở cấp controller + kiểm tra id gửi lên từ form (§7.7) |
| Ghế bị treo sau khi hết hạn | `ExpiredSeatCleaner` mỗi 10 giây + `TryLock` tự cướp lock đã quá hạn |
| Ghế đã đặt không hiển thị khi tải trang | Controller trả kèm danh sách `Confirmed` từ DB, không dựa vào SignalR |
| Email mất khi web process chết | Bảng `OutboxEmails` + Worker gửi, retry 5 lần — không fire-and-forget |
| Lock RAM sai khi chạy nhiều instance | Chấp nhận giới hạn 1 instance trong phạm vi môn học; đường nâng cấp là Redis (§12) |
| Thanh toán | HTTPS bắt buộc, token giả lập, timeout 10s cho request thanh toán |

---

## 11. Definition of Done

Một task chỉ được đóng khi: build không warning · có unit test cho logic nghiệp vụ mới ·
đã tự kiểm thử trên trình duyệt · đã được 1 người khác review PR · migration (nếu có) chạy được
trên máy sạch · đã cập nhật tài liệu liên quan.

---

## 12. Mở rộng nếu còn thời gian

- **Redis** cho `SeatLockManager` → chạy được nhiều instance web.
- **Blazor Server** cho trang chọn ghế, thay JavaScript.
- **JWT API** cho ứng dụng di động.
- **gRPC** giữa Worker và Web thay cho SignalR client.

---

## 13. Đính chính so với bản gốc

| # | Vấn đề trong bản gốc | Đã sửa thành |
|---|---|---|
| 1 | Tên file là `General.txt`; khối code SQL ở §5.1 không đóng nên toàn bộ từ §5.2 trở đi bị nuốt vào code block (mất hết bảng markdown); sơ đồ §4.1 vỡ và còn sót chữ `text` lạc | Đổi sang `General.md`, đóng đúng fence, sơ đồ nằm trong khối `text` |
| 2 | §6.3 nói Worker chạy **mỗi phút**, §10 nói **mỗi 10 giây** | Thống nhất **10 giây**, cấu hình qua `SeatHold:CleanupIntervalSeconds` |
| 3 | Lock mô tả là "danh sách ghế đang bị khóa" — tức khóa theo `seatId` đơn lẻ | Khóa theo cặp `(ShowtimeId, SeatId)` — kiểu `SeatKey` |
| 4 | Không có gì chặn đặt trùng ở tầng DB; `BookingSeats` không có `ShowtimeId` | Thêm `ShowtimeId` + **unique index `(ShowtimeId, SeatId)`** |
| 5 | Bảng `Users` tự dựng cột `PasswordHash`, `Role` nhưng chú thích lại ghi "ASP.NET Identity" | Dùng `IdentityDbContext<ApplicationUser>`; vai trò qua `IdentityRole` |
| 6 | §10 đề xuất `SemaphoreSlim` cho từng ghế | Bỏ — `ConcurrentDictionary` đã atomic sẵn |
| 7 | §7 và §8 dùng `AsParallel()` để lọc phim/lịch chiếu "khi truy vấn" | Lọc ở SQL; PLINQ chỉ cho collection đã ở RAM (§7.4) |
| 8 | §6.2 gửi email fire-and-forget, mâu thuẫn với §3.3 giao email cho Worker | Bảng `OutboxEmails`: Web ghi — Worker gửi |
| 9 | Dùng `HttpClient` trực tiếp | `IHttpClientFactory` qua `AddHttpClient<TInterface, TImpl>` |
| 10 | Blazor "tùy chọn" ở §2 nhưng lại là gợi ý chính ở §8 | Chốt MVC + SignalR JS; Blazor chuyển sang §12 |
| 11 | `Movies.Duration` không rõ đơn vị; `Theaters.TotalRooms` là dữ liệu dẫn xuất; thiếu precision cho cột tiền | `DurationMinutes`; bỏ `TotalRooms`; `decimal(18,2)` |
| 12 | Admin "xóa phim" mâu thuẫn với cột `IsActive` | Soft delete bằng `IsActive`, không xóa cứng |
| 13 | Không nêu việc `OnDisconnectedAsync` phá vỡ luồng thanh toán | Nêu rõ ở §7.5 |
| 14 | Singleton `SeatLockManager` không cảnh báo chuyện không được inject `DbContext` | §7.6 |
| 15 | Kế hoạch 6 sprint = 29 ngày, tính cho 1 người — không khớp nhóm 3 người / 2 tháng | 4 sprint × 2 tuần cho 3 người, xem `SPRINT-PLAN.md` |
| 16 | Thiếu Definition of Done và quy ước thời gian | §11; toàn bộ mốc thời gian lưu UTC |
