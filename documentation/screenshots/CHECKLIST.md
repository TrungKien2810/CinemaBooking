# Checklist ảnh cho [Hướng dẫn sử dụng](../user-guide.md)

Ảnh trong `documentation/user-guide.md` hiện là **placeholder** — tài liệu chỉ ghi tên file, chưa có
ảnh. Nhóm chụp theo danh sách dưới đây, lưu **đúng tên file** vào thư mục này
(`documentation/screenshots/`), tài liệu không phải sửa gì.

## Chuẩn bị trước khi chụp

1. Merge đủ ba nhánh Dev A + Dev B + Dev C vào `dev`: chưa merge JS SignalR (C3.x) thì sơ đồ ghế
   không có trạng thái giữ ghế, chưa merge B4.x/C4.x thì luồng đặt vé dừng ở trang xác nhận ghế
   (tài liệu đánh dấu ⏳ đúng những chỗ này).
2. `git pull` → `dotnet build`.
3. `.\scripts\reset-demo.ps1` để database về đúng trạng thái seed (xem §1.4 tài liệu).
4. Chạy **cả hai**: `dotnet run --project src/CinemaBooking.Web` và
   `dotnet run --project src/CinemaBooking.Worker`.
5. Đăng nhập admin: `admin@cinemabooking.local` / `Admin@12345`.

**Quy ước ảnh:** PNG, cửa sổ ~1440×900, theme tối (mặc định), ẩn mọi thông tin cá nhân thật. Ảnh
chụp log thì để cửa sổ terminal đủ rộng để dòng log không bị xuống dòng.

## A. Luồng khách hàng

| ☐ | File | Màn hình | Chuẩn bị | Ảnh đạt khi… |
| --- | --- | --- | --- | --- |
| ☐ | `01-trang-chu.png` | `/` | Vừa reset demo | Thấy đủ 5 phim với poster, tên, thể loại, thời lượng |
| ☐ | `02-chi-tiet-phim-suat-chieu.png` | Trang chi tiết một phim | Chọn phim có suất ở cả hai thành phố | Suất gom theo ngày → chi nhánh, thấy ít nhất 2 chi nhánh khác thành phố ở 2 khối giá khác nhau |
| ☐ | `03-chon-ghe-trong.png` | `/Booking/SeatMap/{id}` | Chọn suất **sớm nhất ngày mai của một chi nhánh** (có đơn seed) | Thấy ghế đỏ đã đặt ở hàng A, ghế VIP/đôi khác màu, còn lại trống |
| ☐ | `04-hai-cua-so-giu-ghe.png` | Hai cửa sổ cùng suất, đặt cạnh nhau | Một cửa sổ thường, một ẩn danh | Cửa sổ A ghế đang chọn; cửa sổ B **cùng ghế đó** đã đổi màu "đang có người giữ" |
| ☐ | `05-dem-nguoc-5-phut.png` | Sơ đồ ghế sau khi click ghế | — | Thấy đồng hồ đếm ngược của ghế đang giữ (giá trị còn lại bất kỳ, miễn đọc được) |
| ☐ | `06-thanh-toan.png` | `/Booking/Checkout` | Chọn 2 ghế khác loại (một thường, một VIP) | Danh sách ghế + loại ghế, phim, chi nhánh, phòng, giờ, tổng tiền khớp số ghế |
| ☐ | `07-xac-nhan-dat-ve.png` | Trang xác nhận sau thanh toán | Đặt vé tới bước cuối | Thấy **mã đặt vé** (dạng `CB-…`), suất chiếu, ghế, tổng tiền |
| ☐ | `08-email-trong-log.png` | Cửa sổ Worker | Chờ ≤ 15 giây sau khi đặt vé | Một dòng log nội dung email xác nhận có mã đặt vé |
| ☐ | `09-dang-nhap.png` | `/Account/Login` | — | Form đăng nhập, không có thanh địa chỉ lộ thông tin cá nhân |

Ghi chú cho `04`: nếu chưa merge C3.x, ảnh này **không chụp được** — đừng chụp ảnh giả trạng thái
giữ ghế bằng hai trình duyệt khác nhau không có SignalR; ghi lại là chờ merge.

## B. Luồng quản trị

| ☐ | File | Màn hình | Chuẩn bị | Ảnh đạt khi… |
| --- | --- | --- | --- | --- |
| ☐ | `10-admin-phim.png` | `/Admin/Movies` | — | Danh sách 5 phim + nút thêm/sửa/ẩn |
| ☐ | `11-admin-chi-nhanh-phong.png` | `/Admin/Theaters`, `/Admin/Rooms?theaterId=1` | — | Thấy 4 chi nhánh ở 2 thành phố; ảnh phòng thấy cấu hình hàng × cột và số ghế sinh ra |
| ☐ | `12-admin-suat-chieu.png` | `/Admin/Showtimes` | Lọc theo một chi nhánh và ngày mai | Bảng suất chiếu có phim, phòng, giờ, giá; bộ lọc chi nhánh/ngày hiện rõ |
| ☐ | `13-admin-don.png` | `/Admin/Bookings` | — | Thấy các đơn `CB-SEED…` cùng bộ lọc mã / chi nhánh / trạng thái |
| ☐ | `14-admin-chi-tiet-don.png` | `/Admin/Bookings/Details/{id}` | Mở một đơn seed | Thông tin khách, suất, **bảng ghế kèm giá từng ghế**, trạng thái thanh toán |
| ☐ | ~~`15-admin-huy-don.png`~~ | — | Tính năng hủy đơn đã được bỏ khỏi phạm vi | — |
| ☐ | `16-admin-doanh-thu.png` | `/Admin/Revenue` | Chọn khoảng ngày có đơn (mặc định 7 ngày) | Bảng doanh thu theo ngày × chi nhánh + tổng theo chi nhánh, có số vé |
| ☐ | `17-admin-doanh-thu-chi-tiet.png` | `/Admin/Revenue/Breakdown` | Lọc theo một chi nhánh | Bảng theo phim và bảng theo suất chiếu (số vé / sức chứa, doanh thu) |

## C. Tự nhả ghế và giao diện khác (tùy chọn nhưng nên có)

| ☐ | File | Màn hình | Chuẩn bị | Ảnh đạt khi… |
| --- | --- | --- | --- | --- |
| ☐ | `18-ghe-tu-nha-sau-5-phut.png` | Sơ đồ ghế, sau khi hết hạn giữ | Giữ một ghế rồi đợi hết 5 phút, Worker đang chạy | Ghế về trạng thái trống ở **cả hai** cửa sổ (có thể chụp kèm log Worker) |
| ☐ | `19-trang-thai-rong.png` | Trang chủ hoặc trang đơn khi không có dữ liệu | Lọc ra kết quả rỗng | Khối "trạng thái rỗng" đọc được, không phải màn hình trắng |
| ☐ | `20-dien-thoai.png` | Sơ đồ ghế + bảng admin trên màn hẹp | DevTools ~390×844 | Lưới ghế và bảng cuộn được, không tràn ngang |

## Sau khi chụp

- [ ] Đủ ảnh, đúng tên file, nằm trong `documentation/screenshots/`.
- [ ] Mở `documentation/user-guide.md` xem lại: mỗi dòng `📷` đã có ảnh tương ứng.
- [ ] Xóa mục "Ảnh trong tài liệu này là placeholder" ở đầu `user-guide.md` và ghi chú
      "chờ merge" ở §0 nếu lúc đó đã merge đủ.
- [ ] Commit ảnh: `docs(project): add user guide screenshots`.
