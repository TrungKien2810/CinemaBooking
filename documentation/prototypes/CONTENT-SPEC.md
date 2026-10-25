# Đặc tả nội dung dùng chung cho 3 prototype

Mọi hướng dùng **cùng dữ liệu demo** (lấy từ `CatalogSeeder.cs`) để so sánh công bằng.
Không bịa tên phim/số liệu khác. Tiếng Việt có dấu đầy đủ.

## Phim (5 phim — đúng thứ tự seed)

| Tên | Thể loại | Thời lượng | Nội dung 1 câu | Ngày chiếu |
|---|---|---|---|---|
| Mắt Biếc | Tình cảm | 117 phút | Chuyện tình đơn phương của Ngạn dành cho Hà Lan, từ làng Đo Đo lên thành phố. | 20/12/2019 |
| Nhà Bà Nữ | Hài | 106 phút | Bi hài kịch gia đình ba thế hệ quanh một quán bánh canh nhỏ. | 22/01/2023 |
| Dune: Part Two | Khoa học viễn tưởng | 166 phút | Paul Atreides liên minh với người Fremen để trả thù những kẻ đã hủy diệt gia tộc mình. | 01/03/2024 |
| Inside Out 2 | Hoạt hình | 96 phút | Riley bước vào tuổi dậy thì và bộ não cô bé đón thêm những cảm xúc mới. | 14/06/2024 |
| Địa Đạo: Mặt Trời Trong Bóng Tối | Chiến tranh | 128 phút | Một đội du kích bám trụ trong hệ thống địa đạo giữa những ngày ác liệt nhất. | 04/04/2025 |

**Poster**: repo **không có file poster** (chỉ `wwwroot/img/poster-placeholder.svg`). Dựng poster bằng
typography: khối chữ tên phim + thể loại trên nền màu riêng cho từng phim. Tuyệt đối **không** trỏ
`src` tới file ảnh không tồn tại.

## Chi nhánh & phòng & giá gốc (đúng seed)

| Chi nhánh | Thành phố | Địa chỉ | Phòng (hàng × cột) | Suất chiếu | Giá gốc |
|---|---|---|---|---|---|
| CineSmart Nguyễn Huệ | TP. Hồ Chí Minh | 72 Nguyễn Huệ, Quận 1 | Phòng 1: 8×12 · Phòng 2: 6×10 | 09:00, 12:15, 15:30, 18:45, 22:00 | 95.000 ₫ |
| CineSmart Thảo Điền | TP. Hồ Chí Minh | 12 Quốc Hương, Thảo Điền | Phòng 1: 7×10 | 10:30, 14:30, 19:00 | 85.000 ₫ |
| CineSmart Cầu Giấy | Hà Nội | 15 Trần Duy Hưng | Phòng A: 10×14 · Phòng B: 5×8 | 09:30, 12:45, 16:00, 19:15, 22:30 | 90.000 ₫ |
| CineSmart Hoàn Kiếm | Hà Nội | 8 Tràng Tiền | Phòng 1: 6×9 | 11:00, 17:00, 20:30 | 80.000 ₫ |

## Loại ghế & giá (theo `ISeatPricingService`)

- Ghế thường = giá gốc chi nhánh (ví dụ Nguyễn Huệ: **95.000 ₫**)
- Ghế VIP = giá gốc × 1,2 (**114.000 ₫**)
- Ghế đôi = giá gốc × 2 (**190.000 ₫**)
- Phân bố theo hàng (`SeatGenerator`): hàng cuối = **đôi**, khoảng giữa (1/3 → 2/3) = **VIP**, còn lại = **thường**.
  Với phòng 8 hàng: **A, B → thường** · **C, D, E → VIP** · **F, G → thường** · **H → đôi**.
- Nhãn ghế: chữ cái hàng + số cột (`A1` … `H12`). Hàng giữa có lối đi (khoảng trống giữa 2 cột).

## Sơ đồ ghế mẫu (màn hình "Chọn ghế")

Dùng **CineSmart Nguyễn Huệ – Phòng 1 (8 hàng × 12 cột)**, suất **19:15 ngày mai**:

- Hàng A, B, C → ghế thường · Hàng D, E, F → VIP · Hàng H (hàng cuối) → đôi
- Trạng thái cần thể hiện đủ 4: **trống**, **bạn đang giữ**, **người khác đang giữ**, **đã đặt**.
  Ví dụ: `C3 C4` đang giữ của bạn · `D7 D8` người khác giữ · `B2 B3 F5 F6 G7` đã đặt.
- Ghế đã đặt phải **tắt hẳn** (không bấm được) và **không chỉ dựa vào màu** — thêm ký hiệu/khác biệt hình dạng.
- Đồng hồ giữ ghế: **5:00** đếm ngược (khóa 5 phút).

## 4 màn hình cho mỗi hướng

1. **`home.html` — Trang chủ**
   - Khối chủ đạo: phim nổi bật (1 phim được nhấn mạnh) + lời dẫn ngắn.
   - Ô tìm kiếm + bộ lọc thể loại (từ 5 thể loại thật).
   - Lưới/băng 5 phim: tên, thể loại, thời lượng, **giá từ**, trạng thái (Đang chiếu / Sắp chiếu).
2. **`movie.html` — Chi tiết phim**
   - Chọn **Dune: Part Two** (phim dài nhất → nhiều suất nhất).
   - Poster + tên + thể loại + thời lượng + mô tả + ngày khởi chiếu.
   - Chọn ngày: hôm nay + 6 ngày sau (7 ngày, đúng `DaysAhead = 7`).
   - Lọc thành phố (TP. Hồ Chí Minh, Hà Nội) và chi nhánh (4 chi nhánh).
   - Danh sách suất theo chi nhánh: giờ, phòng, giá, **số ghế còn trống** (ví dụ "còn 12 ghế", "hết chỗ").
3. **`seatmap.html` — Chọn ghế**
   - Đúng dữ liệu sơ đồ ở trên; 4 trạng thái; chú thích có **giá từng loại ghế**.
   - Tóm tắt: ghế đã chọn, tổng tiền, **đồng hồ đếm ngược 5:00**.
   - CTA "Tiếp tục thanh toán" — **mặc định disabled**, bật khi chọn ≥1 ghế.
   - Có trạng thái **ghế hết hạn** (mô phỏng giữ ghế hết 5 phút → tự bỏ chọn + báo).
4. **`checkout.html` — Thanh toán**
   - Form: Họ và tên*, Email*, Số điện thoại* (bắt buộc, có lỗi validate mẫu khi bỏ trống/sai định dạng),
     phương thức thanh toán (mô phỏng): Ví điện tử / Thẻ quốc tế / Thanh toán tại quầy.
   - Tóm tắt đơn: phim, suất, chi nhánh, phòng, ghế đã chọn, tổng.
   - Trạng thái lỗi server (một banner) và trạng thái "đang xử lý" khi bấm.
   - Link "Quay lại chọn ghế".

## Yêu cầu kỹ thuật chung

- **HTML tĩnh, không build, không phụ thuộc mạng.** Mở bằng `file://` là chạy.
- Một file HTML cho mỗi màn hình + `index.html` của hướng (giới thiệu hướng + link 4 màn hình).
- CSS tách: `tokens.css` (hệ thiết kế: màu, chữ, không gian, bo góc, đổ bóng, chuyển động) + `screens.css` (layout & component). Mọi giá trị trong `screens.css` **phải lấy từ `tokens.css`** — không viết hex lẻ.
- JS tĩnh trong `app.js` (chọn ghế, đồng hồ, tổng tiền, validate form, điều hướng nhẹ giữa 4 màn hình).
- **Tiếng Việt có dấu, file lưu UTF-8.**
- Responsive: 375 (điện thoại) / 768 (tablet) / 1440 (desktop). Không tràn ngang ở 375.
- Không dùng emoji làm icon chính; nếu cần icon thì dùng SVG nội tuyến.
- A11y: `<label>` thật cho mọi input, `button` thật, `:focus-visible` rõ, `aria-live` cho tổng tiền,
  trạng thái ghế **không chỉ dựa vào màu**, mục tiêu chạm ≥ 44px.
- Có `prefers-reduced-motion` (tắt chuyển động).
- Font: dùng Google Fonts **kèm dấu tiếng Việt** (`&subset=latin-ext` hoặc font có hỗ trợ `vietnamese`),
  kèm `font-family` fallback hệ thống để không mạng vẫn đọc được.
- Cấm các mẹo "AI mặc định": eyebrow ALL-CAPS, mũi tên `→` sau chữ nút, mũi tên/meta nối bằng dấu chấm giữa,
  mọi thẻ cùng bo góc + cùng đổ bóng, gradient trang trí vô nghĩa, badge "New"/"Hot" không có nghĩa.
