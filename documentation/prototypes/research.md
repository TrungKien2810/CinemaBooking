# Research UI/UX — CinemaBooking.Smart

> Ngày: 2026-10-21 · Mã base: `dev` @ `1c95a08` (sau khi pull 39 commit từ `origin/dev`)
> Phạm vi: **không sửa code ứng dụng**. Tài liệu này + 3 prototype tĩnh để chọn hướng.

---

## 1. Phương pháp

1. Đọc toàn bộ tầng UI hiện tại: `wwwroot/css/site.css` (1410 dòng), `_Layout.cshtml`,
   `_AdminLayout.cshtml`, `_MovieCard.cshtml`, `Home/Index`, `Movies/Details`,
   `Booking/SeatMap|Checkout|Lookup|Confirmation`, `js/seat-map.js`, `js/site.js`.
2. Đối chiếu với đặc tả nghiệp vụ `General.md` §3.1 (khách), §3.2 (quản trị), §8 (giao diện).
3. Lấy nội dung thật từ seed (`CatalogSeeder.cs`) để prototype dùng đúng dữ liệu demo, không dùng
   "Lorem ipsum" hay tên phim bịa.
4. Đối chiếu với các pattern đặt vé quen thuộc ngành (đặt vé rạp & sân khấu, app xem phim):
   discovery dạng poster-rail, chọn suất dạng bảng lịch, seat map + đồng hồ giữ ghế,
   checkout 1 trang có tóm tắt dính, admin dạng bảng + bộ lọc.
5. Chấm theo bộ kiểm "anti-tell" (mẫu giao diện AI hay sinh ra): SaaS-card kit, nền kem + serif +
   terracotta, nền gần đen + acid-green, eyebrow ALL-CAPS, mũi tên nối nút, ba chấm giữa.

> Không có công cụ benchmark tự động trong repo (không có `ai-tells-scan.mjs`, không cài npm
> package — scope guard chặn `package.json`). Các bước verify bằng script phải chạy tay.

---

## 2. Audit hiện trạng

### 2.1 Hệ thống thiết kế

| # | Vấn đề | Bằng chứng | Ảnh hưởng | Mức |
|---|---|---|---|---|
| T1 | **Không có font token, không có type scale.** `site.css` không khai báo `font-family` nào → rơi về system stack của Bootstrap 5.3.3 | `site.css` (grep `font-family` → 0 kết quả); `html { font-size: 14px }` tại `site.css:52` | Ứng dụng trông như "template Bootstrap đổi màu"; không có nhịp thị giác, tiêu đề/nội dung không lên xuống rõ | Cao |
| T2 | **~40 `font-size` hardcode** rải rác (`.page-title 1.75rem`, `.movie-title 1.0625rem`, `.seat 0.6rem`, `.admin-table thead th 0.8rem`…) | `site.css:263, 344, 359, 521, 825, 1397` | Không sửa được nhất quán; mọi màn hình mới sẽ lệch | Cao |
| T3 | **Accent đơn `#e11d48` + 3 màu ghế ấm** (`#475569` standard, `#d97706` vip, `#db2777` couple) | `site.css:20-33` | Bảng màu nóng lệch tinh thần "rạp" và khó đọc khi ghế đứng cạnh nhau | Trung bình |
| T4 | **Không có token khoảng cách / bo góc / đổ bóng.** `--cb-radius 0.625rem` lẫn lộn với các giá trị viết tay | `site.css:35-38` | Nhịp không đều giữa các trang | Trung bình |
| T5 | **Container để mặc định Bootstrap** (540/720/960/1140/1320) | không override `.container` trong `site.css` | Trang rộng 1320px → nội dung giãn, khó quét | Thấp |

### 2.2 Màn hình khách

| # | Vấn đề | Bằng chứng | Ảnh hưởng | Mức |
|---|---|---|---|---|
| K1 | **Card phim quá cơ bản**: poster + tên + thể loại + ngày. Không thời lượng nổi, không đánh giá, không "giá từ", không badge trạng thái | `_MovieCard.cshtml:8-27` | Không quét nhanh được "phim nào đáng xem, giá bao nhiêu" | Cao |
| K2 | **Chọn suất thiếu thông tin quyết định**: slot chỉ có giờ + phòng + giá. Không có số ghế còn trống (real-time), không phân biệt khung giờ | `Movies/Details.cshtml:101-109` | Người dùng phải click vào mới biết còn chỗ hay không → thêm một vòng | Cao |
| K3 | **Bộ lọc ngày/thành phố/chi nhánh là link thuần** trong 2 hàng chip | `Movies/Details.cshtml:49-68` | Không có cảm giác "đang lọc theo gì", không đếm số kết quả | Trung bình |
| K4 | **Seat map không phân biệt trạng thái bằng ngoài màu**: "người khác giữ" `#8a5a12` vs "đã đặt" `#3a2227` — hai nâu rất gần; `aria-pressed` lại dùng cho cả ghế đã đặt | `SeatMap.cshtml:40-50`; `site.css:451-454` | Vi phạm WCAG 1.4.1 (dùng màu một mình); người mù màu không đọc được sơ đồ | Cao |
| K5 | **Touch target ghế 2.1rem ≈ 33,6px** < 44px khuyến nghị | `site.css:455` | Bấm nhầm ghế trên điện thoại | Cao |
| K6 | **Đồng hồ giữ ghế bị ẩn** (`hidden`) cho tới khi có ghế, và là text nhỏ | `SeatMap.cshtml:68` | Áp lực thời gian không được nhấn mạnh đúng lúc | Trung bình |
| K7 | **Checkout**: form + tóm tắt dạng bảng Bootstrap, không có trạng thái rỗng/đang gửi/lỗi server rõ ràng | `Booking/Checkout.cshtml:17-48` | Bỏ cuộc ở bước cuối là bước mất tiền nhiều nhất | Cao |

### 2.3 Khu quản trị

| # | Vấn đề | Bằng chứng | Ảnh hưởng | Mức |
|---|---|---|---|---|
| A1 | **Hai guống giao diện trong cùng khu**: `Movies`/`Theaters`/`Rooms` dùng token custom (`admin-page-head`, `cb-card`, `status-badge`) còn `Showtimes`/`Bookings`/`Revenue` dùng **Bootstrap thô** (`table`, `dl.row`, `badge bg-secondary`) | `Areas/Admin/Views/Showtimes/Index.cshtml`, `Bookings/Index.cshtml`, `Revenue/Index.cshtml` | Quản trị trông như gộp 2 sản phẩm; không đồng bộ với khu khách | Cao |
| A2 | **Admin vẫn giữ navbar + footer của trang công cộng** (layout lồng nhau) rồi mới có sidebar | `Areas/Admin/Views/Shared/_AdminLayout.cshtml:4` | Hai tầng điều hướng cùng lúc — rối | Trung bình |
| A3 | Bảng dữ liệu dày, **không có bộ lọc/tìm kiếm phân trang**, không có trạng thái rỗng riêng | `Bookings/Index.cshtml`, `Revenue/Index.cshtml` | Admin không lọc được theo trạng thái/chi nhánh | Trung bình |
| A4 | Không có phân tách "chính / phụ" rõ ràng trong bảng: hành động nguy hiểm (hủy đơn) nằm cạnh nút thường | `Bookings/Details.cshtml` | Dễ bấm nhầm | Thấp |

### 2.4 Điểm mạnh nên giữ

- Nền tảng token `cb-*` đã có (màu, radius, shadow) → **mở rộng được**, không phải làm lại từ đầu.
- Sơ đồ ghế đã dựng bằng CSS Grid với `data-*` cho JS → dễ nâng cấp giao diện mà không đụng backend.
- Đã có `validation-vi.js` + `VietnameseModelBindingMessages` → thông báo lỗi tiếng Việt là có sẵn.
- 4 trạng thái ghế đã phân biệt trong DOM (`is-free` / `is-booked` + JS `is-mine` / `is-held`) → chỉ cần thêm **ký hiệu khác màu**, không cần đổi mô hình.

---

## 3. Pattern ngành tham chiếu

| Pattern | Mô tả | Áp cho dự án ở đâu |
|---|---|---|
| **Poster-rail discovery** | Trang chủ dạng trượt ngang theo "đang chiếu / sắp chiếu", poster to, thông tin phụ gọn | K1 — trang chủ |
| **Timetable picker** | Chọn suất như bảng lịch: cột giờ + nhóm chi nhánh, số ghế còn trống hiện ngay | K2, K3 — chi tiết phim |
| **Seat map + hold timer** | Vòng đồng hồ đếm ngược, ghế có ký hiệu (✓ / × / 〰), thanh tóm tắt dính đáy màn hình | K4, K5, K6 — chọn ghế |
| **One-page checkout** | Form trái, tóm tắt đơn dính phải, trạng thái rỗng/lỗi/đang gửi rõ | K7 — thanh toán |
| **Admin = bảng + bộ lọc** | Filter chips trên đầu bảng, empty state riêng, hành động phân cấp | A1, A3 — quản trị |

---

## 4. Ba hướng thiết kế (3 prototype)

Cùng nội dung, cùng 4 màn hình (trang chủ / chi tiết phim / chọn ghế / thanh toán), khác nhau ở
**hệ màu, kiểu chữ, bố cục, và một quyết định đặc trưng** — để so sánh được thay vì so "đẹp hơn".

| | **A — Marquee** | **B — Signal** | **C — Bảng lịch** |
|---|---|---|---|
| Ý tưởng | Rạp phim cổ điển: hành lang đèn rìn, vé xé có rách | Sản phẩm số tốc độ cao, nhấn mạnh "còn bao nhiêu chỗ" | Lịch chiếu như bảng lịch nhà ga — thông tin là chính |
| Nền | Mực sâu `#0B0E13` | Than `#0D0F14` | Giấy lạnh `#F7F7F5` |
| Nhấn | Vàng marquee `#E3B23C` | Xanh điện `#3B6BFF` + hổ phách báo hiếm `#FFB020` | Cobalt mực `#1F3F8F` |
| Chữ tiêu đề | Bungee (chữ biển hiệu) | Chivo | Bodoni Moda |
| Chữ nội dung | Be Vietnam Pro | Manrope | IBM Plex Sans (số chữ tabular) |
| Quyết định đặc trưng | **Suất chiếu render thành vé xé** có đường rách, ghế nằm trong khung vé | **Seat pulse** — mỗi ghế có "nhịp" số ghế trống theo hàng, thanh hành động dính đáy | **Bảng lịch** — cột giờ dọc, nhóm theo chi nhánh, số ghế còn trống hiện ngay |
| Hợp ai | Khách đi xem phim cuối tuần, muốn cảm giác "đi rạp" | Nhóm bán vé online, cần tốc độ và thông tin tức thì | Người đặt vé qua điện thoại, thích so sánh nhanh |

Mỗi hướng nằm ở `documentation/prototypes/<hướng>/`, mở `index.html` của từng hướng để xem.

---

## 5. Khuyến nghị

**Nền để triển khai thật: B — Signal**, vì nó giữ được 3 thứ đang làm tốt (nền tối, accent nóng,
Bootstrap sẵn có) nhưng sửa đúng các điểm K1–K7 và A1, và chữ/ngăn cách bảng vẫn tiện khi
chuyển sang Bootstrap component.

**Lấy từ A:** cách render suất chiếu như vé xé (rất hợp "rạp", giá trị cảm xúc cao).
**Lấy từ C:** bảng lịch chọn suất + số ghế còn trống hiện ngay (giải quyết K2 triệt để).

### Thứ tự triển khai sau khi chọn (chưa làm trong PR này)

1. `wwwroot/css/tokens.css` — tách token ra file riêng: màu, chữ, không gian, bo góc, đổ bóng, chuyển động.
2. Thêm `font-family` + `type scale` có token; chọn font có dấu tiếng Việt đầy đủ.
3. Sửa ghế: ≥44px, thêm **ký hiệu** cho 4 trạng thái (không chỉ màu), giá theo loại ghế trong chú thích.
4. Lộ đồng hồ giữ ghế ngay khi vào trang; thanh tóm tắt dính đáy trên mobile.
5. Chọn suất: dạng bảng lịch + số ghế còn trống.
6. Checkout: trạng thái rỗng / đang gửi / lỗi, tóm tắt dính.
7. Admin: đưa `Showtimes`/`Bookings`/`Revenue` về cùng bộ class với `Movies`/`Theaters`/`Rooms`.
8. Vừa xong → thêm trạng thái rỗng + skeleton + focus-visible token toàn cục.

---

## 6. Ghi chú về prototype

- Là **HTML/CSS/JS tĩnh** trong `documentation/prototypes/` — không chạm vào `src/`, không cần build,
  mở bằng trình duyệt là thấy.
- Nội dung lấy từ seed thật: 5 phim, 4 chi nhánh, 4 phòng, giá vé theo loại ghế
  (thường = giá gốc, VIP = ×1,2, đôi = ×2 — `ISeatPricingService`).
- Poster thật **không có trong repo** (chỉ có `poster-placeholder.svg`) → prototype dựng poster
  bằng typography, không dùng ảnh hỏng.
- Đã render kiểm bằng Chrome headless ở 375 / 768 / 1440 px; xem `documentation/prototypes/README.md`.
