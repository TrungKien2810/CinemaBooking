// Ảnh poster hỏng thì thay bằng ảnh mặc định.
//
// Hai điểm cần nhớ:
// - Sự kiện 'error' của <img> KHÔNG bubble, nên phải nghe ở pha capture trên document
//   thay vì gắn onerror vào từng thẻ.
// - Đánh dấu đã thay rồi để nếu chính ảnh mặc định cũng lỗi thì không lặp vô hạn.
document.addEventListener('error', function (event) {
    var img = event.target;
    if (!(img instanceof HTMLImageElement)) return;

    var fallback = img.dataset.posterFallback;
    if (!fallback || img.dataset.posterFallbackApplied) return;

    img.dataset.posterFallbackApplied = '1';
    img.src = fallback;
}, true);
