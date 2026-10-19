// Ảnh poster hỏng thì thay bằng ảnh mặc định.
//
// Ba điểm cần nhớ:
// - Sự kiện 'error' của <img> KHÔNG bubble, nên phải nghe ở pha capture trên document
//   thay vì gắn onerror vào từng thẻ.
// - File này nạp ở cuối <body>, mà ảnh đã bắt đầu tải từ lúc trình duyệt parse HTML.
//   Ảnh lỗi NHANH sẽ bắn 'error' trước khi listener kịp gắn, nên phải quét lại một lượt
//   lúc DOMContentLoaded — nếu không thì poster 404 hiện ra icon ảnh vỡ.
// - Đánh dấu đã thay rồi để nếu chính ảnh mặc định cũng lỗi thì không lặp vô hạn.

function applyPosterFallback(img) {
    var fallback = img.dataset.posterFallback;
    if (!fallback || img.dataset.posterFallbackApplied) return;

    img.dataset.posterFallbackApplied = '1';
    img.src = fallback;
}

document.addEventListener('error', function (event) {
    var img = event.target;
    if (img instanceof HTMLImageElement) applyPosterFallback(img);
}, true);

document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('img[data-poster-fallback]').forEach(function (img) {
        // complete = trình duyệt đã xử lý xong ảnh; naturalWidth = 0 nghĩa là tải thất bại.
        if (img.complete && img.naturalWidth === 0) applyPosterFallback(img);
    });
});

// Form có data-confirm: hỏi lại trước khi submit (ẩn phim, đóng chi nhánh, xóa suất…).
// Thay cho onsubmit="return confirm('…')" viết tay: nội dung câu hỏi chứa tên phim/chi nhánh
// do admin nhập, nhét vào chuỗi JS inline thì một dấu nháy đơn là vỡ cú pháp. Để trong
// attribute thường thì Razor encode an toàn, đọc lại bằng dataset là ra đúng chữ.
document.addEventListener('submit', function (event) {
    var form = event.target;
    if (!(form instanceof HTMLFormElement)) return;

    var message = form.dataset.confirm;
    if (message && !window.confirm(message)) event.preventDefault();
});
