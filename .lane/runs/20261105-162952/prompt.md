NHIEM VU: thuc hien brief duoi day.

Toan bo chi thi cua ban nam trong message nay: BRIEF (phan dau) + CONTRACT (phan cuoi).
Khong co chi thi nao khac, va khong can di tim file luat o dau khac tren dia.
Ban goc cua message nay (chi doc, dung sua): D:\Edu\Project\CinemaBooking\.lane\runs\20261105-162952\prompt.md
Lane: agy   |   Project root: D:\Edu\Project\CinemaBooking   |   Run dir: .lane/runs/20261105-162952
Thieu thong tin thi tra "RESULT: blocked" kem cau hoi cu the, dung doan.
# BRIEF

# V3-B — Luồng đặt vé
ĐỌC `docs/lane-briefs/ui-v3-concept.md` và `docs/lane-briefs/COMPONENTS.md` (do lane A tạo; dùng lại component, không viết trùng), `Views/Shared/_Layout.cshtml` mới, `wwwroot/css/tokens.css`.

Viết lại TỪ ĐẦU: `Views/Booking/SeatMap.cshtml` (+ `wwwroot/css/seatmap.css`; chỉnh `seat-map.js` cho hàng ghế uốn cung + sóng + nảy, giữ SignalR / giữ ghế / đếm giờ), `_BookingSteps`, `Checkout`, `_CheckoutForm`, `Confirmation`, `_BookingDetails`, `Lookup`, `MyBookings`, `BookingNotFound`, `checkout-timer.js` nếu cần. CSS vào `seatmap.css` và `booking.css` (đọc `_Layout` để biết cách link CSS theo trang, vd `@section Styles`).
Cú cược L3 nằm ở SeatMap; Checkout/Confirmation = tấm vé; form thanh toán PHẲNG.

Allowlist: src/CinemaBooking.Web/Views/Booking/**, wwwroot/css/seatmap.css, wwwroot/css/booking.css, wwwroot/js/seat-map.js, wwwroot/js/checkout-timer.js.


---

# CONTRACT

# Contract — lane web UI (CinemaBooking)

Bạn là agent headless, KHÔNG có context nào ngoài brief này. Chỉ sửa file khớp allowlist; không chạm `.cs`, `.csproj`, `Program.cs`, migration, test.
Bắt buộc giữ nguyên: mọi `id`, `data-*`, `asp-*`, tên class mà JS dùng (`is-free|is-mine|is-held|is-booked|is-active|is-warning`, `.seat`, `.seat[data-seat-ids]`, `[data-hero-carousel]`, `.hero-slide`, `[data-hero-dot]`), và toàn bộ logic Razor.
Không thêm thư viện npm. Font chỉ qua Google Fonts `<link>` trong `_Layout.cshtml`.
Cuối cùng chạy `dotnet build src/CinemaBooking.Web` và báo kết quả thật.
Báo cáo: `RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED`. `RESULT: done` mà FILES_CHANGED rỗng = bịa.
