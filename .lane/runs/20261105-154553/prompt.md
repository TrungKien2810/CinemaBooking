NHIEM VU: thuc hien brief duoi day.

Toan bo chi thi cua ban nam trong message nay: BRIEF (phan dau) + CONTRACT (phan cuoi).
Khong co chi thi nao khac, va khong can di tim file luat o dau khac tren dia.
Ban goc cua message nay (chi doc, dung sua): D:\Edu\Project\CinemaBooking\.lane\runs\20261105-154553\prompt.md
Lane: agy   |   Project root: D:\Edu\Project\CinemaBooking   |   Run dir: .lane/runs/20261105-154553
Thieu thong tin thi tra "RESULT: blocked" kem cau hoi cu the, dung doan.
# BRIEF

# CinemaBooking UI v2 — vòng 2: phủ nốt các trang còn dùng class cũ

## Bối cảnh (đọc trước, mở file ra xem, đừng đoán)
Vòng 1 đã tạo `src/CinemaBooking.Web/wwwroot/css/tokens.css` (token) và `app.css` (đè hoàn toàn site.css cũ, đã bị xoá). Chỉ 4 view được sửa. Phần còn lại vẫn dùng class cũ mà app.css KHÔNG định nghĩa => trang vỡ. Giữ nguyên ý tưởng "Phòng Chiếu Đêm" và mọi luật motion trong `lane-briefs/brief-ui-v2.md` (đọc file đó).

## Việc phải làm
1. Thêm style (vào app.css hoặc tách `wwwroot/css/pages-*.css` rồi import trong app.css) cho đúng các class đang được view dùng mà chưa có style:
account-card account-email account-name admin-filter admin-group-row admin-poster-preview admin-sidebar-title auth-icon booking-list-main booking-list-payment booking-meta-email booking-movie booking-time checkout-card checkout-heading checkout-hold-timer checkout-payment checkout-payment-option checkout-payment-title checkout-radio checkout-secure city-group error-card error-shell filter-bar filter-label genre-filter movie-genre prose-card prose-heading pwd-strength pwd-strength-bar pwd-strength-label pwd-strength-track resend-email-form room-seat-editor search-summary terms-check ticket-note 
   Dùng `grep -rn "<class>" src/CinemaBooking.Web/Views src/CinemaBooking.Web/Areas` để biết nó nằm ở trang nào rồi style cho hợp ngữ cảnh. Chỉ dùng var(--cb-*), không hex mới.
2. Rà TOÀN BỘ view sau và sửa markup/class nếu cần để khớp thiết kế mới: Views/Booking/SeatMap.cshtml (ghế nhung, màn hình vòm — đảm bảo markup khớp CSS vòng 1), Movies/Details, Branches/Index, Shared/_MovieCard, Booking/{Lookup,MyBookings,_BookingSteps,_CheckoutForm,_BookingDetails}, Account/*, Shared/Error, Areas/Admin/Views/**. Admin: gọn, bảng đọc được, ăn token, KHÔNG làm trò chơi.
3. Form thanh toán / ô nhập / submit / lỗi: phẳng, tương phản cao, target >= 44px, focus rõ (`--cb-focus-ring`).
4. Tự kiểm: chạy lại lệnh liệt kê class thiếu (grep class trong Views/Areas so với app.css) cho tới khi không còn class tự đặt nào thiếu style. Chạy `dotnet build src/CinemaBooking.Web`.

## Ràng buộc
Chỉ sửa trong allowlist; KHÔNG sửa docs/, .govern/, .cs, test. Giữ id, data-*, asp-*, class mà JS dùng. Báo cáo RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED.


---

# CONTRACT

# Contract — lane web UI (CinemaBooking)

Bạn là agent headless, KHÔNG có context nào ngoài brief này. Chỉ sửa file khớp allowlist; không chạm `.cs`, `.csproj`, `Program.cs`, migration, test.
Bắt buộc giữ nguyên: mọi `id`, `data-*`, `asp-*`, tên class mà JS dùng (`is-free|is-mine|is-held|is-booked|is-active|is-warning`, `.seat`, `.seat[data-seat-ids]`, `[data-hero-carousel]`, `.hero-slide`, `[data-hero-dot]`), và toàn bộ logic Razor.
Không thêm thư viện npm. Font chỉ qua Google Fonts `<link>` trong `_Layout.cshtml`.
Cuối cùng chạy `dotnet build src/CinemaBooking.Web` và báo kết quả thật.
Báo cáo: `RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED`. `RESULT: done` mà FILES_CHANGED rỗng = bịa.
