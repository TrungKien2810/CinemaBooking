// ===========================================================================
// CinemaBooking v3 — auth-ui.js
// Tương tác giao diện cho các trang tài khoản & xác thực:
// - Ẩn / hiện mật khẩu (data-password-toggle="<inputId>")
// - Thanh đo độ mạnh mật khẩu (data-password-input + data-password-strength)
// ===========================================================================
(function () {
    'use strict';

    // 1. Ẩn / hiện mật khẩu
    document.querySelectorAll('[data-password-toggle]').forEach(function (btn) {
        var inputId = btn.getAttribute('data-password-toggle');
        var input = document.getElementById(inputId);
        if (!input) return;

        btn.addEventListener('click', function () {
            var show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            btn.textContent = show ? 'Ẩn' : 'Hiện';
            btn.setAttribute('aria-label', show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
            input.focus();
        });
    });

    // 2. Đo độ mạnh mật khẩu (hỗ trợ nhiều form / scoped)
    var passwordInputs = document.querySelectorAll('[data-password-input]');
    if (!passwordInputs.length) return;

    var levels = [
        { text: 'Rất yếu', color: '#ff5c8a', width: '20%' },
        { text: 'Yếu', color: '#ff85a8', width: '40%' },
        { text: 'Trung bình', color: '#ffd166', width: '60%' },
        { text: 'Mạnh', color: '#5ce1e6', width: '80%' },
        { text: 'Rất mạnh', color: '#5df2b0', width: '100%' }
    ];

    function calculateScore(value) {
        var points = 0;
        if (value.length >= 8) points++;
        if (/[A-Z]/.test(value)) points++;
        if (/[0-9]/.test(value)) points++;
        if (/[^A-Za-z0-9]/.test(value)) points++;
        return Math.min(points, 4);
    }

    passwordInputs.forEach(function (password) {
        var form = password.closest('form') || document;
        var strength = form.querySelector('[data-password-strength]');
        if (!strength) return;

        var bar = strength.querySelector('[data-password-bar]');
        var label = strength.querySelector('[data-password-label]');
        if (!bar || !label) return;

        password.addEventListener('input', function () {
            var value = password.value;
            if (!value) {
                strength.hidden = true;
                bar.style.width = '0%';
                label.textContent = '';
                return;
            }

            strength.hidden = false;
            var level = levels[calculateScore(value)];
            bar.style.width = level.width;
            bar.style.background = level.color;
            label.textContent = 'Độ mạnh: ' + level.text;
            label.style.color = level.color;
        });
    });
})();
