// CinemaBooking v3 "Phòng Chiếu Đêm" — site.js
// Giữ nguyên logic ảnh fallback, form data-confirm, menu dải vé không phụ thuộc bootstrap, và màn nhung phiên đầu.

(function () {
    'use strict';

    // 1. Ảnh poster hỏng hoặc không tải được thì thay bằng ảnh mặc định
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
        // Áp dụng fallback cho các ảnh đã tải xong nhưng chiều rộng bằng 0
        document.querySelectorAll('img[data-poster-fallback]').forEach(function (img) {
            if (img.complete && img.naturalWidth === 0) applyPosterFallback(img);
        });

        // 2. Màn nhung hai cánh mở ra (1 lần mỗi phiên, không chặn focus / content)
        var reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        if (!reduced) {
            try {
                if (!sessionStorage.getItem('cb_curtain_opened')) {
                    sessionStorage.setItem('cb_curtain_opened', '1');
                    document.documentElement.classList.add('is-curtain-opening');
                    setTimeout(function () {
                        document.documentElement.classList.remove('is-curtain-opening');
                    }, 850);
                }
            } catch (e) {
                // sessionStorage fallback an toàn nếu bị chặn
            }
        }

        // 3. Menu tài khoản trên dải vé (Vanilla JS accessible dropdown/popover)
        var triggers = document.querySelectorAll('[data-account-trigger]');
        triggers.forEach(function (trigger) {
            var menu = trigger.parentElement.querySelector('[data-account-menu]');
            if (!menu) return;

            function toggleMenu(open) {
                var isOpen = typeof open === 'boolean' ? open : menu.hasAttribute('hidden');
                if (isOpen) {
                    menu.removeAttribute('hidden');
                    trigger.setAttribute('aria-expanded', 'true');
                } else {
                    menu.setAttribute('hidden', '');
                    trigger.setAttribute('aria-expanded', 'false');
                }
            }

            trigger.addEventListener('click', function (e) {
                e.stopPropagation();
                toggleMenu();
            });

            // Phím Escape đóng menu và trả focus về trigger
            document.addEventListener('keydown', function (e) {
                if (e.key === 'Escape' && !menu.hasAttribute('hidden')) {
                    toggleMenu(false);
                    trigger.focus();
                }
            });

            // Bấm ra ngoài đóng menu
            document.addEventListener('click', function (e) {
                if (!menu.contains(e.target) && !trigger.contains(e.target) && !menu.hasAttribute('hidden')) {
                    toggleMenu(false);
                }
            });
        });

        // 5. Thanh nav .cr-bar: cuộn quá 8px bật class is-scrolled (passive scroll listener, rAF)
        var navBar = document.querySelector('.cr-bar');
        if (navBar) {
            var ticking = false;
            var updateNavScroll = function () {
                if (window.scrollY > 8) {
                    navBar.classList.add('is-scrolled');
                } else {
                    navBar.classList.remove('is-scrolled');
                }
                ticking = false;
            };
            window.addEventListener('scroll', function () {
                if (!ticking) {
                    window.requestAnimationFrame(updateNavScroll);
                    ticking = true;
                }
            }, { passive: true });
            updateNavScroll();
        }
    });

    // 4. Form có data-confirm: xác nhận trước khi thực hiện hành động nguy hiểm
    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement)) return;

        var message = form.dataset.confirm;
        if (message && !window.confirm(message)) {
            event.preventDefault();
        }
    });

    // 6. Nút gửi form (.cb-btn-primary): submit hợp lệ bật is-loading + aria-busy="true", chống bấm đúp
    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement)) return;

        var submitBtn = form.querySelector('button[type="submit"].cb-btn-primary, input[type="submit"].cb-btn-primary, .cb-btn-primary[type="submit"]') ||
                        form.querySelector('button[type="submit"], .cb-btn-primary');
        if (!submitBtn) return;

        // Chống bấm đúp nếu đã is-loading
        if (submitBtn.classList.contains('is-loading')) {
            event.preventDefault();
            return;
        }

        // Chỉ xử lý khi submit hợp lệ (chưa bị chặn bởi confirm hoặc validator)
        if (event.defaultPrevented) return;
        if (form.checkValidity && !form.checkValidity()) return;

        submitBtn.classList.add('is-loading');
        submitBtn.setAttribute('aria-busy', 'true');
    });

    // Quay lại bằng Back (bfcache): gỡ trạng thái đang xử lý còn sót trên nút gửi
    window.addEventListener('pageshow', function (event) {
        if (!event.persisted) return;
        document.querySelectorAll('.is-loading[aria-busy="true"]').forEach(function (btn) {
            btn.classList.remove('is-loading');
            btn.removeAttribute('aria-busy');
        });
    });
})();
