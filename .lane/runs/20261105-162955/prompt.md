NHIEM VU: thuc hien brief duoi day.

Toan bo chi thi cua ban nam trong message nay: BRIEF (phan dau) + CONTRACT (phan cuoi).
Khong co chi thi nao khac, va khong can di tim file luat o dau khac tren dia.
Ban goc cua message nay (chi doc, dung sua): D:\Edu\Project\CinemaBooking\.lane\runs\20261105-162955\prompt.md
Lane: agy   |   Project root: D:\Edu\Project\CinemaBooking   |   Run dir: .lane/runs/20261105-162955
Thieu thong tin thi tra "RESULT: blocked" kem cau hoi cu the, dung doan.
# BRIEF

# V3-C — Tài khoản
ĐỌC `docs/lane-briefs/ui-v3-concept.md` và `docs/lane-briefs/COMPONENTS.md`, `Views/Shared/_Layout.cshtml` mới, `wwwroot/css/tokens.css`.

Viết lại TỪ ĐẦU mọi `Views/Account/*` (Login, Register, AccessDenied, ChangePassword, ForgotPassword, Profile, ResetPassword, ...) + `wwwroot/css/account.css`; chỉnh `auth-ui.js` / `validation-vi.js` nếu cần (giữ hành vi: hiện/ẩn mật khẩu, độ mạnh mật khẩu, validation tiếng Việt). Mỗi trang auth một bố cục riêng có chủ đích (không phải một thẻ giữa màn hình): ví dụ Login = nửa trái là hình khán phòng, nửa phải form phẳng. Form PHẲNG, rõ, lỗi dễ thấy.

Allowlist: src/CinemaBooking.Web/Views/Account/**, wwwroot/css/account.css, wwwroot/js/auth-ui.js, wwwroot/js/validation-vi.js.


---

# CONTRACT

# Contract — lane web UI (CinemaBooking)

Bạn là agent headless, KHÔNG có context nào ngoài brief này. Chỉ sửa file khớp allowlist; không chạm `.cs`, `.csproj`, `Program.cs`, migration, test.
Bắt buộc giữ nguyên: mọi `id`, `data-*`, `asp-*`, tên class mà JS dùng (`is-free|is-mine|is-held|is-booked|is-active|is-warning`, `.seat`, `.seat[data-seat-ids]`, `[data-hero-carousel]`, `.hero-slide`, `[data-hero-dot]`), và toàn bộ logic Razor.
Không thêm thư viện npm. Font chỉ qua Google Fonts `<link>` trong `_Layout.cshtml`.
Cuối cùng chạy `dotnet build src/CinemaBooking.Web` và báo kết quả thật.
Báo cáo: `RESULT / FILES_CHANGED / VERIFY / NOTES / BLOCKED`. `RESULT: done` mà FILES_CHANGED rỗng = bịa.
