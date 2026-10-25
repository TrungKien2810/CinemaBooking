# prototype-audit — đo giao diện prototype bằng máy

Hai script kiểm tra thư mục `documentation/prototypes/` (3 hướng thiết kế × 4 màn hình).
Chạy từ **gốc repo**, cần Node 18+ và Google Chrome.

```bash
node scripts/prototype-audit/probe.mjs    # kiểm tra DOM: tràn ngang, label, vùng chạm, link vỡ
node scripts/prototype-audit/visual.mjs   # đo pixel: thang tiêu đề, tỉ lệ accent, dark-mode có repaint
```

`probe.mjs` in từng trang + kết luận `N/N pass`; `visual.mjs` in JSON để xử lý tiếp.
Cả hai tự tìm Chrome ở đường dẫn mặc định — đổi máy thì đặt biến môi trường:

```bash
CHROME_PATH="C:\Program Files\Google\Chrome\Application\chrome.exe" node scripts/prototype-audit/probe.mjs
```

**Vì sao có script này:** relay ảnh trong phiên agent đã dùng để viết prototype bị hỏng, nên
không xem được ảnh render. Các script này đo trực tiếp trên DOM và trên pixel nên vẫn kiểm
được: tràn ngang, ô nhập thiếu `<label>`, vùng chạm dưới 44px, link trỏ tới file không tồn tại,
lỗi console, tỉ lệ tiêu đề/thân chữ, và việc trang có thực sự vẽ lại dưới `prefers-color-scheme: dark`.

**Giới hạn:** đo được hình học và trạng thái, **không** đo được cảm giác thị giác. Chưa có:
đọc bằng trình đọc màn hình thật, bàn phím thật, công cụ đo tương phản chuyên dụng, và người
xem bằng mắt.
