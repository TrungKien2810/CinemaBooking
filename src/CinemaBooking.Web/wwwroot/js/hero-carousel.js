// CinemaBooking v3 "Phòng Chiếu Đêm" — hero-carousel.js
// Giữ nguyên hợp đồng selector: [data-hero-carousel], .hero-slide, [data-hero-dot], [data-hero-pause], .is-active.
// Tự chuyển ảnh sau 4 giây; có nút dừng, dừng khi rê chuột/focus và tôn trọng reduced-motion.

(function () {
    'use strict';

    var root = document.querySelector('[data-hero-carousel]');
    if (!root) return;

    var slides = Array.prototype.slice.call(root.querySelectorAll('.hero-slide'));
    var dots = Array.prototype.slice.call(root.querySelectorAll('[data-hero-dot]'));
    var pauseBtn = root.querySelector('[data-hero-pause]');
    if (slides.length < 2) return;

    var index = 0;
    var timer = null;
    var isManualPaused = false;
    var reduced = window.matchMedia('(prefers-reduced-motion: reduce)');

    function show(next) {
        index = (next + slides.length) % slides.length;

        slides.forEach(function (slide, i) {
            var active = i === index;
            slide.classList.toggle('is-active', active);
            slide.setAttribute('aria-hidden', active ? 'false' : 'true');
            slide.tabIndex = active ? 0 : -1;
        });

        dots.forEach(function (dot, i) {
            dot.classList.toggle('is-active', i === index);
            dot.setAttribute('aria-current', i === index ? 'true' : 'false');
        });
    }

    function stop() {
        if (timer) {
            clearInterval(timer);
            timer = null;
        }
    }

    function start() {
        stop();
        if (reduced.matches || isManualPaused) return;
        timer = setInterval(function () {
            show(index + 1);
        }, 4000);
    }

    function togglePause() {
        isManualPaused = !isManualPaused;
        if (pauseBtn) {
            pauseBtn.textContent = isManualPaused ? '▶' : '⏸';
            pauseBtn.setAttribute('aria-label', isManualPaused ? 'Tiếp tục trình chiếu' : 'Tạm dừng trình chiếu');
            pauseBtn.setAttribute('title', isManualPaused ? 'Tiếp tục trình chiếu' : 'Tạm dừng trình chiếu');
        }
        if (isManualPaused) {
            stop();
        } else {
            start();
        }
    }

    if (pauseBtn) {
        pauseBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            togglePause();
        });
    }

    dots.forEach(function (dot, i) {
        dot.addEventListener('click', function () {
            show(i);
            start();
        });
    });

    root.addEventListener('mouseenter', stop);
    root.addEventListener('mouseleave', function () {
        if (!isManualPaused) start();
    });
    root.addEventListener('focusin', stop);
    root.addEventListener('focusout', function (event) {
        if (!root.contains(event.relatedTarget) && !isManualPaused) start();
    });

    root.addEventListener('keydown', function (event) {
        if (event.key === 'ArrowLeft') {
            event.preventDefault();
            show(index - 1);
            start();
        } else if (event.key === 'ArrowRight') {
            event.preventDefault();
            show(index + 1);
            start();
        }
    });

    document.addEventListener('visibilitychange', function () {
        if (document.hidden) stop();
        else if (!isManualPaused) start();
    });

    if (typeof reduced.addEventListener === 'function') {
        reduced.addEventListener('change', function () {
            if (reduced.matches) stop();
            else if (!isManualPaused) start();
        });
    }

    start();
})();
