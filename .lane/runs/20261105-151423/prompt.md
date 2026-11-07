NHIEM VU: thuc hien brief duoi day.

Toan bo chi thi cua ban nam trong message nay: BRIEF (phan dau) + CONTRACT (phan cuoi).
Khong co chi thi nao khac, va khong can di tim file luat o dau khac tren dia.
Ban goc cua message nay (chi doc, dung sua): D:\Edu\Project\CinemaBooking\.lane\runs\20261105-151423\prompt.md
Lane: codex   |   Project root: D:\Edu\Project\CinemaBooking   |   Run dir: .lane/runs/20261105-151423
Thieu thong tin thi tra "RESULT: blocked" kem cau hoi cu the, dung doan.
# BRIEF

# CinemaBooking UI v2 "Phòng Chiếu Đêm" — thay hoàn toàn giao diện cũ

## Mục tiêu
Bỏ HOÀN TOÀN giao diện cũ (đỏ-hồng #e11d48 trên nền xám-đen). Thay bằng hệ thiết kế mới, càng độc đáo càng tốt.
Nguồn sự thật token: `src/CinemaBooking.Web/wwwroot/css/tokens.css` (ĐỌC TRƯỚC, mở ra xem, đừng đoán). Mọi màu/chữ/bo góc/thời lượng phải lấy từ `var(--cb-*)`; không hardcode hex mới ngoài tokens.css.

## Việc phải làm
1. Xoá `wwwroot/css/site.css` cũ và `ui-refresh.css`; viết `wwwroot/css/app.css` mới (import tokens.css trước). Cập nhật `_Layout.cshtml`: bootstrap -> tokens.css -> app.css, thêm Google Fonts `Unbounded` + `Be Vietnam Pro` (subset vietnamese). Bootstrap vẫn giữ làm lưới, ghi đè qua biến `--bs-*`.
2. Tái thiết kế mọi trang: Home (hero carousel), Movies/Details, Branches, Booking (SeatMap, Checkout, Confirmation, Lookup, MyBookings, _BookingSteps), Account/*, Shared (_Layout, _MovieCard, Error), và Admin views (chỉ cần ăn token, bố cục gọn).
3. Nhân vật chính (mạo hiểm tối đa ở SeatMap): 
   - Ghế = hình ghế nhung (đầu tròn `--cb-radius-seat`, tay vịn bằng pseudo-element), KHÔNG phải ô vuông. Ghế VIP viền vàng đồng; ghế đôi là một khối liền hình sofa hồng.
   - Trạng thái không chỉ dựa vào màu: held = sọc chéo, booked = ghế xám có dấu ✕ nhỏ, mine = hổ phách + phát sáng.
   - Màn hình = vòm cong phát sáng xanh lơ (`--cb-beam`) phía trên; một chùm sáng chiếu quét chậm (`--cb-beam-sweep`) lên sơ đồ ghế (pointer-events:none).
   - Vé: phần tóm tắt/Checkout/Confirmation là một tấm vé giấy có khấc tròn hai bên (mask radial-gradient `--cb-ticket-notch`) và đường răng cưa; mã vé dùng `--cb-font-ticket`, tabular-nums.
4. Animation (cho phép mạo hiểm, nhưng mọi thứ phải gói trong `@media (prefers-reduced-motion: no-preference)` hoặc dùng các token dur đã về 0):
   - Vào trang: màn nhung hai cánh mở ra (clip-path/transform, `--cb-dur-curtain`) một lần mỗi phiên (sessionStorage), không chặn nội dung hay tab/focus.
   - Ghế: xuất hiện theo sóng từ giữa ra (stagger bằng `--cb-stagger` qua biến `--i` đặt bởi JS hoặc `:nth-child`), chọn ghế = nảy `--cb-ease-snap` + vòng sóng lan ra, bỏ chọn = lún nhẹ.
   - Hạt phim (grain) phủ toàn trang bằng SVG feTurbulence data-URI, opacity `--cb-grain-opacity`; hero có hiệu ứng nhấp nháy phim rất nhẹ.
   - Chuyển trang bằng View Transitions API (`@view-transition { navigation: auto; }`) với fallback im lặng; scroll-driven animation (`animation-timeline: view()`) cho thẻ phim, có `@supports`.
   - Đồng hồ giữ ghế (`.seat-timer.is-warning`): đập nhịp hồng `--cb-curtain`.
   Mỗi hiệu ứng chỉ animate `transform`/`opacity`/`clip-path`, không layout thrash; `will-change` chỉ khi cần.
5. Chất lượng: tương phản chữ >= 4.5:1, focus ring `--cb-focus-ring` thấy rõ, bàn phím chọn ghế được, responsive tới 360px, không cuộn ngang trang (sơ đồ ghế được cuộn ngang riêng).
6. Tránh các dấu hiệu "AI-generated": không eyebrow viết hoa rải khắp nơi, không chuỗi `A · B · C` thừa, không mọi thứ cùng một bo góc/cùng một bóng xám, không gradient trang trí vô nghĩa.

## Ràng buộc
- Chỉ sửa file trong allowlist; không đụng C#, test, migration.
- JS: chỉ được thêm/chỉnh hành vi trình bày (class, biến CSS `--i`); không đổi luồng SignalR/gọi hub.

## Báo cáo
Theo khuôn trong contract: RESULT / FILES_CHANGED / VERIFY (kết quả `dotnet build` thật) / NOTES / BLOCKED.


---

# CONTRACT

# Contract — lane web UI (CinemaBooking)

Bạn là agent headless, KHÔNG có context nào ngoài brief này. Chỉ sửa file khớp allowlist; không chạm `.cs`, `.csproj`, `Program.cs`, migration, test.
Bắt buộc giữ nguyên: mọi `id`, `data-*`, `asp-*`, tên class mà JS dùng (`is-free|is-mine|is-held|is-booked|is-active|is-warning`, `.seat`, `.seat[data-seat-ids]`, `[data-hero-carousel]`, `.hero-slide`, `[data-hero-dot]`), và toàn bộ logic Razor.
Không thêm thư viện npm. Font chỉ qua Google Fonts `<link>` trong `_Layout.cshtml`.
Cuối cùng chạy `dotnet build src/CinemaBooking.Web` và báo kết quả thật.
Báo cáo: `RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED`. `RESULT: done` mà FILES_CHANGED rỗng = bịa.
