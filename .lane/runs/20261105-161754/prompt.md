NHIEM VU: thuc hien brief duoi day.

Toan bo chi thi cua ban nam trong message nay: BRIEF (phan dau) + CONTRACT (phan cuoi).
Khong co chi thi nao khac, va khong can di tim file luat o dau khac tren dia.
Ban goc cua message nay (chi doc, dung sua): D:\Edu\Project\CinemaBooking\.lane\runs\20261105-161754\prompt.md
Lane: agy   |   Project root: D:\Edu\Project\CinemaBooking   |   Run dir: .lane/runs/20261105-161754
Thieu thong tin thi tra "RESULT: blocked" kem cau hoi cu the, dung doan.
# BRIEF

# V3-A — Nền tảng + trang công khai (chạy ĐẦU TIÊN)
ĐỌC `docs/lane-briefs/ui-v3-concept.md` trước (bắt buộc, áp dụng toàn bộ), rồi `src/CinemaBooking.Web/wwwroot/css/tokens.css`.

Việc:
1. Xoá `wwwroot/css/app.css`; tạo kiến trúc CSS mới: `base.css` (reset, typography, shell, focus, grain, view-transition), `components.css` (nút, form phẳng, vé, thẻ giờ chiếu, huy hiệu, bảng, thông báo/toast, phân trang, bộ lọc — mọi component dùng chung cho các lane khác, đặt tên rõ ràng, comment đầu mỗi khối), `home.css`, `movie.css`. Thứ tự link trong `_Layout.cshtml`: tokens -> base -> components -> (css trang). Gỡ bootstrap css/js khỏi layout nếu không còn dùng; giữ jQuery + validation unobtrusive nếu form cần.
2. Viết lại từ đầu: `Shared/_Layout.cshtml` (shell dải vé dọc / tab dưới), `_LoginPartial`, `_MovieCard`, `Error`, `Home/Index`, `Home/Privacy`, `Movies/Details`, `Branches/Index`, `wwwroot/js/site.js` (menu, màn nhung 1 lần/phiên, View Transitions), `hero-carousel.js` (giữ hợp đồng selector; có nút dừng).
3. Để các lane sau (B booking, C account, D admin) dùng được: đảm bảo `components.css` có đủ nút, field, ticket, pill giờ chiếu, bảng, alert. Tạo `docs/lane-briefs/COMPONENTS.md` liệt kê tên class + ví dụ HTML 1 dòng cho mỗi component (lane khác sẽ đọc file này).

Allowlist: src/CinemaBooking.Web/wwwroot/css/**, wwwroot/js/**, Views/Shared|Home|Movies|Branches/**, docs/lane-briefs/COMPONENTS.md.


---

# CONTRACT

# Contract — lane web UI (CinemaBooking)

Bạn là agent headless, KHÔNG có context nào ngoài brief này. Chỉ sửa file khớp allowlist; không chạm `.cs`, `.csproj`, `Program.cs`, migration, test.
Bắt buộc giữ nguyên: mọi `id`, `data-*`, `asp-*`, tên class mà JS dùng (`is-free|is-mine|is-held|is-booked|is-active|is-warning`, `.seat`, `.seat[data-seat-ids]`, `[data-hero-carousel]`, `.hero-slide`, `[data-hero-dot]`), và toàn bộ logic Razor.
Không thêm thư viện npm. Font chỉ qua Google Fonts `<link>` trong `_Layout.cshtml`.
Cuối cùng chạy `dotnet build src/CinemaBooking.Web` và báo kết quả thật.
Báo cáo: `RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED`. `RESULT: done` mà FILES_CHANGED rỗng = bịa.
