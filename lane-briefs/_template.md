# <Tên asset> — <action> (<N> frame)

> Brief mẫu. Copy ra `lane-briefs/brief-<slug>.md` rồi điền. Brief là **toàn bộ** context lane có:
> nó không thấy handoff, không thấy doc map, không thấy governance hook. Viết như giao việc cho
> người mới, đừng viết như nhắc người đã biết.

## Mục tiêu

Sinh <N> frame PNG cho `<tên>/<action>` để <dùng làm gì — identity? walk? FX? icon?>.

## Tạo hình

- Canvas `WxH`, nhân vật cao khoảng `<h>` px, aspect mục tiêu `~<a>`.
- Mô tả: <loài, dáng, trang phục, vũ khí, 3–4 màu chủ đạo>.
- Tham chiếu bắt buộc — **mở ra xem trước khi vẽ** (đừng đoán từ chữ):
  - `<đường dẫn ảnh tham chiếu>` — cùng camera, cùng độ chi tiết.

## Frame

1. <tư thế frame 1 — chân nào trước, tay nào đâu>
2. <frame 2>
3. …

## Output

- Raw (còn nền chroma): `<out>/raw/<action>-<i>.png`.
- Frame đã key + despill: `<out>/<action>-<i>.png` (bắt đầu từ 1) — đây là file `lane qc` phải đo.
- **KHÔNG** ghi vào thư mục production: người duyệt soi bằng mắt + số đo rồi mới đóng vào
  production kèm `pipeline-meta.json`.

## Ràng buộc riêng của brief này

- <ví dụ: giữ nguyên trang phục của reference; không thêm bóng đổ; không đổi hướng nhìn>

## Báo cáo

Theo đúng khuôn cuối `contract.md`: `RESULT / ASSETS / VERIFY / FILES_CHANGED / NOTES / BLOCKED`,
kèm số đo `lane qc` thật.
