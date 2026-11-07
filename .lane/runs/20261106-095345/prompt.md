NHIEM VU: thuc hien brief duoi day.

Toan bo chi thi cua ban nam trong message nay: BRIEF (phan dau) + CONTRACT (phan cuoi).
Khong co chi thi nao khac, va khong can di tim file luat o dau khac tren dia.
Ban goc cua message nay (chi doc, dung sua): D:\Edu\Project\CinemaBooking\.lane\runs\20261106-095345\prompt.md
Lane: agy   |   Project root: D:\Edu\Project\CinemaBooking   |   Run dir: .lane/runs/20261106-095345
Thieu thong tin thi tra "RESULT: blocked" kem cau hoi cu the, dung doan.
# BRIEF

# CinemaBooking — lớp motion phản hồi: nút, link, chip, ô nhập, ghế, menu

## Bối cảnh (mở file ra xem, đừng đoán)
UI hiện tại là hướng "Chữ chạy cuối phim": `src/CinemaBooking.Web/wwwroot/css/credits.css` nạp SAU mọi css, nền đen + vàng nhạt `--cr-gold`. Token motion ĐÃ CÓ ở `wwwroot/css/tokens.css` dòng ~86-102 (`--cb-dur-press` 120ms, `--cb-dur-pop` 180ms, `--cb-dur-surface` 260ms, `--cb-dur-exit` 160ms, `--cb-ease-out`, `--cb-ease-in-out`, `--cb-ease-snap`, và khối `prefers-reduced-motion` đặt các token về 0.01ms).

**Vấn đề:** hầu hết rule `:hover` trong credits.css KHÔNG có `transition` nên màu/viền nhảy tức thì; hầu như không có trạng thái `:active` (nhấn). Người dùng cảm thấy thiếu motion ở nút và hover. Chỉ `.cr-credit-poster img`, `.cr-film-poster img` (border-color) và `.seat-toast` có transition.

## Việc phải làm (CHỈ sửa `credits.css`)
Mỗi dòng: trigger → trạng thái được giải thích → token thời lượng → token easing. Dùng token có sẵn, KHÔNG literal ms/cubic-bezier mới.

| Phần tử | Trigger | Hiệu ứng | Thời lượng / easing |
|---|---|---|---|
| `.cb-btn`, `.cr-chip` (mọi biến thể: primary/outline/secondary/ghost, `.cr-chip-go`) | hover / focus-visible | chuyển `color, background-color, border-color` | `--cb-dur-press` / `--cb-ease-out` |
| cùng nhóm | `:active` (không disabled) | `transform: scale(0.97)` | `--cb-dur-press` / `--cb-ease-out` |
| link: `.cr-link`, `.cr-back`, `.cr-footer a`, `.cr-credit-title a`, `.cr-film-title a`, `.cr-stub-title a`, `.auth-toggle-pwd`, `.cr-slot-meta` | hover | chuyển `color` | `--cb-dur-press` / `--cb-ease-out` |
| `.cr-account .cb-account-trigger`, `.cb-account-item` | hover / mở | chuyển `color, background-color`; nếu menu mở/đóng bằng thuộc tính thì mở bằng opacity + `translateY(-4px)→0` | `--cb-dur-pop` vào, `--cb-dur-exit` ra / `--cb-ease-out` |
| `.cb-input`, `.cr-lookup-input`, select | focus | chuyển `border-color` (giữ focus ring `:focus-visible` hiện có, KHÔNG xoá outline) | `--cb-dur-press` / `--cb-ease-out` |
| `.seat` (ghế tự do) | hover | chuyển `border-color, color, background-color`; `:active` `scale(0.97)` | `--cb-dur-press` / `--cb-ease-out` |
| `.seat.is-mine` / `.is-held` / `.is-booked` | đổi trạng thái | chuyển màu nền/viền (đừng đè `animation` của `.is-popping`/`cb-pop` đang có) | `--cb-dur-pop` / `--cb-ease-out` |
| poster card (`.cr-credit`, `.cr-film`) | hover | giữ đổi viền vàng sẵn có; thêm `img` `transform: scale(1.02)` trong khung (`overflow: hidden` trên wrapper nếu cần, KHÔNG làm layout nhảy) | `--cb-dur-surface` / `--cb-ease-out` |

Quy tắc:
- Liệt kê thuộc tính rõ ràng, **CẤM `transition: all`**. Chỉ animate `transform, opacity, color, background-color, border-color`. KHÔNG animate width/height/top/left/box-shadow.
- Hover-transform chỉ trong `@media (hover: hover) and (pointer: fine)`; `:active` được phép mọi thiết bị.
- Trạng thái nhìn thấy mặc định: không có `opacity: 0` tĩnh nào.
- `:disabled` / `.is-booked` / `.is-held` KHÔNG có hiệu ứng nhấn/hover.
- Reduced motion: không cần khối mới cho transition (token đã về 0.01ms) — nhưng MỌI `transform: scale()` mới (nhấn, zoom poster) phải bị tắt trong `@media (prefers-reduced-motion: reduce)` (giữ đổi màu).
- Nếu rule cũ cùng selector đã có `transition`, sửa tại chỗ thay vì chồng rule mới.
- Đặt khối mới thành MỘT đoạn có comment tiếng Việt cùng phong cách file (`/* tên: trigger=… · … · token */`), không format lại rule lân cận, không xoá code có sẵn.

## KHÔNG làm
- Không thêm hiệu ứng "biểu cảm" mới (xé vé, đóng dấu, pin cuộn…), không thêm thư viện/JS, không đụng `@keyframes` đang có, không sửa `.cshtml`, `.js`, `.cs`, `tokens.css`.
- Không đổi màu/kích thước/layout hiện có.

## Tự kiểm
`dotnet build src/CinemaBooking.Web` phải xanh. `grep -n "transition: all" src/CinemaBooking.Web/wwwroot/css/credits.css` phải rỗng.

## Báo cáo
`RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED` — kèm danh sách selector đã gắn transition.


---

# CONTRACT

# Contract — lane web UI (CinemaBooking)

Bạn là agent headless, KHÔNG có context nào ngoài brief này. Chỉ sửa file khớp allowlist; không chạm `.cs`, `.csproj`, `Program.cs`, migration, test.
Bắt buộc giữ nguyên: mọi `id`, `data-*`, `asp-*`, tên class mà JS dùng (`is-free|is-mine|is-held|is-booked|is-active|is-warning`, `.seat`, `.seat[data-seat-ids]`, `[data-hero-carousel]`, `.hero-slide`, `[data-hero-dot]`), và toàn bộ logic Razor.
Không thêm thư viện npm. Font chỉ qua Google Fonts `<link>` trong `_Layout.cshtml`.
Cuối cùng chạy `dotnet build src/CinemaBooking.Web` và báo kết quả thật.
Báo cáo: `RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED`. `RESULT: done` mà FILES_CHANGED rỗng = bịa.
