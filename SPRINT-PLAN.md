# Kế hoạch Sprint — CinemaBooking.Smart

**Nhóm 3 người · 8 tuần · 4 sprint × 2 tuần · 07/09/2026 → 01/11/2026**

Tài liệu đặc tả: [`General.md`](../../General.md)

---

## 1. Phân vai

| Ký hiệu | Vai trò | Sở hữu chính |
|---|---|---|
| **A** | Backend / Data | Domain, EF Core, migration, seed, Admin CRUD, thống kê |
| **B** | Real-time / Worker | `SeatHub`, `SeatLockManager`, Booking service, thanh toán, 3 Worker, unit test |
| **C** | Frontend / UX | Razor Views, layout, sơ đồ ghế, SignalR JS client, Identity UI, form đặt vé |

Vai trò là **sở hữu chính**, không phải tường ngăn: ai rảnh thì nhận thêm. Mỗi task đều phải có
người thứ hai review PR (§4).

---

## 2. Ngân sách commit

Mục tiêu **120 commit**, chia đều: **40 commit / người**, tức **10 commit / người / sprint**
≈ **5 commit / người / tuần**.

| | Sprint 1 | Sprint 2 | Sprint 3 | Sprint 4 | Tổng |
|---|---|---|---|---|---|
| **A** | 10 | 10 | 10 | 10 | **40** |
| **B** | 10 | 10 | 10 | 10 | **40** |
| **C** | 10 | 10 | 10 | 10 | **40** |
| **Tổng** | 30 | 30 | 30 | 30 | **120** |

Danh sách commit bên dưới là **kế hoạch, không phải hợp đồng**. Nếu một task cần tách làm 2 commit
thì gộp một task khác lại — giữ tổng mỗi người mỗi sprint ở mức 10 (±2 là bình thường).
Điều thật sự quan trọng: **không ai dồn 30 commit vào ngày cuối sprint.**

---

## 3. Quy ước commit & nhánh

**Nhánh:** `master` (luôn build được) ← `dev` (tích hợp hằng ngày) ← `<type>/s<sprint>-<dev>-<mô tả>`
Ví dụ: `feat/s3-b-seat-hub`, `fix/s2-a-showtime-overlap`.

- `master`: không ai push thẳng; chỉ nhận merge từ `dev` vào cuối mỗi sprint, kèm tag `v0.1`…`v0.4`.
- `dev`: mọi PR đổ về đây, 1 reviewer bắt buộc, CI phải xanh.
- Nhánh việc: 1 nhánh = 1 dòng trong bảng commit (hoặc 2–3 dòng liền kề của cùng người), sống ≤ 2
  ngày rồi merge. `<dev>` là `a`/`b`/`c` để hai người không trùng tên nhánh khi làm song song.

**Chống đụng file** (scope guard chỉ chặn sai file, không chặn 3 người sửa cùng một file):

| File / vùng | Ai đụng | Quy ước |
|---|---|---|
| `Program.cs` (Web) | C1.3, B1.6, B2.8, B2.9, B3.7 | Mỗi người chỉ thêm dòng vào block DI của mình, không reorder block người khác |
| Seed dữ liệu | A1.7, A1.8 vs C1.6 | Tách `CatalogSeeder` (A) và `IdentitySeeder` (C); merge A trước |
| EF Core migration | A1.6, A3.5, A4.6 | **Chỉ Dev A** tạo migration; ai cần đổi schema thì báo A |
| Luồng seat map | A3.1, A3.3 · B3.1 · C3.1 | Merge A3.1 + A3.3 vào `dev` sớm trong tuần 1 để B và C có contract |

**Thông điệp commit** — Conventional Commits, mô tả bằng tiếng Anh, ngắn gọn:

```text
<type>(<scope>): <mô tả ở thì hiện tại>

type:  feat | fix | refactor | test | docs | chore | style
scope: domain | data | web | hub | worker | ui | admin | auth | ci
```

Ví dụ: `feat(hub): broadcast SeatLocked to showtime group`

**Nhịp làm việc:** commit ít nhất 1 lần/ngày làm việc; push mỗi cuối ngày; PR nhỏ (≤ 400 dòng thay
đổi) và merge trong vòng 24h.

---

## 4. Nghi thức nhóm

| Khi nào | Việc gì | Thời lượng |
|---|---|---|
| Thứ 2, 4, 6 | Standup: hôm qua / hôm nay / vướng gì | 10 phút |
| Cuối tuần 2 mỗi sprint | Sprint review — demo chạy thật, không demo bằng slide | 30 phút |
| Ngay sau review | Retro: giữ gì / bỏ gì | 20 phút |
| Mỗi PR | 1 reviewer bắt buộc, không tự merge PR của mình | — |

**Definition of Done** cho mọi task: build không warning · có test nếu là logic nghiệp vụ ·
chạy thử trên trình duyệt · PR được review · migration chạy được trên DB sạch · cập nhật tài liệu.

---

## 5. Sprint 1 — Nền tảng · 07/09 → 20/09

**Mục tiêu sprint:** chạy `dotnet run`, đăng nhập được, DB có dữ liệu mẫu, CI xanh.
**Demo:** trang chủ liệt kê phim từ database thật.

### Dev A — Domain & Data (10 commit)

| # | Commit | Ghi chú |
|---|---|---|
| A1.1 | `chore(repo): scaffold solution and five projects` | ✅ đã có sẵn trong khung |
| A1.2 | `feat(domain): add catalog entities Movie Theater Room Seat Showtime` | ✅ |
| A1.3 | `feat(domain): add Booking BookingSeat Payment entities` | ✅ |
| A1.4 | `feat(domain): add enums and OutboxEmail entity` | ✅ |
| A1.5 | `feat(data): add CinemaDbContext with indexes and precision` | ✅ |
| A1.6 | `feat(data): add initial EF Core migration` | `dotnet ef migrations add Initial` |
| A1.7 | `feat(data): seed movies theaters rooms and showtimes` | 5 phim, 2 rạp, 4 phòng |
| A1.8 | `feat(data): generate seats from room row and column config` | Sinh ghế tự động |
| A1.9 | `test(data): cover seat generation and unique seat index` | |
| A1.10 | `docs(data): document schema decisions in technical-design` | |

### Dev B — Hạ tầng dịch vụ (10 commit)

| # | Commit | Ghi chú |
|---|---|---|
| B1.1 | `feat(domain): define ISeatLockManager with SeatKey` | ✅ |
| B1.2 | `feat(domain): define IPaymentService and IEmailSender` | ✅ |
| B1.3 | `feat(infra): implement InMemorySeatLockManager` | ✅ |
| B1.4 | `test(infra): cover concurrent TryLock grants exactly one` | ✅ |
| B1.5 | `test(infra): cover expiry release and per-showtime isolation` | ✅ |
| B1.6 | `feat(infra): register services via AddCinemaInfrastructure` | ✅ |
| B1.7 | `feat(infra): add MockPaymentService over IHttpClientFactory` | ✅ |
| B1.8 | `chore(ci): add GitHub Actions build and test workflow` | |
| B1.9 | `feat(worker): scaffold host and register three background services` | ✅ |
| B1.10 | `docs(infra): document DI lifetimes and the singleton pitfall` | §7.6 |

### Dev C — Nền UI & xác thực (10 commit)

| # | Commit | Ghi chú |
|---|---|---|
| C1.1 | `feat(web): add layout navbar and footer` | |
| C1.2 | `feat(web): add site theme and shared css tokens` | |
| C1.3 | `feat(auth): wire ASP.NET Identity into Program` | ✅ |
| C1.4 | `feat(auth): add register and login pages` | |
| C1.5 | `feat(auth): add logout and account partial in navbar` | |
| C1.6 | `feat(auth): seed Admin and Customer roles with a default admin` | Phối hợp với A |
| C1.7 | `feat(web): add home page listing active movies` | |
| C1.8 | `feat(web): add movie card component and poster fallback` | |
| C1.9 | `feat(web): add responsive grid for movie list` | |
| C1.10 | `docs(web): add README with local setup steps` | Connection string, `dotnet ef` |

---

## 6. Sprint 2 — Catalog & Admin · 21/09 → 04/10

**Mục tiêu sprint:** admin quản trị được toàn bộ dữ liệu; khách xem được lịch chiếu.
**Demo:** admin tạo phim mới + suất chiếu → khách thấy ngay ở trang chi tiết phim.

### Dev A (10 commit)

| # | Commit |
|---|---|
| A2.1 | `feat(admin): add MoviesController with list and search` |
| A2.2 | `feat(admin): add create and edit movie with validation` |
| A2.3 | `feat(admin): soft delete movie via IsActive flag` |
| A2.4 | `feat(admin): add TheatersController crud` |
| A2.5 | `feat(admin): add RoomsController with seat map config` |
| A2.6 | `feat(admin): regenerate seats when room layout changes` |
| A2.7 | `feat(admin): add ShowtimesController crud` |
| A2.8 | `feat(admin): reject overlapping showtimes in the same room` |
| A2.9 | `test(admin): cover showtime overlap and soft delete rules` |
| A2.10 | `refactor(data): extract query helpers for catalog reads` |

### Dev B (10 commit)

| # | Commit |
|---|---|
| B2.1 | `feat(web): add showtime query service with date filter` |
| B2.2 | `feat(web): add movie search and genre filter service` |
| B2.3 | `perf(web): push catalog filtering down to SQL` |
| B2.4 | `feat(web): compute EndTimeUtc from movie duration on create` |
| B2.5 | `feat(infra): add booking code generator` |
| B2.6 | `test(web): cover showtime query filters` |
| B2.7 | `test(web): cover movie search and genre filter` |
| B2.8 | `feat(infra): add global exception handling middleware` |
| B2.9 | `feat(infra): add structured logging configuration` |
| B2.10 | `docs(arch): record the PLINQ versus SQL decision` |

### Dev C (10 commit)

| # | Commit |
|---|---|
| C2.1 | `feat(admin): add admin layout with sidebar navigation` |
| C2.2 | `feat(admin): add movie list and form views` |
| C2.3 | `feat(admin): add theater and room views` |
| C2.4 | `feat(admin): add room seat map preview` |
| C2.5 | `feat(admin): add showtime list and form views` |
| C2.6 | `feat(web): add movie detail page with showtimes by date` |
| C2.7 | `feat(web): add date tab strip for showtime selection` |
| C2.8 | `feat(web): add search bar and genre filter to home page` |
| C2.9 | `feat(web): wire client side validation with unobtrusive jquery` |
| C2.10 | `fix(web): make admin tables scroll on small screens` |

---

## 7. Sprint 3 — Real-time (sprint quan trọng nhất) · 05/10 → 18/10

**Mục tiêu sprint:** hai trình duyệt mở cùng một suất chiếu — người này chọn ghế, người kia thấy ngay.
**Demo:** mở 2 cửa sổ cạnh nhau, click ghế, ghế đổi màu ở cửa sổ còn lại trong dưới 1 giây.

> ⚠️ Đây là sprint rủi ro nhất. Ưu tiên **cuối tuần 1 phải có hub gửi/nhận được tin nhắn**,
> dù giao diện còn xấu. Đừng để tích hợp SignalR dồn vào tuần 2.

### Dev A (10 commit)

| # | Commit |
|---|---|
| A3.1 | `feat(web): add BookingController seat map action` |
| A3.2 | `feat(data): query confirmed seats for a showtime` |
| A3.3 | `feat(web): build SeatMapViewModel from room layout` |
| A3.4 | `feat(data): add ShowtimeId to BookingSeat with unique index` |
| A3.5 | `feat(data): add migration for the booking unique index` |
| A3.6 | `feat(data): seed sample confirmed bookings for testing` |
| A3.7 | `feat(web): store selected seats in session` |
| A3.8 | `test(web): cover seat map view model construction` |
| A3.9 | `test(data): cover unique index rejecting duplicate seat booking` |
| A3.10 | `refactor(web): move seat pricing into a dedicated service` |

### Dev B (10 commit)

| # | Commit |
|---|---|
| B3.1 | `feat(hub): add SeatHub with showtime groups` |
| B3.2 | `feat(hub): implement SelectSeat with lock acquisition` |
| B3.3 | `feat(hub): broadcast SeatLocked to other clients in group` |
| B3.4 | `feat(hub): implement ReleaseSeat and SeatUnlocked broadcast` |
| B3.5 | `feat(hub): send SeatSnapshot on JoinShowtime` |
| B3.6 | `feat(hub): release all locks on client disconnect` |
| B3.7 | `feat(infra): make hold duration configurable via SeatHold section` |
| B3.8 | `feat(hub): reject selection when seat is already confirmed` |
| B3.9 | `test(hub): cover select release and disconnect flows` |
| B3.10 | `test(infra): cover stealing an expired lock` |

### Dev C (10 commit)

| # | Commit |
|---|---|
| C3.1 | `feat(ui): render seat map grid from view model` |
| C3.2 | `feat(ui): add seat state styles for free held mine and booked` |
| C3.3 | `feat(ui): connect SignalR client and join showtime group` |
| C3.4 | `feat(ui): send SelectSeat and ReleaseSeat on seat click` |
| C3.5 | `feat(ui): apply SeatLocked and SeatUnlocked events to the grid` |
| C3.6 | `feat(ui): apply SeatSnapshot on join` |
| C3.7 | `feat(ui): add five minute countdown timer for held seats` |
| C3.8 | `feat(ui): show toast when a seat is rejected` |
| C3.9 | `feat(ui): auto reconnect SignalR and refresh snapshot` |
| C3.10 | `feat(ui): show running total for selected seats` |

---

## 8. Sprint 4 — Đặt vé, Worker, hoàn thiện · 19/10 → 01/11

**Mục tiêu sprint:** đặt vé end-to-end, email vào outbox và được gửi, ghế hết hạn tự trả.
**Demo:** đặt vé thật → xem mã vé → xem email trong log → để 5 phút cho ghế tự nhả.

> 🚩 **Chốt tính năng vào hết tuần 1 (25/10).** Tuần 2 chỉ sửa lỗi, viết test, làm tài liệu và
> chuẩn bị bảo vệ. Không nhận tính năng mới sau mốc này.

### Dev A (10 commit)

| # | Commit |
|---|---|
| A4.1 | `feat(admin): add booking list with code and status filter` |
| A4.2 | `feat(admin): add booking detail with seat breakdown` |
| A4.3 | `feat(admin): cancel a booking and free its seats` |
| A4.4 | `feat(admin): add revenue dashboard by day` |
| A4.5 | `feat(admin): add revenue breakdown by movie and showtime` |
| A4.6 | `feat(data): add DailyRevenueReport migration` |
| A4.7 | `test(admin): cover cancellation and revenue aggregation` |
| A4.8 | `docs(project): write user guide with screenshots` |
| A4.9 | `docs(project): write slide mapping report for submission` |
| A4.10 | `chore(data): add reset and reseed script for demo` |

### Dev B (10 commit)

| # | Commit |
|---|---|
| B4.1 | `feat(web): create booking inside a single transaction` |
| B4.2 | `feat(web): revalidate seat locks before writing the booking` |
| B4.3 | `feat(web): handle unique index violation as a friendly error` |
| B4.4 | `feat(web): charge through MockPaymentService and confirm booking` |
| B4.5 | `feat(web): enqueue confirmation email into the outbox` |
| B4.6 | `feat(worker): implement ExpiredSeatCleaner on a ten second timer` |
| B4.7 | `feat(worker): implement EmailOutboxWorker with retry limit` |
| B4.8 | `feat(worker): implement DailyReportWorker aggregation` |
| B4.9 | `feat(worker): broadcast SeatUnlocked from worker via hub client` |
| B4.10 | `test(web): cover the full booking flow end to end` |

### Dev C (10 commit)

| # | Commit |
|---|---|
| C4.1 | `feat(ui): add checkout page with customer form` |
| C4.2 | `feat(ui): show selected seat summary and total on checkout` |
| C4.3 | `feat(ui): add booking confirmation page with booking code` |
| C4.4 | `feat(ui): mark seats as booked in real time after purchase` |
| C4.5 | `feat(ui): add booking lookup page by code` |
| C4.6 | `feat(ui): add my bookings page for signed in users` |
| C4.7 | `feat(ui): add empty and error states across pages` |
| C4.8 | `fix(ui): make the seat map usable on mobile` |
| C4.9 | `style(ui): unify spacing colors and typography` |
| C4.10 | `docs(ui): add screenshots to README` |

---

## 9. Mốc kiểm tra

| Ngày | Mốc | Tiêu chí đạt |
|---|---|---|
| 20/09 | Kết thúc Sprint 1 | `dotnet run` lên trang chủ có phim từ DB; login được; CI xanh |
| 04/10 | Kết thúc Sprint 2 | Admin CRUD đủ 4 thực thể; khách xem được lịch chiếu theo ngày |
| 11/10 | **Giữa Sprint 3** | Hub gửi/nhận được tin nhắn giữa 2 client, dù UI còn thô |
| 18/10 | Kết thúc Sprint 3 | 2 trình duyệt thấy ghế nhau đổi màu dưới 1 giây |
| 25/10 | **Chốt tính năng** | Đặt vé end-to-end chạy được; từ đây chỉ sửa lỗi |
| 01/11 | Kết thúc Sprint 4 | Toàn bộ tính năng + test + tài liệu; sẵn sàng bảo vệ |

---

## 10. Kế hoạch dự phòng

Nếu đến **11/10** SignalR vẫn chưa chạy giữa 2 client, cắt theo thứ tự sau — cắt từ dưới lên:

1. Bỏ `DailyReportWorker` (thống kê tính trực tiếp bằng LINQ khi mở trang admin).
2. Bỏ trang "vé của tôi" và trang tra cứu theo mã.
3. Bỏ `EmailOutboxWorker`, giữ `LoggingEmailSender` ghi log thay vì gửi thật.
4. Bỏ soft delete / lịch sử — chỉ giữ CRUD cơ bản.

**Không bao giờ cắt:** `SeatHub` + `SeatLockManager` + luồng đặt vé + unique index chống đặt trùng.
Đó là toàn bộ giá trị của đề tài.
