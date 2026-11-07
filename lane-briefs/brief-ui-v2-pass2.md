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
