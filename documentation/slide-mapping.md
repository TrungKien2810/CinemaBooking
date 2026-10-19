# Ánh xạ slide bài giảng → mã nguồn — CinemaBooking.Smart

> Dev A · Sprint 4 (A4.9) · đối chiếu [`General.md`](../General.md) §2 "Ánh xạ kiến thức theo slide".
>
> Mục đích: mỗi slide kỹ thuật của môn phải chỉ vào **một chỗ cụ thể trong mã nguồn** và **một cách
> chứng minh trong khoảng hai phút**. Cột "Bằng chứng" ghi file, route hoặc test để người chấm mở đúng
> chỗ; cột "Chứng minh" là việc làm tại chỗ khi bảo vệ, không phải đọc lại code.

## 1. Bảng tổng

| Slide | Kỹ thuật | Áp dụng vào chức năng nào | Bằng chứng trong mã nguồn | Chứng minh khi bảo vệ |
| --- | --- | --- | --- | --- |
| **1 – Networking** | `IHttpClientFactory`, `HttpClient` được tiêm | Gọi cổng thanh toán giả lập khi chốt đơn | `src/CinemaBooking.Infrastructure/DependencyInjection.cs` (`AddHttpClient<IPaymentService, MockPaymentService>`), `Services/MockPaymentService.cs`, `Payment:BaseUrl` trong `appsettings.json` | Chỉ vào chỗ `HttpClient` **được tiêm** (không có `new HttpClient()` trong repo); chốt đơn và xem log request |
| **2 – Async & PLINQ** | `async/await`, `CancellationToken`, `Task.WhenAll` | Mọi I/O; gửi email theo lô trong Worker | `src/CinemaBooking.Worker/Workers/EmailOutboxWorker.cs` (`Task.WhenAll` + mỗi email tự bắt lỗi), mọi action controller đều nhận `CancellationToken` | Mở song song một action MVC và `EmailOutboxWorker` |
| | `AsParallel()` | **Chủ ý không** dùng trên `IQueryable` | [`adr/0001-catalog-filtering-plinq-vs-sql.md`](adr/0001-catalog-filtering-plinq-vs-sql.md), comment ở `MovieCatalogService.cs` và `DailyReportWorker.cs` | Giải thích quyết định + ngưỡng dùng PLINQ (xem §2.2) |
| **3 – DI** | Constructor injection, Singleton / Scoped / Transient | Lock ghế trong RAM (Singleton), `DbContext` – thanh toán – email (Scoped), `SeatHub` (Transient) | `DependencyInjection.cs`, [`technical-design/di-lifetimes.md`](technical-design/di-lifetimes.md), `tests/CinemaBooking.Tests/SeatLockManagerTests.cs` | Chỉ bảng vòng đời; nói về captive dependency và vì sao `ValidateScopes` ở Development bắt được |
| **4 & 5 – MVC** | Model binding + DataAnnotations, `ModelState` | Form admin (phim, chi nhánh, phòng, suất chiếu), form đăng ký | `Areas/Admin/Models/*FormModel.cs`, `Areas/Admin/Controllers/*Controller.cs`, `tests/.../RegisterViewModelTests.cs`, `tests/.../AdminCatalogRulesTests.cs` | Gửi form thiếu tên phim → lỗi tiếng Việt tại chỗ; nhập giá vé ngoài khoảng → bị chặn |
| | Session | Giỏ ghế giữa trang chọn ghế và trang thanh toán | `src/CinemaBooking.Web/Services/SeatSelection.cs`, `AddSession` trong `Program.cs` | Chọn ghế → sang trang xác nhận: danh sách ghế đến từ **Session**, client không gửi lại |
| | ViewModel | View model riêng cho sơ đồ ghế, trang xác nhận, dashboard doanh thu | `Models/SeatMapViewModels.cs`, `Areas/Admin/Models/RevenueModels.cs`, `tests/.../SeatMapViewModelTests.cs` | Chạy test view model: dựng sơ đồ ghế không cần HTTP |
| | Middleware & logging | Bắt lỗi một chỗ, mỗi request một log scope có trace | `src/CinemaBooking.Web/Middleware/ExceptionHandlingMiddleware.cs`, `Middleware/RequestLogScopeMiddleware.cs`, `Infrastructure/Logging/LoggingConfiguration.cs`, `tests/.../ExceptionHandlingMiddlewareTests.cs` | Mở `/Booking/SeatMap/999999` → trang lỗi có `traceId` và dòng log tương ứng |
| **6 – Blazor** | *(ngoài phạm vi chính)* | — | `General.md` §1 "Quyết định phạm vi", `docs/project-state/IDEAS.md` | Nói rõ vì sao loại: MVC + SignalR JS khớp 8 tuần / 3 người; Blazor để ở nhánh thử nghiệm |
| **7 – SignalR** | Hubs, Groups, broadcast | Giữ ghế real-time — trái tim của đề tài | `src/CinemaBooking.Web/Hubs/SeatHub.cs` (`GroupName`, `JoinShowtime`, `SelectSeat`, `ReleaseSeat`, `OnDisconnectedAsync`), `MapHub<SeatHub>("/hubs/seat")`, `Infrastructure/Services/InMemorySeatLockManager.cs` | Mở hai cửa sổ cùng suất: click ghế, cửa sổ kia đổi màu trong dưới 1 giây |
| **8 – Worker Service** | `BackgroundService`, `PeriodicTimer`, `IServiceScopeFactory` | Dọn ghế hết hạn, gửi email trong outbox, tổng hợp báo cáo ngày | `src/CinemaBooking.Worker/Workers/{ExpiredSeatCleaner,EmailOutboxWorker,DailyReportWorker}.cs`, `src/CinemaBooking.Worker/Program.cs` | Chạy Worker và Web cạnh nhau; đặt vé → log Worker in nội dung email xác nhận |

## 2. Chi tiết từng slide

### 2.1 Slide 1 — Networking (`IHttpClientFactory`)

Thanh toán gọi ra ngoài qua `HttpClient` **được tiêm**, đăng ký bằng
`AddHttpClient<IPaymentService, MockPaymentService>`; địa chỉ cổng giả lập nằm ở `Payment:BaseUrl`.
Cách viết này giữ được `HttpMessageHandler` pool dùng lại (không cạn socket) và cho phép test thay
handler bằng `HttpMessageHandler` giả.

*Câu hỏi dễ gặp:* "Sao không `new HttpClient()`?" → mỗi lần `new` là một connection pool mới, hết
socket dưới tải; `IHttpClientFactory` tái dùng handler và gom cấu hình về một chỗ.

### 2.2 Slide 2 — Async/await và PLINQ

**Async:** mọi thao tác I/O là `async` và nhận `CancellationToken` (controller → service → EF Core),
nên khi khách đóng tab thì truy vấn cũng dừng. Chỗ dùng song song thật là gửi email theo lô trong
`EmailOutboxWorker`: `Task.WhenAll` trên batch 20 email, **mỗi email tự bắt lỗi** vì `Task.WhenAll`
chỉ ném exception đầu tiên — một email hỏng không được kéo cả batch chết.

**PLINQ:** đề tài cố ý **không** dùng `AsParallel()` trong đường đi truy vấn. `AsParallel()` là
LINQ-to-Objects, gọi trên `IQueryable` thì hoặc không dịch được, hoặc âm thầm kéo cả bảng về RAM rồi
mới lọc — song song hoá đúng phần rẻ nhất. Mọi lọc/sắp xếp/phân trang của catalog nằm trong
`IQueryable` và ra **một** câu SQL (xem [ADR 0001](adr/0001-catalog-filtering-plinq-vs-sql.md)).

Chỗ PLINQ thật sự có lãi trong đề tài và ngưỡng áp dụng đã ghi trong ADR:
**xử lý CPU-bound trên `List<T>` đã nằm trong RAM**, không có I/O ở giữa (ví dụ tổng hợp báo cáo ngày
theo phim/chi nhánh nếu số dòng lớn hơn ngưỡng vài nghìn). Dưới ngưỡng đó, chi phí chia partition còn
lớn hơn phần tiết kiệm — nên bản hiện tại lọc bằng SQL và không có `AsParallel()` trong mã.

*Câu hỏi dễ gặp:* "Vậy có áp dụng PLINQ không?" → Có, ở đúng chỗ: CPU-bound trên dữ liệu trong RAM;
còn truy vấn thì để SQL làm, và ADR ghi rõ lý do cùng ngưỡng chuyển đổi.

### 2.3 Slide 3 — Dependency Injection

- `ISeatLockManager` → `InMemorySeatLockManager` **Singleton**: bảng lock phải dùng chung toàn process;
  nếu Scoped thì mỗi request một bảng lock và không ai thấy ghế của ai.
- `CinemaDbContext`, `IPaymentService`, `IEmailSender` **Scoped**: một request = một unit of work.
- `SeatHub` **Transient**: SignalR tạo mới cho mỗi lần gọi hub method.
- `TimeProvider` Singleton qua `TryAddSingleton(TimeProvider.System)`: chỗ cần "bây giờ" đều lấy từ đây
  nên test bơm được đồng hồ giả.

Bẫy đã gặp và ghi lại: Singleton **không được** nhận Scoped. Đúng khuôn khi Singleton cần DB là inject
`IServiceScopeFactory` rồi tự mở scope ngắn — cả ba worker đều viết như vậy.

*Câu hỏi dễ gặp:* "Chạy nhiều instance web thì sao?" → Lock RAM chỉ đúng trong một process; chốt chặn
thật là unique index `BookingSeat(ShowtimeId, SeatId)` trong DB, và `ISeatLockManager` tách interface
sẵn để đổi sang Redis khi cần (§5 của `di-lifetimes.md`).

### 2.4 Slide 4 & 5 — MVC

- **Model binding + validation:** form admin dùng form model riêng với DataAnnotations
  (`[Required]`, `[StringLength]`, `[Range]`) và mọi action ghi dữ liệu đều kiểm `ModelState.IsValid`
  trước khi chạm DB. Thông báo lỗi viết bằng tiếng Việt, có test riêng
  (`RegisterViewModelTests`, `AdminCatalogRulesTests`).
- **Kiểm tra nghiệp vụ đặt ở server, không ở view:** suất chiếu trùng giờ, buffer 15 phút dọn phòng,
  phòng của chi nhánh đã đóng, phim đã ẩn — đều chặn ở controller và có test. Form bị sửa tay cũng
  không lọt.
- **Session:** giỏ ghế giữa hai trang nằm ở Session server-side, chỉ chứa `showtimeId + seatIds +
  connectionId`; bước ghi đơn **kiểm tra lại** lock nên client sửa Session/URL cũng không đặt được ghế
  người khác.
- **ViewModel:** mỗi màn hình một view model (`SeatMapViewModel`, `CheckoutViewModel`,
  `RevenueDashboardViewModel`…); entity không bao giờ đi thẳng ra view.
- **Routing:** khu quản trị map bằng area route `{area:exists}/{controller=Movies}/{action=Index}/{id?}`
  trước route mặc định, nên `/Admin/Bookings` không đụng `/Booking/SeatMap`.
- **Middleware xử lý lỗi** đặt đầu pipeline (middleware chỉ bắt được exception của những middleware
  nằm sau nó): lỗi nghiệp vụ → 400, lỗi lạ → 500 có `traceId`, JSON client nhận `ProblemDetails`,
  request trình duyệt được render lại thành trang lỗi.

### 2.5 Slide 6 — Blazor (ngoài phạm vi)

Chọn **MVC + SignalR JavaScript client** cho toàn bộ UI. Lý do đã ghi ở `General.md` §1: bản đặc tả
đầu tiên để Blazor là "tuỳ chọn", ba người trong 8 tuần mà làm cả hai thì không phần nào đủ sâu; phần
mở rộng nếu dư thời gian nằm ở `docs/project-state/IDEAS.md`. Đây là **quyết định phạm vi có lý do**,
không phải bỏ sót — nên nói thẳng khi bảo vệ.

### 2.6 Slide 7 — SignalR

- Mỗi suất chiếu là **một group** `showtime-{id}`; client join khi mở trang chọn ghế, hub gửi
  `SeatSnapshot` cho riêng người vừa vào (ghế đang giữ, có cờ `mine`) và `SeatLocked` /
  `SeatUnlocked` / `SeatRejected` cho nhóm.
- **Hub không giữ trạng thái:** SignalR tạo mới instance cho mỗi lần gọi, nên mọi trạng thái ghế nằm ở
  Singleton `ISeatLockManager`; hub chỉ là lớp vận chuyển message.
- **Ghế đã đặt** đọc từ DB ở controller và render sẵn vào trang — không đi qua hub, tránh việc mỗi
  client phải kéo cả danh sách ghế đã bán.
- Khóa theo cặp `(ShowtimeId, SeatId)`: cùng một ghế ở hai suất là hai lock độc lập.
- Rớt mạng / đóng tab → `OnDisconnectedAsync` trả hết ghế của connection đó và broadcast.

### 2.7 Slide 8 — Worker Service

Ba `BackgroundService` độc lập, mỗi cái một `PeriodicTimer` (dọn ghế 10 giây, gửi email 15 giây, báo
cáo ngày 1 giờ). Điểm "học được" của slide nằm ở chỗ phải viết đúng khuôn:

- Worker là **host riêng**, không có request nào cả → mọi thứ resolve từ `IServiceScopeFactory`, một
  scope cho **một vòng lặp**, xong là dispose.
- **Web không tự gửi email**: nó ghi vào bảng `OutboxEmails`, Worker đọc và gửi, retry tối đa 5 lần rồi
  đánh dấu `Failed`. Fire-and-forget trong request sẽ mất email khi process chết giữa chừng.
- Vòng lặp bọc `try/catch` để một lần lỗi không giết cả service.

## 3. Ánh xạ slide → chức năng → test

| Slide | Chức năng (`General.md` §3) | Test chứng minh |
| --- | --- | --- |
| 1 | Đặt vé + thanh toán giả lập | `AdminBookingCancellationTests` (đơn có thanh toán), `RevenueAggregationTests` |
| 2 | Truy vấn catalog/lịch chiếu, gửi email theo lô | `MovieCatalogServiceTests`, `ShowtimeQueryServiceTests` |
| 3 | Giữ ghế, mọi service trong hệ thống | `SeatLockManagerTests` (khoá, hết hạn, tranh chấp) |
| 4 & 5 | Form admin, form đăng ký, giỏ ghế, khu quản trị | `RegisterViewModelTests`, `AdminCatalogRulesTests`, `AdminAuthorizationTests`, `SeatMapViewModelTests`, `ExceptionHandlingMiddlewareTests` |
| 6 | — (ngoài phạm vi) | — |
| 7 | Giữ ghế real-time | `SeatLockManagerTests` (khoá/giải phóng), `SeatMapViewModelTests` (ghế đã đặt) |
| 8 | Dọn ghế hết hạn, email outbox, báo cáo ngày | `SeatLockManagerTests.Expired_lock_is_removed_and_reported`, `BookingSeatConstraintTests` |

Chạy lại toàn bộ trước khi nộp:

```bash
dotnet test                        # xUnit, toàn bộ project
dotnet format --verify-no-changes  # CI cũng chạy hai lệnh này
```

## 4. Còn lại trước khi bảo vệ (đừng hứa trong slide nếu chưa xong)

| Việc | Trạng thái | Liên quan |
| --- | --- | --- |
| Chọn ghế real-time **trên UI** (JS SignalR, đếm ngược, toast) | ⏳ C3.3–C3.10 chưa merge | Slide 7 — chưa có phần này thì demo 2 cửa sổ không chạy |
| Đặt vé end-to-end: transaction, thanh toán, mã vé, email | ⏳ B4.1–B4.5, C4.1–C4.3 chưa merge | Slide 1, 4 & 5 |
| Trang danh sách suất chiếu cho khách | ⏳ C2.6/C2.7 chưa merge | Slide 4 & 5 |
| `ExpiredSeatCleaner` chạy **trong process Web** (hiện nằm ở Worker nên nó đọc bảng lock rỗng của chính mình), phần broadcast `SeatUnlocked` còn `TODO` trong mã | ⏳ cần chốt trước demo | Slide 7, 8 |
| `DailyReportWorker` ghi `TheaterId` khi tổng hợp (AD-6, B4.8) | ⏳ chưa ghi | Slide 8, dashboard doanh thu |
| Ảnh cho [hướng dẫn sử dụng](user-guide.md) | ⏳ chờ merge rồi chụp | [`screenshots/CHECKLIST.md`](screenshots/CHECKLIST.md) |

## 5. Dàn slide gợi ý cho buổi bảo vệ (10 slide)

| # | Slide | Nội dung | Chỉ vào đâu trong repo/tài liệu |
| --- | --- | --- | --- |
| 1 | Mở đầu | Đề tài, phạm vi, 3 người / 4 sprint | `General.md` §1, `docs/project-state/SPRINT-PLAN.md` |
| 2 | Kiến trúc | 5 project, luồng một request, vì sao tách Domain | `General.md` §4, `technical-design.md` |
| 3 | Demo tổng thể | Chọn phim → suất → ghế → thanh toán → mã vé | chạy thật |
| 4 | SignalR (điểm nhấn) | Group theo suất, hub không giữ trạng thái, lock Singleton | `SeatHub.cs`, `di-lifetimes.md` §4 |
| 5 | Chống đặt trùng hai lớp | Lock RAM 5 phút + unique index trong DB | `BookingSeatConstraintTests`, `README.md` |
| 6 | MVC & validation | Form admin, kiểm tra nghiệp vụ ở server, Session giỏ ghế | `Areas/Admin/Models`, `SeatSelection.cs` |
| 7 | DI & vòng đời | Bảng vòng đời, captive dependency, scope trong worker | `di-lifetimes.md` |
| 8 | Worker Service | Ba background service, outbox email, báo cáo ngày | `src/CinemaBooking.Worker` |
| 9 | Quyết định kỹ thuật | PLINQ vs SQL, một rạp nhiều chi nhánh, hủy vé trả ghế | [ADR 0001](adr/0001-catalog-filtering-plinq-vs-sql.md), `technical-design.md` |
| 10 | Kết quả & hạn chế | Test xanh, CI, việc còn lại (§4 ở trên) | `dotnet test`, `.github/workflows/ci.yml` |

Nguyên tắc khi dựng slide: **mỗi slide kỹ thuật phải có một dòng "mở file này trong repo"** — người
chấm theo được từ slide xuống mã nguồn, đó chính là mục đích của tài liệu này.
