# Research vòng 2 — ba hướng MỚI, đo lại trên cùng CONTENT-SPEC

> Ngày: 2026-10-21 · Base: `dev` @ `1c95a08` (sạch — chỉ có `documentation/prototypes/` và
> `scripts/prototype-audit/` là untracked từ vòng 1)
> Vòng 2 của `skills/ui-pipeline/references/direction-lab.md`. Vòng 1 (A/B/C) đã chọn B làm nền.
> Vòng 2 hỏi câu khác: **B có thật sự tốt hơn, hay chỉ tốt hơn A và C?**

Vòng 1 đổi *cách trình bày* (vé xé / nhịp ghế / bảng lịch). Vòng 2 đổi **thứ tự quyết định** —
ba hướng mới bắt đầu từ ba vật thể khác nhau của cùng một nghiệp vụ, thay vì bắt đầu từ danh sách phim.

---

## 1. Cái vòng 1 để lại (đo được, không phải cảm nhận)

Audit vòng 1 (`research.md` §2, mã T1–T5 / K1–K7 / A1–A4) vẫn đúng ở HEAD: `site.css` vẫn không có
`font-family`, ghế vẫn `2.1rem`, vẫn 3 màu ghế ấm gần nhau. Không cần làm lại audit đó.

Cái **vòng 1 chưa trả lời** — đọc ảnh render của A/B/C (đợt này relay ảnh đã hoạt động trở lại, nên
lần đầu tiên có người *nhìn* thay vì chỉ đo):

| # | Vấn đề của bộ A/B/C | Bằng chứng |
|---|---|---|
| V1 | **Cả ba vẫn mở bằng một danh sách phim.** "Quyết định" duy nhất nằm ở màn chọn suất, tức là sau khi người dùng đã cam kết xem phim gì | `a-marquee/home.html`, `b-signal/home.html`, `c-schedule/home.html` |
| V2 | **B trình bày "còn bao nhiêu chỗ" bằng con số, không bằng vị trí.** Người dùng vẫn phải mở sơ đồ ghế mới biết chỗ trống nằm ở đâu — đúng vấn đề K2 mà B tự nhận đã giải | `b-signal/movie.html` (ô suất: giờ + "43 ghế còn trống") |
| V3 | **Card phim của B vỡ chữ ở 1440.** Cột hẹp + giá + thời lượng + số ghế dồn vào một thẻ, chữ xuống dòng giữa cụm ("9 ghế còn / trống", "từ 80.000 đ" rơi xuống) | ảnh render 1440 của `b-signal/home.html` |
| V4 | **Thời lượng phim không xuất hiện ở nơi người ta quyết định.** Người dùng chọn 19:15 mà không biết mấy giờ tan, có kịp chuyến cuối không | cả 3 hướng, màn `movie.html` |
| V5 | **Đồng hồ giữ ghế vẫn là chi tiết phụ.** K6 được "sửa" bằng cách cho hiện sớm hơn, nhưng nó vẫn nhỏ hơn tên phim | `b-signal/seatmap.html` |

Ba hướng dưới đây giải đúng V1–V5, không lặp lại T/K/A.

---

## 2. Pattern ngành (ngắn — chỉ phần vòng 1 chưa dùng)

| Pattern | Mô tả | Vòng 1 đã dùng? |
|---|---|---|
| **Location-first discovery** (Fandango/CGV "near me") | Chi nhánh là bộ lọc gốc; phim là kết quả | Không — A/B/C đều film-first |
| **Session as a time block** (sân khấu, concert, airline) | Suất chiếu là khoảng thời gian có độ dài, không phải một ô giờ | Không — C làm bảng giờ rời |
| **Seating chart as the product** (theatre, stadium, airline) | Vị trí là thứ được bán; con số chỉ là hệ quả | Không — B bán con số, ghế là màn phụ |
| **Technical drawing / wayfinding** | Bản vẽ có kích thước, chú thích, lưới — ngôn ngữ của thứ cần tin | Không |

---

## 3. Ba ứng viên (mô tả bằng **quyết định**, không bằng tính từ)

### D — Địa chỉ (venue-first)

**Quyết định:** người dùng chọn **rạp** trước, phim sau. Toàn bộ trang chủ là một bảng bốn "cửa"
(chi nhánh thật: Nguyễn Huệ, Thảo Điền, Cầu Giấy, Hoàn Kiếm); chọn cửa nào thì phim, suất, giá đều
lọc theo cửa đó và trạng thái ấy đi theo người dùng sang cả 4 màn.
**Giải:** K1 (thẻ phim có giá từ/thời lượng/trạng thái), K3 (đang lọc gì luôn hiện), V1.
**SIGNATURE:** bảng cửa — mỗi chi nhánh là một bậu cửa có địa chỉ, 2 phòng, 3 suất kế tiếp.
**Rủi ro:** 4 chi nhánh chỉ có 2 thành phố → bảng cửa có thể loãng; phải giữ nó dày thông tin.

### E — Buổi tối (time-first)

**Quyết định:** suất chiếu là **một khối thời gian có độ dài**, không phải một ô giờ. Trục
17:00 → 23:30, khối dài đúng bằng phút của phim, ghi "vào 19:15 · ra 21:01".
**Giải:** K2 (còn ghế + tan lúc mấy giờ), K6 (đồng hồ giữ ghế), V4.
**SIGNATURE:** trục buổi tối — chỗ duy nhất được đồ hoạ; phần còn lại chỉ chữ và đường 1px.
**Rủi ro:** trục ngang dễ vỡ ở 375; phải có phiên bản trục dọc thật, không phải "thu nhỏ".

### F — Sơ đồ (position-first, ngôn ngữ bản vẽ)

**Quyết định:** bán **vị trí**, không bán con số. "Còn 12 ghế" được vẽ thành vạch chia độ; sơ đồ ghế
là một bản vẽ kỹ thuật có đường kích thước (8 hàng × 12 cột), chú thích dạng legend, mã ghế mono.
**Giải:** K4 (trạng thái không chỉ bằng màu), K5 (ghế ≥44px), K2 (số ghế thành đại lượng), V2, V3.
**SIGNATURE:** bản vẽ kỹ thuật — chỉ sơ đồ được phép phức tạp; ba màn còn lại mực trên trắng.
**Rủi ro:** dễ thành "bản vẽ cho vui" nếu đường kích thước không mang thông tin thật.

### Vì sao ba hướng này không trùng A/B/C

| | A — Marquee | B — Signal | C — Bảng lịch | D — Địa chỉ | E — Buổi tối | F — Sơ đồ |
|---|---|---|---|---|---|---|
| Bắt đầu từ | cảm giác "đi rạp" | mật độ còn trống | thông tin lịch | **rạp** | **khoảng thời gian** | **vị trí** |
| Đơn vị bán | vé xé | con số | ô giờ | cửa rạp | khối thời gian | ô ghế trên bản vẽ |
| Chỗ đáng nhớ | đường rách | nhịp ghế | bảng giờ | bảng cửa | trục buổi tối | đường kích thước |

---

## 4. Giao kèo so sánh (không đổi)

`CONTENT-SPEC.md` **đóng băng**: 5 phim, 4 chi nhánh, 6 phòng, giá thật, sơ đồ 8×12, đồng hồ 5:00,
4 màn, 3 bề rộng. Không hướng nào được đổi dữ liệu để dễ thắng. Đo bằng ba scanner sẵn có
(`a11y-scan`, `craft-probe`, `responsive-shots`), không viết harness mới.

## 5. Kết quả

Xem `README.md` mục "Vòng 2" và `index.html` (hub đặt bộ D/E/F cạnh A/B/C).
