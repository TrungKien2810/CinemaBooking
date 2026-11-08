// Sơ đồ ghế real-time (v3 "Phòng Chiếu Đêm").
//
// Luồng: mở trang -> kết nối /hubs/seat -> JoinShowtime. Click ghế trống -> SelectSeat;
// server giữ ghế trong DB với hạn CỐ ĐỊNH (không gia hạn): trả SeatHeldByMe kèm số giây còn lại
// cho chính mình và SeatLocked cho người khác. Click lại ghế của mình -> ReleaseSeat. Hết hạn thì
// server phát SeatUnlocked; ghế đã đặt / đang thanh toán lấy từ DB, cập nhật qua SeatBooked/SeatFreed.
//
// Hiệu ứng: hàng ghế uốn cung nhẹ về màn hình vòm + sóng xuất hiện từ tâm + nảy khi chọn ghế.
(function () {
    'use strict';

    var root = document.getElementById('seat-map');
    if (!root) return;

    var showtimeId = Number(root.dataset.showtimeId);
    var MAX_SEATS = 8; // khớp BookingService.MaxSeatsPerOrder (server vẫn kiểm lại)

    var form = document.getElementById('seat-selection-form');
    var selectedInputs = document.getElementById('seat-selected-inputs');
    var selectedList = document.getElementById('seat-selected-list');
    var timerEl = document.getElementById('seat-hold-timer');
    var holdDeadline = 0; // mốc hết hạn theo đồng hồ server (đổi ra Date.now() lúc nhận số giây còn lại)
    var totalEl = document.getElementById('seat-total');
    var continueBtn = document.getElementById('seat-continue');
    var toasts = document.getElementById('seat-toasts');

    // Admin/BranchAdmin chỉ XEM: vẫn nối hub để thấy ghế đang được giữ/đặt theo thời gian thực, nhưng không chọn ghế.
    // Trang của họ không có khung "đã chọn", nên dùng phần tử rời để các hàm vẽ lại không phải kiểm tra null khắp nơi.
    var readOnly = root.dataset.readonly === 'true';
    if (readOnly) {
        selectedInputs = document.createElement('div');
        selectedList = document.createElement('span');
        totalEl = document.createElement('span');
        continueBtn = document.createElement('button');
    }

    // Ghế đôi là MỘT button mang 2 seat id; map mọi id về cùng button để sự kiện realtime
    // (vốn đi theo từng ghế) vẫn tìm được khối tương ứng.
    var seatButtons = new Map();
    root.querySelectorAll('.seat[data-seat-ids]').forEach(function (btn) {
        unitIds(btn).forEach(function (id) { seatButtons.set(id, btn); });
    });

    // button -> { label, price, seatIds }
    var selected = new Map();

    function unitIds(btn) {
        return (btn.dataset.seatIds || '').split(',')
            .map(Number)
            .filter(function (id) { return id > 0; });
    }

    var reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // 1. Tính toán độ uốn cong hình cung cho các hàng ghế hướng về màn hình vòm cong
    function applyRowCurves() {
        var rows = Array.prototype.slice.call(root.querySelectorAll('.seat-row:not(.seat-row-head)'));
        if (rows.length === 0) return;

        rows.forEach(function (rowEl) {
            var seats = Array.prototype.slice.call(rowEl.querySelectorAll('.seat'));
            if (seats.length <= 1) return;
            var mid = (seats.length - 1) / 2;
            seats.forEach(function (seatEl, idx) {
                var diff = idx - mid;
                var norm = diff / (mid || 1); // từ -1 đến +1
                // Cung parabol nhẹ: hai bên uốn cong nhẹ về phía trước màn hình (tối đa 12px, góc nghiêng 4.5 độ)
                var curveY = reduced ? 0 : Math.round(Math.pow(norm, 2) * 12);
                var curveR = reduced ? 0 : Number((norm * 4.5).toFixed(2));
                seatEl.style.setProperty('--seat-curve-y', curveY + 'px');
                seatEl.style.setProperty('--seat-curve-r', curveR + 'deg');
            });
        });
    }
    applyRowCurves();

    // 2. Hiệu ứng sóng xuất hiện từ giữa khán phòng ra ngoài (tổng thời gian <= 600ms)
    if (!reduced) {
        var rows = Array.prototype.slice.call(root.querySelectorAll('.seat-row:not(.seat-row-head)'));
        var midRow = (rows.length - 1) / 2;
        var maxDist = 1;

        rows.forEach(function (rowEl, rIdx) {
            var seatsInRow = Array.prototype.slice.call(rowEl.querySelectorAll('.seat[data-seat-ids]'));
            var midCol = (seatsInRow.length - 1) / 2;
            seatsInRow.forEach(function (btn, cIdx) {
                var d = Math.sqrt(Math.pow(rIdx - midRow, 2) + Math.pow(cIdx - midCol, 2));
                if (d > maxDist) maxDist = d;
            });
        });

        rows.forEach(function (rowEl, rIdx) {
            var seatsInRow = Array.prototype.slice.call(rowEl.querySelectorAll('.seat[data-seat-ids]'));
            var midCol = (seatsInRow.length - 1) / 2;
            seatsInRow.forEach(function (btn, cIdx) {
                var d = Math.sqrt(Math.pow(rIdx - midRow, 2) + Math.pow(cIdx - midCol, 2));
                var i = Math.round((d / maxDist) * 18);
                btn.style.setProperty('--i', i);
            });
        });

        root.classList.add('seat-wave-ready');
    }

    var money = new Intl.NumberFormat('vi-VN', {
        style: 'currency',
        currency: 'VND',
        maximumFractionDigits: 0
    });

    function toast(message, isError) {
        if (!toasts) return;
        var el = document.createElement('div');
        el.className = 'seat-toast' + (isError ? ' is-error' : '');
        el.textContent = message;
        toasts.appendChild(el);
        setTimeout(function () {
            el.style.opacity = '0';
            setTimeout(function () { el.remove(); }, 200);
        }, 3200);
    }

    function setState(btn, state) {
        btn.classList.remove('is-free', 'is-mine', 'is-held', 'is-booked');
        btn.classList.add('is-' + state);
        btn.disabled = readOnly || state === 'booked' || state === 'held';
        btn.setAttribute('aria-pressed', state === 'mine' ? 'true' : 'false');
    }

    function isBooked(btn) { return btn.classList.contains('is-booked'); }
    function isHeld(btn) { return btn.classList.contains('is-held'); }
    function isMine(btn) { return btn.classList.contains('is-mine'); }

    function updateBumpedText(el, text) {
        if (!el) return;
        if (el.textContent === text) return;
        el.textContent = text;
        el.classList.remove('is-bumped');
        void el.offsetWidth;
        el.classList.add('is-bumped');
        var onEnd = function () {
            el.classList.remove('is-bumped');
            el.removeEventListener('animationend', onEnd);
        };
        el.addEventListener('animationend', onEnd);
    }

    function renderSelection() {
        if (selectedInputs) selectedInputs.innerHTML = '';
        selected.forEach(function (item) {
            item.seatIds.forEach(function (seatId) {
                if (selectedInputs) {
                    var input = document.createElement('input');
                    input.type = 'hidden';
                    input.name = 'seatIds';
                    input.value = seatId;
                    selectedInputs.appendChild(input);
                }
            });
        });

        if (selected.size === 0) {
            if (selectedList) {
                updateBumpedText(selectedList, 'Chưa chọn ghế nào');
                selectedList.classList.add('cb-muted');
            }
            if (totalEl) updateBumpedText(totalEl, money.format(0));
            if (continueBtn) continueBtn.disabled = true;
            return;
        }

        var labels = [];
        var total = 0;
        selected.forEach(function (item) {
            labels.push(item.label);
            total += item.price;
        });
        if (selectedList) {
            updateBumpedText(selectedList, 'Đã chọn: ' + labels.join(', '));
            selectedList.classList.remove('cb-muted');
        }
        if (totalEl) updateBumpedText(totalEl, money.format(total));
        if (continueBtn) continueBtn.disabled = false;
    }

    function formatClock(ms) {
        var total = Math.max(0, Math.ceil(ms / 1000));
        var seconds = total % 60;
        return Math.floor(total / 60) + ':' + (seconds < 10 ? '0' : '') + seconds;
    }

    function addMine(seatId, secondsLeft) {
        if (secondsLeft) holdDeadline = Date.now() + secondsLeft * 1000;
        var btn = seatButtons.get(seatId);
        if (!btn) return;

        setState(btn, 'mine');

        // Hiệu ứng nảy phản hồi khi server xác nhận ghế còn bán được
        if (!reduced) {
            btn.classList.add('is-popping');
            setTimeout(function () { btn.classList.remove('is-popping'); }, 300);
        }

        selected.set(btn, {
            label: btn.dataset.seatLabel,
            price: Number(btn.dataset.seatPrice) || 0,
            seatIds: unitIds(btn)
        });
        renderSelection();
    }

    function removeSelected(seatId) {
        var btn = seatButtons.get(seatId);
        if (!btn) return;
        if (selected.delete(btn)) renderSelection();
        if (!isBooked(btn)) setState(btn, 'free');
    }

    function markHeld(seatId) {
        var btn = seatButtons.get(seatId);
        if (!btn || isBooked(btn) || isMine(btn)) return;
        setState(btn, 'held');
    }

    function markFree(seatId) {
        var btn = seatButtons.get(seatId);
        if (!btn || isBooked(btn)) return;
        if (selected.delete(btn)) renderSelection();
        setState(btn, 'free');
    }

    function markBooked(seatId) {
        var btn = seatButtons.get(seatId);
        if (!btn) return;
        selected.delete(btn);
        renderSelection();
        setState(btn, 'booked');
    }

    // Ghế đã đặt trống lại: khác markFree ở chỗ được phép bỏ trạng thái "đã đặt".
    function markFreed(seatId) {
        var btn = seatButtons.get(seatId);
        if (!btn) return;
        if (selected.delete(btn)) renderSelection();
        setState(btn, 'free');
    }

    function clearSelection() {
        selected.forEach(function (item, btn) {
            if (!isBooked(btn)) setState(btn, 'free');
        });
        selected.clear();
        renderSelection();
    }

    // Đồng hồ đếm ngược theo hạn cố định của server; hết hạn thì bỏ chọn (server cũng đã nhả ghế).
    setInterval(function () {
        if (!timerEl) return;
        if (selected.size === 0) {
            timerEl.hidden = true;
            return;
        }
        var left = holdDeadline - Date.now();
        if (left <= 0) {
            clearSelection();
            timerEl.hidden = true;
            toast('Đã hết thời gian giữ ghế. Vui lòng chọn lại.', true);
            return;
        }
        timerEl.hidden = false;
        timerEl.classList.toggle('is-warning', left < 60000);
        timerEl.textContent = '⏱ Giữ ghế còn ' + formatClock(left);
    }, 500);

    if (form) {
        form.addEventListener('submit', function (event) {
            if (selected.size === 0) {
                event.preventDefault();
                toast('Vui lòng chọn ít nhất một ghế.', true);
                return;
            }
            if (event.defaultPrevented) return;

            // Chặn double-submit.
            if (continueBtn) {
                continueBtn.disabled = true;
                continueBtn.textContent = 'Đang chuyển sang thanh toán…';
            }
        });
    }

    if (typeof signalR === 'undefined') {
        toast('Không tải được thư viện real-time. Vui lòng tải lại trang.', true);
        return;
    }

    var connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/seat')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .build();

    connection.on('SeatSnapshot', function (held) {
        // Snapshot là sự thật hiện tại: ghế nào không có trong đó mà đang hiện "giữ" (người khác nhả/hết hạn
        // lúc mất kết nối) thì trả về trống, rồi mới áp snapshot.
        seatButtons.forEach(function (btn) { if (isHeld(btn)) setState(btn, 'free'); });
        var mineIds = {};
        (held || []).forEach(function (item) { if (item.mine) mineIds[item.seatId] = true; });
        (held || []).forEach(function (item) {
            // Ghế đôi: chỉ coi là của mình khi đủ cả hai nửa (một nửa lẻ không đặt được).
            var btn = seatButtons.get(item.seatId);
            if (item.mine && btn && unitIds(btn).every(function (id) { return mineIds[id]; })) addMine(item.seatId, item.secondsLeft);
            else markHeld(item.seatId);
        });
    });

    connection.on('SeatHeldByMe', function (seatId, secondsLeft) {
        addMine(seatId, secondsLeft);
    });

    connection.on('SeatLocked', function (seatId) {
        markHeld(seatId);
    });

    connection.on('SeatUnlocked', function (seatIds) {
        (seatIds || []).forEach(markFree);
    });

    connection.on('SeatRejected', function (seatId, message) {
        // Ghế đôi gửi 2 yêu cầu: nếu nửa thứ hai bị từ chối thì bỏ chọn luôn nửa đã chọn được
        // để không ai bị kẹt nửa cặp.
        var btn = seatButtons.get(seatId);
        if (btn) {
            unitIds(btn).forEach(function (id) {
                connection.invoke('ReleaseSeat', showtimeId, id).catch(function () { });
            });
            removeSelected(Number((btn.dataset.seatIds || '').split(',')[0]));
        }
        toast(message || 'Không chọn được ghế này.', true);
    });

    // Worker/Booking phát khi ghế vừa được đặt xong; nếu server chưa phát thì
    // danh sách ghế đã đặt vẫn đúng khi tải lại trang (đọc từ DB).
    connection.on('SeatBooked', function (seatIds) {
        (seatIds || []).forEach(markBooked);
    });

    connection.on('SeatFreed', function (seatIds) {
        (seatIds || []).forEach(markFreed);
    });

    connection.onreconnecting(function () {
        toast('Mất kết nối real-time, đang kết nối lại…', true);
        clearSelection();
    });

    connection.onreconnected(function () {
        joinShowtime();
        toast('Đã kết nối lại. Ghế bạn đang giữ sẽ hiện lại nếu còn hạn.');
    });

    connection.onclose(function () {
        toast('Đã mất kết nối real-time. Đang thử lại…', true);
        setTimeout(start, 3000);
    });

    function joinShowtime() {
        return connection.invoke('JoinShowtime', showtimeId);
    }

    function start() {
        connection.start()
            .then(function () {
                return joinShowtime();
            })
            .catch(function () {
                toast('Chưa kết nối được real-time, thử lại sau 3 giây…', true);
                setTimeout(start, 3000);
            });
    }

    // Mỗi button chỉ gắn listener một lần (ghế đôi có 2 seat id cùng trỏ về một button).
    new Set(seatButtons.values()).forEach(function (btn) {
        if (readOnly) return;
        btn.addEventListener('click', function () {
            if (isBooked(btn)) return;

            if (isHeld(btn)) {
                toast('Ghế đang được người khác giữ, thử lại sau ít phút.', true);
                return;
            }

            if (isMine(btn)) {
                unitIds(btn).forEach(function (id) {
                    connection.invoke('ReleaseSeat', showtimeId, id).catch(function () { });
                });
                removeSelected(Number((btn.dataset.seatIds || '').split(',')[0]));
                return;
            }

            if (connection.state !== signalR.HubConnectionState.Connected) {
                toast('Chưa kết nối real-time, vui lòng thử lại.', true);
                return;
            }

            var need = unitIds(btn).length;
            var chosen = 0;
            selected.forEach(function (item) { chosen += item.seatIds.length; });
            if (chosen + need > MAX_SEATS) {
                toast('Mỗi đơn chỉ đặt tối đa ' + MAX_SEATS + ' ghế.', true);
                return;
            }

            // Ghế trống: xin kiểm tra cho TỪNG ghế của khối (ghế đôi gửi cả 2 nửa).
            // UI chỉ đổi khi server gửi SeatHeldByMe.
            unitIds(btn).forEach(function (id) {
                connection.invoke('SelectSeat', showtimeId, id).catch(function () {
                    toast('Không gửi được yêu cầu chọn ghế.', true);
                });
            });
        });
    });

    renderSelection();
    start();
})();
