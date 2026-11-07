NHIEM VU: thuc hien brief duoi day.

Toan bo chi thi cua ban nam trong message nay: BRIEF (phan dau) + CONTRACT (phan cuoi).
Khong co chi thi nao khac, va khong can di tim file luat o dau khac tren dia.
Ban goc cua message nay (chi doc, dung sua): D:\Edu\Project\CinemaBooking\.lane\runs\20261106-100829\prompt.md
Lane: agy   |   Project root: D:\Edu\Project\CinemaBooking   |   Run dir: .lane/runs/20261106-100829
Thieu thong tin thi tra "RESULT: blocked" kem cau hoi cu the, dung doan.
# BRIEF

# CinemaBooking — motion vòng 2: hiệu ứng suy ra từ nội dung (ui-motion §0 ideation)

## Bối cảnh (mở file ra xem, đừng đoán)
`src/CinemaBooking.Web/wwwroot/css/credits.css` (nạp SAU mọi css; đen + vàng `--cr-gold`). Token motion ở `wwwroot/css/tokens.css` ~dòng 86-102 (`--cb-dur-press/pop/surface/exit/rise`, `--cb-ease-out`, `--cb-ease-in-out`; khối reduced-motion đặt token về 0.01ms). Vòng 1 đã thêm lớp phản hồi (nút/link/ghế/menu/poster) ở cuối file — GIỮ NGUYÊN, đừng sửa lại.

Sản phẩm là **quầy bán vé rạp**: vé in ra, cuống vé có răng cưa, đồng hồ giữ ghế. Giấy phép thử nghiệm (docs/project-state/ui-v3/directions.md) cho phép MỘT hiệu ứng biểu cảm trên bề mặt "vé sau khi phát hành". Bề mặt giao dịch (chọn ghế, thanh toán, đăng nhập) chỉ được phản hồi nhỏ, KHÔNG biểu cảm.

## Ba hàng cần làm (CHỈ sửa `credits.css`, không sửa .cshtml/.js/.cs/tokens.css)

| # | Bề mặt | Trigger → trạng thái | Cỗ máy | Loại | Thời lượng / easing |
|---|---|---|---|---|---|
| 1 | `.cb-confirmation-card` (trang xác nhận vé, `Views/Booking/Confirmation.cshtml`) | tải trang xác nhận → vé "vừa phát hành" | máy in nhả tấm vé từ trên xuống: `clip-path: inset(0 0 100% 0)` → `inset(0)` | sự kiện, chạy 1 lần | 800ms (literal được phép vì token `--cb-dur-signature` = 800ms: dùng `var(--cb-dur-signature)`), `var(--cb-ease-in-out)` |
| 2 | `.cr-stub` trong `Views/Booking/MyBookings.cshtml` | hover cuống vé | cuống dịch trái 4px tại đường răng cưa: `.cr-stub-when` `translateX(-4px)` và `border-right-color` chuyển sang `--cr-gold` | phản hồi | `--cb-dur-pop`, `--cb-ease-out` |
| 3 | `.cb-hold-timer` (đồng hồ giữ ghế ở checkout, class `is-warning` do JS thêm khi còn ≤60s) và `.seat-timer` | còn ≤60s → cảnh báo | chỉ chuyển màu mượt về `#ff9b9b`, KHÔNG lặp, KHÔNG nhấp nháy | phản hồi | `--cb-dur-pop`, `--cb-ease-out` |

### Hàng 1 — chi tiết
- Hiện tại các con của `.cb-confirmation-card` chạy `cb-line` với stagger 260ms (tổng ~1.7s, quá ngưỡng "nội dung thấy trong 1s"). **Thay tại chỗ** bằng hiệu ứng nhả vé: bỏ animation/stagger của các con (`.cb-confirmation-card > *`, các `--i`, keyframes `cb-line` nếu không còn ai dùng — grep trước khi xoá), thay bằng `animation: cb-print var(--cb-dur-signature) var(--cb-ease-in-out) both` trên chính `.cb-confirmation-card`, `@keyframes cb-print { from { clip-path: inset(0 0 100% 0); } to { clip-path: inset(0); } }` (chỉ khai báo `from`/`to` bằng clip-path; KHÔNG đặt `opacity: 0` tĩnh; trạng thái mặc định là vé hiện đủ).
- Không cuộn khoá, không chặn tương tác; nội dung vé vẫn đọc được trong ≤1s.
- Reduced motion: thêm `.cb-confirmation-card` vào danh sách `animation: none` trong khối `@media (prefers-reduced-motion: reduce)` sẵn có (giữ nguyên dạng khối, chỉ thêm selector, bỏ `.cb-confirmation-card > *` nếu hết dùng).

### Hàng 2 — chi tiết
- Trong `@media (hover: hover) and (pointer: fine)`. `.cr-stub-when` cần `transition: transform var(--cb-dur-pop) var(--cb-ease-out), border-right-color var(--cb-dur-pop) var(--cb-ease-out)`.
- Mobile (<= breakpoint đã đặt `border-right: 0` ở `.cr-stub-when`): không dịch chuyển. Tắt `transform` trong reduced-motion (giữ đổi màu).

## Quy tắc chung
- Token có sẵn, KHÔNG literal ms/cubic-bezier mới. CẤM `transition: all`. Chỉ animate `transform, opacity, color, border-color, clip-path` (clip-path chỉ cho hàng 1).
- Không đụng `@keyframes` khác, không format lại rule lân cận, không xoá code có sẵn ngoài chỗ hàng 1 nói rõ. Comment tiếng Việt cùng phong cách: `/* tên: trigger=… · … · token */`.

## Tự kiểm
`dotnet build src/CinemaBooking.Web` xanh; `grep -n "transition: all" src/CinemaBooking.Web/wwwroot/css/credits.css` rỗng.

## Báo cáo
`RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED` + danh sách selector đã đổi + keyframes đã xoá/giữ.


---

# CONTRACT

# Contract — lane web UI (CinemaBooking)

Bạn là agent headless, KHÔNG có context nào ngoài brief này. Chỉ sửa file khớp allowlist; không chạm `.cs`, `.csproj`, `Program.cs`, migration, test.
Bắt buộc giữ nguyên: mọi `id`, `data-*`, `asp-*`, tên class mà JS dùng (`is-free|is-mine|is-held|is-booked|is-active|is-warning`, `.seat`, `.seat[data-seat-ids]`, `[data-hero-carousel]`, `.hero-slide`, `[data-hero-dot]`), và toàn bộ logic Razor.
Không thêm thư viện npm. Font chỉ qua Google Fonts `<link>` trong `_Layout.cshtml`.
Cuối cùng chạy `dotnet build src/CinemaBooking.Web` và báo kết quả thật.
Báo cáo: `RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED`. `RESULT: done` mà FILES_CHANGED rỗng = bịa.
