// Đồng hồ giữ ghế ở trang thanh toán.
//
// Ghế chỉ được giữ SeatHold:DurationMinutes (mặc định 5 phút) kể từ lúc bấm "Tiếp tục thanh toán".
// Trước đây khách không thấy gì nên gõ xong form mới bị báo "ghế đã hết hạn". Đồng hồ này đếm ngược
// theo đúng mốc server đã tính, hết giờ thì chặn gửi form và đưa về sơ đồ ghế.
(function () {
    'use strict';

    var timerEl = document.getElementById('checkout-hold-timer');
    if (!timerEl) return;

    var remaining = Number(timerEl.dataset.remainingSeconds) || 0;
    var seatMapUrl = timerEl.dataset.seatMapUrl;
    var submitBtn = document.getElementById('checkout-submit');
    var expiredBox = document.getElementById('checkout-expired');

    function paint() {
        if (remaining <= 0) {
            timerEl.textContent = 'Đã hết thời gian giữ ghế';
            timerEl.classList.add('is-warning');

            if (submitBtn) {
                submitBtn.disabled = true;
                submitBtn.textContent = 'Hết thời gian giữ ghế';
            }
            if (expiredBox) expiredBox.hidden = false;

            clearInterval(ticker);
            if (seatMapUrl) {
                setTimeout(function () { window.location.href = seatMapUrl; }, 4000);
            }
            return;
        }

        var minutes = Math.floor(remaining / 60);
        var seconds = remaining % 60;
        timerEl.textContent = 'Giữ ghế còn ' + minutes + ':' + (seconds < 10 ? '0' : '') + seconds;
        timerEl.classList.toggle('is-warning', remaining <= 60);
        remaining--;
    }

    var ticker = setInterval(paint, 1000);
    paint();
})();
