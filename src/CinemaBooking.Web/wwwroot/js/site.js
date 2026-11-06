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

// Form (hoặc nút submit) có data-confirm: hỏi lại bằng cửa sổ xác nhận trước khi gửi.
// Câu hỏi chứa tên phim/chi nhánh do admin nhập, nên để trong attribute cho Razor encode
// và gán bằng textContent, không nhét vào HTML hay chuỗi JS inline.
(function () {
    var approvedForm = null; // form đang được gửi lại sau khi người dùng đã đồng ý.

    function ask(message, danger, onConfirm) {
        var el = document.getElementById('confirm-modal');
        var modal = bootstrap.Modal.getOrCreateInstance(el);
        var ok = el.querySelector('[data-confirm-ok]');

        el.querySelector('[data-confirm-message]').textContent = message;
        ok.className = 'btn ' + (danger ? 'btn-danger' : 'btn-primary');
        ok.onclick = function () {
            modal.hide();
            onConfirm();
        };
        modal.show();
    }

    // Focus vào "Quay lại" để lỡ nhấn Enter thêm lần nữa cũng không xác nhận nhầm.
    document.addEventListener('shown.bs.modal', function (event) {
        if (event.target.id === 'confirm-modal') event.target.querySelector('[data-bs-dismiss]').focus();
    });

    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement) || form === approvedForm) return;

        // jQuery Validate gắn trên form nên chạy trước: nhập sai thì đã bị chặn, chỉ báo lỗi, không hỏi.
        if (event.defaultPrevented) return;

        var submitter = event.submitter || undefined;
        var message = (submitter && submitter.dataset.confirm) || form.dataset.confirm;
        if (!message) return;

        event.preventDefault();
        var button = submitter || form.querySelector('[type="submit"]');
        var danger = !!button && /\bbtn(-outline)?-danger\b/.test(button.className);

        ask(message, danger, function () {
            // requestSubmit (không dùng form.submit) để giữ giá trị nút đã bấm, ví dụ command=apply.
            approvedForm = form;
            form.requestSubmit(submitter);
            approvedForm = null;
        });
    });
})();
