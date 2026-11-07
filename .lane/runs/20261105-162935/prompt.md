NHIEM VU: thuc hien brief duoi day.

Toan bo chi thi cua ban nam trong message nay: BRIEF (phan dau) + CONTRACT (phan cuoi).
Khong co chi thi nao khac, va khong can di tim file luat o dau khac tren dia.
Ban goc cua message nay (chi doc, dung sua): D:\Edu\Project\CinemaBooking\.lane\runs\20261105-162935\prompt.md
Lane: agy   |   Project root: D:\Edu\Project\CinemaBooking   |   Run dir: .lane/runs/20261105-162935
Thieu thong tin thi tra "RESULT: blocked" kem cau hoi cu the, dung doan.
# BRIEF

# V3-D — Admin
ĐỌC `docs/lane-briefs/ui-v3-concept.md` và `docs/lane-briefs/COMPONENTS.md`, `wwwroot/css/tokens.css`, `Areas/Admin/Views/_ViewStart.cshtml` + layout admin đang dùng (nếu dùng `_Layout` chung thì tạo `Areas/Admin/Views/Shared/_AdminLayout.cshtml` mới và trỏ `_ViewStart` tới nó).

Viết lại TỪ ĐẦU mọi view trong `Areas/Admin/Views/**` (Bookings, Movies, Revenue, Rooms, Showtimes, Theaters) + `wwwroot/css/admin.css`. Bảng mật độ cao, sidebar trái, bộ lọc gọn, biểu đồ doanh thu bằng CSS/SVG thuần (không thêm thư viện), form phẳng. KHÔNG mạo hiểm; ưu tiên đọc nhanh.

Allowlist: src/CinemaBooking.Web/Areas/Admin/Views/**, wwwroot/css/admin.css.


---

# CONTRACT

# Contract — lane web UI (CinemaBooking)

Bạn là agent headless, KHÔNG có context nào ngoài brief này. Chỉ sửa file khớp allowlist; không chạm `.cs`, `.csproj`, `Program.cs`, migration, test.
Bắt buộc giữ nguyên: mọi `id`, `data-*`, `asp-*`, tên class mà JS dùng (`is-free|is-mine|is-held|is-booked|is-active|is-warning`, `.seat`, `.seat[data-seat-ids]`, `[data-hero-carousel]`, `.hero-slide`, `[data-hero-dot]`), và toàn bộ logic Razor.
Không thêm thư viện npm. Font chỉ qua Google Fonts `<link>` trong `_Layout.cshtml`.
Cuối cùng chạy `dotnet build src/CinemaBooking.Web` và báo kết quả thật.
Báo cáo: `RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED`. `RESULT: done` mà FILES_CHANGED rỗng = bịa.
