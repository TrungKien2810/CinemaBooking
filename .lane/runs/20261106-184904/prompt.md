NHIEM VU: thuc hien brief duoi day.

Toan bo chi thi cua ban nam trong message nay: BRIEF (phan dau) + CONTRACT (phan cuoi).
Khong co chi thi nao khac, va khong can di tim file luat o dau khac tren dia.
Ban goc cua message nay (chi doc, dung sua): D:\Edu\Project\CinemaBooking\.lane\runs\20261106-184904\prompt.md
Lane: agy   |   Project root: D:\Edu\Project\CinemaBooking   |   Run dir: .lane/runs/20261106-184904
Thieu thong tin thi tra "RESULT: blocked" kem cau hoi cu the, dung doan.
# BRIEF

# CinemaBooking — motion vòng 3: bổ sung hiệu ứng còn thiếu (nút, trạng thái, bước đặt vé, vé)

## Bối cảnh (mở file ra đọc, đừng đoán)
Site rạp phim, UI hướng "Chữ chạy cuối phim": nền đen, vàng nhạt `--cr-gold`, token ở `src/CinemaBooking.Web/wwwroot/css/tokens.css` (`--cb-dur-press|pop|surface|exit|signature`, `--cb-ease-out|in-out|snap`, `--cb-stagger`). Style chính: `wwwroot/css/credits.css` (nạp sau cùng). Đã có sẵn lớp motion ở `credits.css` dòng ~422–576 ("Lớp motion phản hồi", "Lớp motion vòng 2") + cb-track, cb-rise, cb-seat-in, cb-pop, cb-print. ĐỌC chúng trước, KHÔNG làm lại, KHÔNG sửa hành vi cũ. Admin có layout riêng (`Areas/Admin/Views/Shared/_AdminLayout.cshtml`, `wwwroot/css/admin.css`).

Người dùng thấy motion còn "chưa đủ". Thêm các hiệu ứng dưới đây, mỗi cái là một dòng: trigger → trạng thái nó giải thích → token thời lượng → token easing.

## Danh sách hiệu ứng (làm đúng từng dòng, không thêm)
| # | Bề mặt | Trigger → trạng thái | Cách làm | Token |
|---|---|---|---|---|
| 1 | Thanh nav `.cr-bar` | cuộn quá 8px → "đã rời đầu trang" | JS nhỏ trong `site.js` bật class `is-scrolled` (passive scroll listener, rAF); CSS: viền dưới sáng lên + nền đặc hơn, chỉ transition màu | `--cb-dur-pop`, ease-out |
| 2 | Link chữ (`.cr-link`, link footer, `.cr-back`) | hover/focus-visible | gạch chân vẽ dần bằng `background-size` hoặc `transform: scaleX` từ trái, không đổi layout | `--cb-dur-pop`, ease-out |
| 3 | Poster (`.cr-credit-poster`, `.cr-film-poster`, `_MovieCard`) | hover (chỉ `(hover:hover) and (pointer:fine)`) → "đèn máy chiếu quét qua" | pseudo-element `::after` ánh vàng mờ trượt ngang qua poster 1 lần (translateX, opacity), `overflow:hidden` ở khung; không đụng zoom 1.02 sẵn có | `calc(var(--cb-dur-surface) * 2)`, ease-out |
| 4 | Nút gửi form (`.cb-btn-primary` trong `<form>`: đăng nhập, đăng ký, thanh toán, tra cứu) | submit hợp lệ → "đang xử lý" | JS trong `site.js`: trên sự kiện `submit` (chỉ khi form hợp lệ, `!event.defaultPrevented`) thêm `is-loading` + `aria-busy="true"` cho nút submit; CSS: vệt sáng chạy ngang nút bằng gradient + transform, con trỏ `progress`. KHÔNG disable nút trước khi form gửi đi (sẽ mất giá trị submit); chống bấm đúp bằng chặn thêm 1 submit nếu đã `is-loading` | `--cb-dur-surface`, vòng lặp tới khi trang chuyển |
| 5 | Lỗi validate (`.field-validation-error`, `.input-validation-error`, `.validation-summary-errors`) | lỗi xuất hiện | fade + trượt xuống 4px, KHÔNG rung lắc (bề mặt giao dịch) | `--cb-dur-pop`, ease-out |
| 6 | Bước đặt vé `_BookingSteps` (`.cb-step`, `.is-done`, `.is-active`) | bước → xong/đang | số bước chuyển sang ✓ bằng pop nhỏ (scale 0.9→1, không overshoot lớn), đường nối giữa các bước tô dần bằng `scaleX` khi `is-done` | `--cb-dur-pop`, ease-out |
| 7 | Tổng tiền / số ghế đã chọn trong `seat-map.js` (tìm phần tử hiển thị tổng) | giá trị đổi | JS: khi text đổi, gỡ rồi thêm lại class `is-bumped` (gỡ bằng `animationend`); CSS: màu vàng flash + translateY(-2px) rồi về. TUYỆT ĐỐI không đổi logic chọn/giữ ghế/SignalR | `--cb-dur-pop`, ease-out |
| 8 | Xác nhận vé (`Booking/Confirmation`) | vé vừa phát hành → "đóng dấu ĐÃ XUẤT VÉ" | sau khi `cb-print` chạy xong (delay ~`calc(var(--cb-dur-signature) * 0.7)`), huy hiệu trạng thái/`.cb-badge` đầu trang thả xuống: scale 1.3→1 + opacity, overshoot cho phép (sự kiện hiếm), chạy 1 lần | tới 600ms, `--cb-ease-snap` |
| 9 | Bảng & menu Admin (`admin.css`) | hover hàng bảng, link sidebar, nút | CHỈ chuyển màu nền/chữ/viền, nút `:active` scale(0.97). Admin là bề mặt cấp 1: KHÔNG entrance, KHÔNG scroll reveal | `--cb-dur-press`, ease-out |

## Luật bắt buộc (không có ngoại lệ)
- Mọi thời lượng/easing qua token `var(--cb-*)`; không literal ms, không `transition: all`, không animate `width/height/top/left/margin`. Chỉ `transform`, `opacity`, màu (và `clip-path` nếu cần).
- Mặc định phải THẤY nội dung: không đặt `opacity:0` tĩnh trong CSS cho phần tử chưa được script bật. Trạng thái "trước" chỉ nằm trong `@keyframes from` hoặc class do JS thêm.
- Mọi hiệu ứng có travel/scale phải có nhánh `@media (prefers-reduced-motion: reduce)` (bỏ travel/scale, giữ đổi màu/opacity); vòng lặp (#4) thì chuyển thành màu tĩnh/viền sáng.
- Không flash >3 lần/giây; không `preventDefault` wheel; không scroll hijack; target >= 44px giữ nguyên.
- Giữ nguyên `id`, `data-*`, `asp-*`, class JS dùng (`is-free|is-mine|is-held|is-booked|is-active|is-warning`, `.seat`, `.seat[data-seat-ids]`, `[data-hero-carousel]`...). JS mới: vanilla, trong `site.js` (hoặc thêm đúng vài dòng vào `seat-map.js` cho #7), bọc kiểm tra phần tử tồn tại. Không thêm thư viện.
- CSS mới của vòng này gom vào MỘT khối cuối `credits.css` có tiêu đề `Lớp motion vòng 3`, mỗi quy tắc có comment 1 dòng: trigger · trạng thái · token · easing (đúng kiểu vòng 1–2). Riêng #9 vào `admin.css`.
- Chỉ sửa trong allowlist; KHÔNG sửa `docs/`, `.govern/`, `.cs`, test, `.csproj`. Không "tiện tay" sửa format/code lân cận.

## Tự kiểm
1. `grep -n "transition: *all" src/CinemaBooking.Web/wwwroot/css/*.css` phải rỗng.
2. `opacity: 0` trong khối vòng 3 chỉ được nằm trong `@keyframes`.
3. `dotnet build src/CinemaBooking.Web` và báo kết quả thật.
4. Liệt kê mỗi hiệu ứng #1–#9: file + dòng đã làm; hiệu ứng nào bỏ thì ghi lý do.

Báo cáo: `RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED`. `RESULT: done` mà FILES_CHANGED rỗng = bịa.


---

# CONTRACT

# Contract — lane web UI (CinemaBooking)

Bạn là agent headless, KHÔNG có context nào ngoài brief này. Chỉ sửa file khớp allowlist; không chạm `.cs`, `.csproj`, `Program.cs`, migration, test.
Bắt buộc giữ nguyên: mọi `id`, `data-*`, `asp-*`, tên class mà JS dùng (`is-free|is-mine|is-held|is-booked|is-active|is-warning`, `.seat`, `.seat[data-seat-ids]`, `[data-hero-carousel]`, `.hero-slide`, `[data-hero-dot]`), và toàn bộ logic Razor.
Không thêm thư viện npm. Font chỉ qua Google Fonts `<link>` trong `_Layout.cshtml`.
Cuối cùng chạy `dotnet build src/CinemaBooking.Web` và báo kết quả thật.
Báo cáo: `RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED`. `RESULT: done` mà FILES_CHANGED rỗng = bịa.
