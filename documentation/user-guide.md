# Hướng dẫn sử dụng — CinemaBooking.Smart

> Dev A · Sprint 4 (A4.8) · Bổ sung cho [`General.md`](../General.md) §3 (danh sách chức năng) và
> [`README.md`](../README.md) (cách chạy dự án).
>
> **Ảnh trong tài liệu này là placeholder.** Mỗi mục cần ảnh có một dòng dạng
> `📷 screenshots/03-chon-ghe.png`. Nhóm chụp theo [checklist ảnh](screenshots/CHECKLIST.md), lưu
> đúng tên file đó vào `documentation/screenshots/` là ảnh khớp tài liệu, không phải sửa markdown.

Tài liệu viết cho **người dùng cuối** (khách đặt vé) và **quản trị viên**. Kiến trúc, lý do kỹ thuật
không nằm ở đây — xem [`technical-design.md`](technical-design.md) và thư mục [ADR](adr/).

## 0. Phạm vi tài liệu

Viết ngày 2026-10-01, trên nhánh `feat/s4-a-revenue` (Dev A đã xong A4.1–A4.7). Phần việc của Dev B
và Dev C chưa merge, nên tài liệu ghi rõ chỗ nào **đã có trong mã nguồn** và chỗ nào **mô tả theo hợp
đồng đã chốt** giữa ba dev (AD-5, AD-6):

| Khu vực | Trạng thái |
| --- | --- |
| Trang chủ — danh sách phim đang chiếu | ✅ có |
| Khu quản trị `/Admin/**` — phim, chi nhánh, phòng, suất chiếu, đơn, doanh thu | ✅ có |
| Sơ đồ ghế `/Booking/SeatMap/{id}` + giỏ ghế trong Session | ✅ có (lưới còn thô, JS SignalR của C3.x chưa merge) |
| Danh sách suất chiếu phía khách (C2.6/C2.7) | ⏳ chờ merge |
| Chọn ghế real-time trên UI (C3.3–C3.10) | ⏳ chờ merge |
| Form khách + thanh toán + trang xác nhận mã vé (B4.1–B4.5, C4.1–C4.3) | ⏳ chờ merge |
| Email xác nhận, worker dọn ghế hết hạn / báo cáo ngày (B4.6–B4.9) | ⏳ chờ merge |

**Hệ quả cho người chụp ảnh:** chụp sau khi merge đủ ba nhánh. Chụp trước khi merge thì sơ đồ ghế
không có SignalR, luồng đặt vé không có bước thanh toán, và ảnh sẽ lệch tài liệu.

## 1. Chuẩn bị môi trường

### 1.1 Cần gì

- .NET SDK 9.0+
- SQL Server LocalDB (kèm Visual Studio) hoặc SQL Server
- Hai cửa sổ terminal: một cho Web, một cho Worker

### 1.2 Chạy lần đầu

```bash
dotnet build
dotnet run --project src/CinemaBooking.Web        # cửa sổ 1
dotnet run --project src/CinemaBooking.Worker     # cửa sổ 2
```

Mở **http://localhost:5005** (profile `http`) hoặc **https://localhost:7157** (profile `https`).

Lần chạy đầu tiên Web tự làm ba việc, theo đúng thứ tự: áp migration → `CatalogSeeder` (phim, rạp,
phòng, suất chiếu, đơn mẫu) → `IdentitySeeder` (vai trò + tài khoản admin). Không phải chạy
`dotnet ef database update` bằng tay.

Worker **không** bắt buộc để xem phim và chọn ghế (ghế vẫn hết hạn và chọn lại được, vì đồng hồ giữ
ghế nằm ở phía Web). Worker bắt buộc cho **email xác nhận** và **báo cáo ngày**; phần bắn sự kiện nhả
ghế realtime còn chờ B4.9 ⏳. Demo đầy đủ thì chạy cả hai cửa sổ.

| Cần | Nghe ở |
| --- | --- |
| Cổng web | `Now listening on: http://localhost:5005` |
| Seed chạy xong | log `Đã tạo tài khoản admin ...` (chỉ in ở lần đầu) |
| Worker sống | không in dòng "listening" (đây là console app); thấy log khi có email gửi lỗi, hoặc khi nó dọn được lock hết hạn |

📷 `screenshots/01-trang-chu.png`

### 1.3 Tài khoản dùng để demo

| Vai trò | Đăng nhập ở | Giá trị |
| --- | --- | --- |
| `Admin` | `/Account/Login` | `admin@cinemabooking.local` · mật khẩu `Admin@12345` (mặc định trong mã nguồn, **chỉ để demo**) |
| `Customer` | `/Account/Register` | Tự đăng ký; mật khẩu ≥ 8 ký tự |

Tài khoản admin do `IdentitySeeder` tạo lúc khởi động; chạy lại bao nhiêu lần cũng không tạo trùng.
Khi còn dùng mật khẩu mặc định, log khởi động in một dòng `warn` — đổi bằng
`SeedAdmin:Password` trong `appsettings.Development.json` hoặc user-secrets (xem `README.md`).

📷 `screenshots/09-dang-nhap.png`

### 1.4 Làm lại dữ liệu demo (trước khi bảo vệ)

Đặt lại database về đúng trạng thái seed, không phải nhớ chuỗi lệnh:

```powershell
.\scripts\reset-demo.ps1              # PowerShell 5.1 hoặc PowerShell 7
.\scripts\reset-demo.ps1 -WhatIf      # chỉ in ra việc sẽ làm, không đụng database
```

Script dừng tiến trình Web/Worker đang chạy, drop database, rồi khởi động Web một lần để tạo lại
schema + seed và tự tắt. Chi tiết ở `.\scripts\reset-demo.ps1 -?`.

> Mỗi dev một database riêng trên máy mình (`LocalDB\CinemaBookingSmart`), **không dùng chung DB** —
> xem `README.md` §"Ba dev đồng bộ database thế nào".

## 2. Dữ liệu mẫu có sẵn sau khi seed

### 2.1 Phim (5 phim, danh mục dùng chung)

| Phim | Thể loại | Thời lượng |
| --- | --- | --- |
| Mắt Biếc | Tình cảm | 117 phút |
| Nhà Bà Nữ | Hài | 106 phút |
| Dune: Part Two | Khoa học viễn tưởng | 166 phút |
| Inside Out 2 | Hoạt hình | 96 phút |
| Địa Đạo: Mặt Trời Trong Bóng Tối | Chiến tranh | 128 phút |

Poster thiếu file **không** làm vỡ layout: view tự dùng `/img/poster-placeholder.svg`.

### 2.2 Chi nhánh và phòng

| Chi nhánh | Thành phố | Phòng (số ghế) |
| --- | --- | --- |
| CineSmart Nguyễn Huệ | TP. Hồ Chí Minh | Phòng 1 (8×12 = 96) · Phòng 2 (6×10 = 60) |
| CineSmart Thảo Điền | TP. Hồ Chí Minh | Phòng 1 (7×10 = 70) |
| CineSmart Cầu Giấy | Hà Nội | Phòng A (10×14 = 140) · Phòng B (5×8 = 40) |
| CineSmart Hoàn Kiếm | Hà Nội | Phòng 1 (6×9 = 54) |

Ghế do `SeatGenerator` sinh theo cấu hình hàng × cột: **hàng cuối là ghế đôi**, **một phần ba giữa
phòng là ghế VIP**, còn lại ghế thường. Cùng một phòng luôn cho ra cùng một sơ đồ.

### 2.3 Suất chiếu và giá

- Sinh cho **7 ngày kể từ hôm nay**, mỗi phòng × mỗi khung giờ của chi nhánh là một suất, phim xoay
  vòng nên mọi phim đều có suất và cùng một phim xuất hiện ở nhiều chi nhánh với giờ, giá khác nhau.
- Khung giờ riêng theo chi nhánh (ví dụ Nguyễn Huệ: 09:00, 12:15, 15:30, 18:45, 22:00; Hoàn Kiếm:
  11:00, 17:00, 20:30), cách nhau ≥ 3h15 để không chồng suất.
- Giá gốc theo chi nhánh (80.000–95.000đ), rồi điều chỉnh:

| Yếu tố | Mức |
| --- | --- |
| Phòng nhỏ dưới 100 ghế | −15.000đ |
| Suất bắt đầu từ 18:00 | +25.000đ |
| Ghế VIP | ×1,2 giá gốc của suất |
| Ghế đôi | ×2 giá gốc của suất |

Công thức nằm ở một chỗ duy nhất (`SeatPricingService`), nên giá hiện trên sơ đồ ghế và
`PriceAtBooking` ghi vào đơn luôn khớp nhau.

### 2.4 Đơn mẫu

Seed sẵn 8 đơn đã thanh toán (`CB-SEED01` … `CB-SEED08`) ở **suất sớm nhất ngày mai của mỗi chi
nhánh**, ghế hàng A: mở sơ đồ ghế là thấy ngay ghế "đã đặt" mà không phải tự đặt vé trước. Hai đơn
mỗi chi nhánh, khách `Nguyễn Văn An` và `Trần Thị Bình`.

## 3. Luồng khách hàng

### 3.1 Xem phim đang chiếu — `/`

Trang chủ liệt kê phim `IsActive`, mới khởi chiếu trước. Thanh tìm kiếm và lọc thể loại nằm cùng
trang (C2.8) ⏳.

📷 `screenshots/01-trang-chu.png`

### 3.2 Chọn phim, chọn suất ⏳

Bấm vào một phim để mở trang chi tiết: suất chiếu gom theo **ngày → chi nhánh**, lọc được theo thành
phố; mỗi suất hiện giờ, phòng và giá gốc. Khách tự chọn suất, không bị gắn vào một chi nhánh nào.

📷 `screenshots/02-chi-tiet-phim-suat-chieu.png`

### 3.3 Chọn ghế — `/Booking/SeatMap/{showtimeId}`

Sơ đồ phòng chiếu vẽ theo đúng hàng × cột của phòng. Quy ước màu và thao tác:

| Trạng thái ghế | Hiển thị | Bấm được? |
| --- | --- | --- |
| Trống | ghế thường / VIP / đôi theo viền | có |
| Bạn đang giữ | đổi màu "đang chọn", có đồng hồ đếm ngược | có (bấm lại để nhả) |
| Người khác đang giữ | khoá tạm, đổi màu ngay khi họ click | không |
| Đã đặt | đỏ, không bấm được (đọc từ `BookingSeats` trong DB) | không |

Ghế giữ **5 phút** kể từ lúc click (`SeatHold:DurationMinutes`). Hết 5 phút, ghế **coi như trống** với
mọi người: lock hết hạn bị bỏ qua khi đọc trạng thái và người khác chọn được ngay, kể cả lúc tiến
trình dọn ghế chưa kịp quét. Việc **bắn sự kiện** để các cửa sổ đang mở tự đổi màu ghế đó là phần của
Worker + hub client (B4.6/B4.9) ⏳ — chưa merge thì phải tải lại trang mới thấy ghế trống.

Chọn xong bấm **Tiếp tục** — server kiểm tra lại toàn bộ: ghế thuộc đúng phòng của suất, chưa ai đặt,
và đang do **chính** kết nối này giữ. Sai một điều kiện thì quay lại sơ đồ kèm thông báo đỏ, ví dụ
`Thời gian giữ ghế đã hết hoặc ghế không còn do bạn giữ. Vui lòng chọn lại.`

📷 `screenshots/03-chon-ghe-trong.png`
📷 `screenshots/05-dem-nguoc-5-phut.png`

### 3.4 Xác nhận ghế — `/Booking/Checkout`

Trang tóm tắt giỏ ghế (phim, chi nhánh, phòng, giờ, danh sách ghế và loại ghế) đọc từ **Session** —
client không gửi lại danh sách ghế nên không sửa được giá trị. Session giữ 20 phút. Nút **Chọn lại
ghế** quay về sơ đồ; vào thẳng `/Booking/Checkout` khi chưa chọn ghế thì bị đưa về trang chủ.

📷 `screenshots/06-thanh-toan.png`

### 3.5 Điền thông tin và thanh toán ⏳

Form thông tin khách (họ tên, email, số điện thoại) + nút thanh toán. Hệ thống:

1. Mở **một transaction**: kiểm tra lại lock còn hiệu lực, ghi `Booking` (trạng thái `Pending`) và
   các dòng `BookingSeats`.
2. Gọi cổng thanh toán giả lập qua `IHttpClientFactory`.
3. Thành công thì đơn chuyển `Confirmed`, ghi `Payment` và **enqueue email xác nhận vào outbox**.

Ghế đã có người đặt thì DB từ chối bằng unique index `BookingSeat(ShowtimeId, SeatId)`; khách nhận
thông báo tử tế chứ không phải trang lỗi 500.

### 3.6 Nhận mã vé và email ⏳

Sau khi thanh toán, trang xác nhận hiện **mã đặt vé** (dạng `CB-XXXXXXXX`), phim, suất, ghế, tổng
tiền. Email xác nhận do **Worker** gửi (mỗi 15 giây quét outbox, retry tối đa 5 lần); ở môi trường
dev, `LoggingEmailSender` ghi nội dung email vào log của Worker — mở cửa sổ Worker để chụp làm bằng
chứng.

📷 `screenshots/07-xac-nhan-dat-ve.png`
📷 `screenshots/08-email-trong-log.png`

### 3.7 Tra cứu vé và vé của tôi ⏳

Tra cứu theo mã đặt vé (không cần đăng nhập) và danh sách vé của tài khoản đã đăng nhập.

## 4. Luồng quản trị viên

Đăng nhập bằng tài khoản `Admin`. Toàn bộ khu `/Admin/**` yêu cầu vai trò `Admin`; khách đã đăng
nhập nhưng không phải admin bị đưa tới `/Account/AccessDenied`.

| Trang | Đường dẫn | Làm được gì |
| --- | --- | --- |
| Phim | `/Admin/Movies` | Thêm/sửa phim; xoá là **ẩn** (`IsActive = false`) để không phá đơn cũ và suất đã bán |
| Chi nhánh | `/Admin/Theaters` | Thêm/sửa/ẩn chi nhánh (tên, thành phố, địa chỉ) |
| Phòng | `/Admin/Rooms?theaterId=…` | Thêm/sửa phòng theo số hàng × số cột; **đổi layout là sinh lại ghế**, ràng buộc `Seat(RoomId, RowNumber, ColumnNumber)` chặn ghế trùng |
| Suất chiếu | `/Admin/Showtimes` | Lọc theo chi nhánh và ngày, thêm/sửa suất: chọn phim từ danh mục, phòng, giờ, giá gốc; suất cùng phòng phải cách nhau ≥ 15 phút dọn phòng |
| Đơn đặt vé | `/Admin/Bookings` | Lọc theo mã, chi nhánh, trạng thái (`Pending` / `Confirmed` / `Cancelled` / `Expired`); tối đa 100 đơn mới nhất mỗi lần xem |
| Chi tiết đơn | `/Admin/Bookings/Details/{id}` | Thông tin khách, phim, suất, **danh sách ghế kèm giá từng ghế**, trạng thái thanh toán, mã giao dịch |
| Hủy đơn | nút trên trang chi tiết | Đơn chuyển `Cancelled`, **xoá các dòng `BookingSeats` để trả ghế**, thanh toán đã thành công thì đánh dấu `Refunded` |
| Doanh thu theo ngày | `/Admin/Revenue` | Mặc định 7 ngày gần nhất; bảng theo ngày × chi nhánh (số vé, doanh thu) + tổng theo chi nhánh |
| Doanh thu chi tiết | `/Admin/Revenue/Breakdown` | Chia theo **phim** và theo **từng suất chiếu** (số vé / sức chứa phòng), lọc theo chi nhánh |

Hai điều dễ hiểu sai khi demo:

- **Doanh thu tính theo ngày ĐẶT vé** (lúc thu tiền), theo giờ rạp (UTC+7), **không phải ngày chiếu**.
- Chỉ tính vé của đơn `Confirmed`. Hủy đơn xong, doanh thu ngày đó giảm ngay — đây là hành vi đúng,
  và cũng là cách nhanh nhất để kiểm chứng nút hủy có thật sự trả ghế: mở lại sơ đồ ghế của suất đó.

Hủy đơn **không** tự nhả lock RAM của người đang giữ ghế; lock hết hạn theo đồng hồ 5 phút là tự hết.

📷 `screenshots/10-admin-phim.png`
📷 `screenshots/11-admin-chi-nhanh-phong.png`
📷 `screenshots/12-admin-suat-chieu.png`
📷 `screenshots/13-admin-don.png`
📷 `screenshots/14-admin-chi-tiet-don.png`
📷 `screenshots/15-admin-huy-don.png`
📷 `screenshots/16-admin-doanh-thu.png`
📷 `screenshots/17-admin-doanh-thu-chi-tiet.png`

## 5. Kịch bản demo giữ ghế real-time (2 cửa sổ)

1. Mở cùng một suất chiếu ở **hai cửa sổ** (một cửa sổ ẩn danh để không dùng chung cookie/Session).
2. Cửa sổ A click ghế A5 → ghế đổi màu ở A.
3. Cửa sổ B: ghế A5 đổi màu "đang có người giữ" **trong dưới 1 giây**.
4. Cửa sổ B click A5 → bị từ chối, hiện thông báo ghế đang được người khác chọn.
5. Cửa sổ A bấm lại A5 (hoặc đóng tab) → cửa sổ B thấy ghế về trống.
6. Muốn xem ghế **tự** nhả: để yên 5 phút rồi bấm lại ghế đó ở cửa sổ B — chọn được ngay. Cửa sổ
   đang mở **tự** đổi màu khi hết hạn là việc của Worker + hub client (B4.9) ⏳.

Ghi chú khi demo:

- Đóng tab / mất mạng thì ghế trả ngay (hub xử lý ở `OnDisconnectedAsync`).
- Lock nằm trong RAM **một process**: chạy nhiều instance web sau load balancer thì hai người có thể
  cùng chọn một ghế — chốt chặn thật là unique index trong DB (xem `technical-design/di-lifetimes.md` §5).
- Ở bước thanh toán, ghế được giữ bằng bản ghi DB, **không** dựa vào kết nối SignalR.

📷 `screenshots/04-hai-cua-so-giu-ghe.png`
📷 `screenshots/18-ghe-tu-nha-sau-5-phut.png`

## 6. Sự cố thường gặp

| Hiện tượng | Nguyên nhân | Xử lý |
| --- | --- | --- |
| Trang chủ báo "Chưa có phim nào" | Database trống / chưa seed | Khởi động lại Web, hoặc `.\scripts\reset-demo.ps1` |
| `Invalid object name 'AspNetRoles'` khi khởi động | Chưa có migration nào được áp | Web tự áp migration lúc khởi động — chạy `dotnet run --project src/CinemaBooking.Web` một lần; nếu vẫn lỗi thì `dotnet ef database update -p src/CinemaBooking.Infrastructure -s src/CinemaBooking.Web` |
| `Cannot open database ...` | LocalDB chưa chạy hoặc sai chuỗi kết nối | Kiểm tra `ConnectionStrings:Default` trong `src/CinemaBooking.Web/appsettings.json`; không commit chuỗi thật (dùng `appsettings.Development.json`) |
| Drop database báo "database in use" | Web/Worker đang chạy | Tắt hai tiến trình (script `reset-demo.ps1` tự làm) |
| Bấm Tiếp tục báo "Chưa chọn ghế nào" | Client chưa đẩy `seatIds` vào form (JS SignalR của C3.x) | Đây là lỗi của bản chưa merge JS, không phải lỗi dữ liệu; kiểm tra đã merge nhánh Dev C chưa |
| Ghế hết hạn nhưng cửa sổ khác không thấy đổi màu | Sự kiện nhả ghế do Worker bắn (B4.9) chưa merge | Không phải lock còn giữ: người khác **chọn được** ghế đó ngay; tải lại trang là thấy trống |
| Không thấy log dọn ghế trong Worker | `ExpiredSeatCleaner` chạy trong **process Worker**, nơi bảng lock rỗng, nên log chỉ in khi có lock hết hạn trong chính process đó | Không ảnh hưởng luồng chọn ghế của Web; đây là việc tích hợp còn lại (B4.6/B4.9) |
| Đăng nhập admin bị từ chối | Còn mật khẩu mặc định bị đổi ở `SeedAdmin:Password`, hoặc sai email | Xem log dòng `warn` lúc khởi động; `IdentitySeeder` tạo admin theo `SeedAdmin:Email` |
| Bị đá sang `/Account/AccessDenied` | Tài khoản không có vai trò `Admin` | Đăng nhập bằng tài khoản admin, hoặc thêm vai trò cho tài khoản đang dùng |
| Ảnh poster trắng | Thiếu file trong `wwwroot/img/posters/` | Không phải lỗi: view tự dùng `poster-placeholder.svg` |

## 7. Đường dẫn nhanh

| Màn hình | Đường dẫn |
| --- | --- |
| Trang chủ / danh sách phim | `/` |
| Đăng nhập / đăng ký / từ chối truy cập | `/Account/Login` · `/Account/Register` · `/Account/AccessDenied` |
| Sơ đồ ghế của một suất | `/Booking/SeatMap/{showtimeId}` |
| Xác nhận ghế (giỏ trong Session) | `/Booking/Checkout` |
| Khu quản trị | `/Admin/Movies` · `/Admin/Theaters` · `/Admin/Rooms?theaterId={id}` · `/Admin/Showtimes` |
| Đơn và doanh thu | `/Admin/Bookings` · `/Admin/Revenue` · `/Admin/Revenue/Breakdown` |
| Hub giữ ghế (SignalR) | `/hubs/seat` |

## 8. Ảnh chụp

Danh sách ảnh cần chụp, trạng thái dữ liệu và điều kiện "đạt" của từng ảnh:
[`screenshots/CHECKLIST.md`](screenshots/CHECKLIST.md). Lưu ảnh vào `documentation/screenshots/` với
đúng tên file trong checklist.
