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
