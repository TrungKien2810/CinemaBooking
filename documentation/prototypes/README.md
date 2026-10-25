# Prototype UI/UX — CinemaBooking.Smart

Sáu hướng thiết kế cho cùng một bộ 4 màn hình (trang chủ · chi tiết phim · chọn ghế · thanh toán), dùng
chung một `CONTENT-SPEC.md` đóng băng. Tài liệu này **không sửa code ứng dụng** — mọi thứ trong
`documentation/prototypes/` là HTML/CSS/JS tĩnh, mở bằng trình duyệt là chạy.

## Xem

Mở `documentation/prototypes/index.html` — hub so sánh 6 hướng (khung xem trang chủ ở 1440px, bảng so
sánh, bảng đo, khuyến nghị hội tụ).

| Hướng | Ý tưởng | Mở |
|---|---|---|
| **A — Marquee** | Rạp phim cổ điển: nền mực, đèn rìn vàng, suất chiếu là **vé xé có đường rách** | `a-marquee/index.html` |
| **B — Signal** | Sản phẩm số tốc độ cao: **“còn bao nhiêu chỗ”** là thứ được nhấn mạnh nhất | `b-signal/index.html` |
| **C — Bảng lịch** | Sáng, thông tin là chính: chọn suất làm **bảng lịch** như nhà ga | `c-schedule/index.html` |
| **D — Địa chỉ** | **Chọn rạp trước, phim sau**: bốn “cửa” chi nhánh mở đầu trang chủ | `d-address/index.html` |
| **E — Buổi tối** | **Suất chiếu là khối thời gian có độ dài**: trục 17:00 → 01:30 | `e-evening/index.html` |
| **F — Sơ đồ** | **Bán vị trí, không bán con số**: sơ đồ ghế là bản vẽ kỹ thuật | `f-plan/index.html` |

Mỗi hướng có 4 màn hình: `home.html` · `movie.html` · `seatmap.html` · `checkout.html`, cùng
`tokens.css` (hệ thiết kế) + `screens.css` + `app.js`.

## Hai vòng, hai câu hỏi khác nhau

- **Vòng 1 (A/B/C)** hỏi *cách trình bày*: cùng một luồng, ba ngôn ngữ thị giác. Kết luận: chọn **B**
  làm nền, nhặt “vé xé” của A và “bảng lịch” của C.
- **Vòng 2 (D/E/F)** hỏi *thứ tự quyết định*: người dùng quyết định cái gì trước — rạp (D), giờ (E),
  hay chỗ ngồi (F). Đây là phép đo công bằng hơn cho câu “gu có tốt hơn không”, vì nó không so hai
  bảng màu mà so hai cách hiểu sản phẩm.

Bối cảnh vòng 2: `research-round2.md` (V1–V5 — những gì A/B/C để lại, đo trên ảnh render).

## Dữ liệu dùng trong prototype

Lấy nguyên vẹn từ `CatalogSeeder.cs`: 5 phim, 4 chi nhánh ở 2 thành phố, 6 phòng, giá vé theo loại ghế
(thường = giá gốc chi nhánh, VIP = ×1,2, đôi = ×2 — `ISeatPricingService`). Poster thật **không có
trong repo** nên cả 6 hướng dựng poster bằng typography, không dùng ảnh hỏng.

## Đã kiểm bằng gì (vòng 2, đo lại toàn bộ)

36 tổ hợp (3 hướng × 4 màn × 3 bề rộng) render bằng Chrome qua ba scanner sẵn có của
`frontend-craft` — không viết harness mới:

```bash
cd D:\Edu\Project\frontend-craft
node scripts/a11y-scan.mjs    <12 URL> --json     # axe-core + focus indicator
node scripts/craft-probe.mjs  <12 URL> --json     # cỡ chữ, bước khoảng cách, vùng chạm, hàng lệch
node scripts/responsive-shots.mjs <12 URL> --out %TEMP%\shots --widths 375,768,1440 --light-only
```

| Kiểm tra | Vòng 1 (A/B/C) | Vòng 2 (D/E/F) |
|---|---|---|
| Tràn ngang / console / squeeze (36 tổ hợp) | 0 / 0 / 0 | **0 / 0 / 0** |
| axe-core: trang có vi phạm | **6 / 12** | **0 / 12** |
| Control thiếu focus indicator | 8 lượt trên 5 trang | **0** |
| Vùng chạm < 44px | 11 control | **0** |
| Ghế đo hộp thật | ~34px (app thật), prototype ≥44 | **44–46px** |
| Trạng thái ghế khác nhau ngoài màu | 3 / 3 | **3 / 3** (viền liền / viền đứt / nét chéo / nền + ✓) |

**Sửa lại một con số của vòng 1:** README cũ ghi “0 lỗi” cho bộ A/B/C. Đó là kết quả của
`scripts/prototype-audit/probe.mjs` (một bộ kiểm hẹp hơn). Chạy lại bằng `a11y-scan` + `craft-probe`
thì bộ A/B/C **không** sạch: `link-name` ở `b-signal/home` (5 link không có tên đọc được), `dlitem` ở
`b-signal/seatmap|checkout`, `color-contrast` ở `a-marquee/movie` và `c-schedule/movie`,
`heading-order` ở `a-marquee/seatmap|checkout`, cộng 8 lượt thiếu focus indicator và 11 vùng chạm
dưới 44px. Vòng 2 là bộ đầu tiên sạch 0/12 ở cả ba scanner.

Đợt này **relay ảnh đã hoạt động**, nên khác vòng 1: ảnh PNG đã được **mở ra nhìn**, không chỉ đọc số.
Ba hướng vòng 2 đều sửa lỗi thị giác tìm được bằng mắt (chữ vỡ trong thẻ ở 1440, thanh dính che lưới
ghế, khối “hết chỗ” bị gạch ngang giờ, ghế thứ 7 rơi vào cột lối đi, poster vỡ trong ô 44px, dải ngày
chồng chữ ở 375).

## Hội tụ (S8)

**Nền: F — Sơ đồ.** Giải K2 bằng *vị trí* thay vì thêm một con số (đúng chỗ B hụt), giải trọn K4/K5
(4 trạng thái khác hình, ghế 46×46), và hệ thị giác của nó rẻ nhất để chuyển sang Razor: một màu nhấn,
hai cỡ kẻ, không ảnh, không đổ bóng.
**Mượn từ D:** “Đang ở cửa nào” + địa chỉ chi nhánh ở đầu tóm tắt đơn.
**Mượn từ E:** cặp “vào 19:15 → ra 22:01” trên từng suất (vá V4).
**Bỏ:** trục buổi tối đầy đủ của E (375 dài 9.338px, 14 bước khoảng cách — bảng giờ làm rẻ hơn) và bảng
cửa 4 ô chiếm trọn màn đầu của D (biến trang chủ thành màn chọn rạp).

Tiêu chí (a) của lab vẫn **chờ người dùng**: xem hub, rồi xếp hạng — có ít nhất một hướng vòng 2 đứng
trên B hay không.

## Mâu thuẫn có sẵn trong `CONTENT-SPEC.md` (chưa sửa — spec đóng băng)

1. **Suất 19:15 ở Nguyễn Huệ.** Mục “Sơ đồ ghế mẫu” bắt buộc `CineSmart Nguyễn Huệ – Phòng 1, suất
   19:15`, nhưng bảng suất của chi nhánh đó chỉ có 09:00 / 12:15 / 15:30 / 18:45 / 22:00. Ba hướng xử
   lý khác nhau: D và F thêm suất 19:15, E giữ nguyên suất seed (18:45) và màn chọn ghế vẫn là mẫu
   19:15. Cách nào cũng đúng một nửa — vòng sau nên sửa bảng suất trong spec.
2. **“CTA mặc định disabled” vs “phải thể hiện đủ 4 trạng thái ghế”.** Không thể vừa có trạng thái
   “bạn đang giữ” ngay lần vẽ đầu, vừa có CTA tắt. Cả 3 hướng chọn: giữ sẵn C3/C4 để đủ 4 trạng thái,
   CTA bật; bấm “Bỏ chọn tất cả” (hoặc để đồng hồ hết 5:00) thì CTA tắt đúng luật “≥1 ghế”.
3. **Dune 166 phút bắt đầu 19:15 thì tan 22:01**, không phải 21:01. E đã sửa theo số học đúng.

## Giới hạn

- Không chạy được hậu trường: không có SignalR thật, không form gửi đi, không đăng nhập. JS chỉ mô
  phỏng: chọn ghế, đồng hồ giữ ghế 5 phút, tổng tiền, validate form, trạng thái hết hạn.
- Font Google tải qua mạng; không có mạng thì rơi về font hệ thống theo `font-family` fallback.
- Ảnh chụp kiểm tra (375 / 768 / 1440) render bằng Chrome rồi xoá sau khi kiểm — repo không giữ PNG.
- **Chưa có người thật dùng**: chưa đo với người dùng, chưa kiểm bàn phím thật / trình đọc màn hình
  thật, chưa thử 200% zoom hay thiết bị thật. `a11y-scan` chỉ bắt được khoảng 30–40% vấn đề a11y.
- Thanh tóm tắt dính đáy ở 375 hiện nằm giữa trang trong ảnh full-page (nó `fixed`/`sticky`) — mở file
  thật thì đúng; đừng đọc ảnh full-page ở vùng đó.
- Chỉ phủ 4 màn phía khách; khu quản trị (A1–A4 trong `research.md`) chưa có prototype.
