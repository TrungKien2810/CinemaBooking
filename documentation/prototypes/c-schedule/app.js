/* ==========================================================================
   C — Bảng lịch · app.js
   Tĩnh, không thư viện ngoài. Điều khiển: lọc trang chủ, bảng lịch theo
   ngày/chi nhánh, chọn ghế, đồng hồ giữ ghế 5:00, tổng tiền, kiểm tra
   biểu mẫu, mô phỏng hết hạn, và điều hướng nhẹ giữa 4 màn hình.
   ========================================================================== */

(function () {
  'use strict';

  /* ---------------- Tiện ích ---------------- */

  var $ = function (sel, root) { return (root || document).querySelector(sel); };
  var $$ = function (sel, root) {
    return Array.prototype.slice.call((root || document).querySelectorAll(sel));
  };

  function vnd(amount) {
    return amount.toLocaleString('vi-VN') + ' ₫';
  }

  function formatClock(totalSeconds) {
    var m = Math.floor(totalSeconds / 60);
    var s = totalSeconds % 60;
    return m + ':' + (s < 10 ? '0' + s : s);
  }

  function params() {
    var out = {};
    new URLSearchParams(window.location.search).forEach(function (value, key) {
      out[key] = value;
    });
    return out;
  }

  /* ---------------- Dữ liệu demo (theo CatalogSeeder) ---------------- */

  var DAYS = [
    { n: 1, dow: 'Thứ Tư', date: '21/10' },
    { n: 2, dow: 'Thứ Năm', date: '22/10' },
    { n: 3, dow: 'Thứ Sáu', date: '23/10' },
    { n: 4, dow: 'Thứ Bảy', date: '24/10' },
    { n: 5, dow: 'Chủ Nhật', date: '25/10' },
    { n: 6, dow: 'Thứ Hai', date: '26/10' },
    { n: 7, dow: 'Thứ Ba', date: '27/10' }
  ];

  var BRANCHES = [
    {
      id: 'nguyen-hue',
      name: 'CineSmart Nguyễn Huệ',
      address: '72 Nguyễn Huệ, Quận 1',
      city: 'hcmc',
      cityName: 'TP. Hồ Chí Minh',
      base: 95000,
      rooms: [
        {
          name: 'Phòng 1',
          size: '8×12',
          times: ['09:00', '12:15', '15:30', '18:45', '22:00'],
          seats: {
            '09:00': [22, 20, 18, 16, 14, 12, 10],
            '12:15': [30, 28, 26, 24, 22, 20, 18],
            '15:30': [10, 14, 18, 22, 26, 30, 34],
            '18:45': [40, 36, 32, 28, 24, 20, 16],
            '22:00': [12, 10, 8, 6, 4, 2, 0]
          }
        },
        {
          name: 'Phòng 2',
          size: '6×10',
          times: ['09:00', '12:15', '15:30', '18:45', '22:00'],
          seats: {
            '09:00': [16, 15, 14, 13, 12, 11, 10],
            '12:15': [24, 22, 20, 18, 16, 14, 12],
            '15:30': [8, 10, 12, 14, 16, 18, 20],
            '18:45': [30, 28, 26, 24, 22, 20, 18],
            '22:00': [6, 5, 4, 3, 2, 1, 0]
          }
        }
      ]
    },
    {
      id: 'thao-dien',
      name: 'CineSmart Thảo Điền',
      address: '12 Quốc Hương, Thảo Điền',
      city: 'hcmc',
      cityName: 'TP. Hồ Chí Minh',
      base: 85000,
      rooms: [
        {
          name: 'Phòng 1',
          size: '7×10',
          times: ['10:30', '14:30', '19:00'],
          seats: {
            '10:30': [20, 18, 16, 14, 12, 10, 8],
            '14:30': [25, 23, 21, 19, 17, 15, 13],
            '19:00': [35, 32, 29, 26, 23, 20, 17]
          }
        }
      ]
    },
    {
      id: 'cau-giay',
      name: 'CineSmart Cầu Giấy',
      address: '15 Trần Duy Hưng',
      city: 'hn',
      cityName: 'Hà Nội',
      base: 90000,
      rooms: [
        {
          name: 'Phòng A',
          size: '10×14',
          times: ['09:30', '12:45', '16:00', '19:15', '22:30'],
          seats: {
            '09:30': [30, 28, 26, 24, 22, 20, 18],
            '12:45': [40, 38, 36, 34, 32, 30, 28],
            '16:00': [18, 16, 14, 12, 10, 8, 6],
            '19:15': [44, 40, 36, 32, 28, 24, 20],
            '22:30': [12, 11, 10, 9, 8, 7, 6]
          }
        },
        {
          name: 'Phòng B',
          size: '5×8',
          times: ['09:30', '12:45', '16:00', '19:15', '22:30'],
          seats: {
            '09:30': [14, 13, 12, 11, 10, 9, 8],
            '12:45': [22, 20, 18, 16, 14, 12, 10],
            '16:00': [10, 9, 8, 7, 6, 5, 4],
            '19:15': [28, 26, 24, 22, 20, 18, 16],
            '22:30': [8, 7, 6, 5, 4, 3, 2]
          }
        }
      ]
    },
    {
      id: 'hoan-kiem',
      name: 'CineSmart Hoàn Kiếm',
      address: '8 Tràng Tiền',
      city: 'hn',
      cityName: 'Hà Nội',
      base: 80000,
      rooms: [
        {
          name: 'Phòng 1',
          size: '6×9',
          times: ['11:00', '17:00', '20:30'],
          seats: {
            '11:00': [18, 16, 14, 12, 10, 8, 6],
            '17:00': [26, 24, 22, 20, 18, 16, 14],
            '20:30': [12, 10, 8, 6, 4, 2, 0]
          }
        }
      ]
    }
  ];

  /* ---------------- TRANG CHỦ: tìm kiếm + lọc thể loại ---------------- */

  function initHome() {
    var list = $('#film-list');
    if (!list) return;

    var rows = $$('.filmlist__row', list);
    var empty = $('#film-empty');
    var query = $('#film-search');
    var chips = $$('#genre-chips .chip');
    var activeGenre = 'all';

    function apply() {
      var term = ((query && query.value) || '').trim().toLowerCase();
      var visible = 0;

      rows.forEach(function (row) {
        var name = row.getAttribute('data-title').toLowerCase();
        var genre = row.getAttribute('data-genre').toLowerCase();
        var matchTerm = term === '' || name.indexOf(term) !== -1 || genre.indexOf(term) !== -1;
        var matchGenre = activeGenre === 'all' || genre === activeGenre;
        var show = matchTerm && matchGenre;
        row.hidden = !show;
        if (show) visible += 1;
      });

      if (empty) empty.hidden = visible !== 0;
    }

    if (query) {
      query.addEventListener('input', apply);
      query.addEventListener('search', apply);
    }

    chips.forEach(function (chip) {
      chip.addEventListener('click', function () {
        chips.forEach(function (c) { c.setAttribute('aria-pressed', 'false'); });
        chip.setAttribute('aria-pressed', 'true');
        activeGenre = chip.getAttribute('data-genre');
        apply();
      });
    });

    apply();
  }

  /* ---------------- TRANG PHIM: bảng lịch theo ngày + chi nhánh ---------------- */

  function initMovie() {
    var board = $('#bang-lich');
    if (!board) return;

    var tabs = $$('#daytabs .daytab');
    var cityChips = $$('#city-chips .chip');
    var branchChips = $$('#branch-chips .chip');
    var dayNote = $('#board-day');
    var countNote = $('#board-count');
    var query = params();

    var state = {
      day: parseInt(query.day, 10) || 1,
      city: query.city || 'all',
      branch: query.branch || 'all',
      show: query.show || ''
    };

    function seatsLeft(room, time) {
      var series = room.seats[time];
      if (!series) return 0;
      return series[state.day - 1];
    }

    function seatsClass(count) {
      if (count === 0) return 'seatsleft--none';
      if (count <= 5) return 'seatsleft--low';
      return 'seatsleft--ok';
    }

    function seatsLabel(count) {
      if (count === 0) return 'hết chỗ';
      return 'còn ' + count + ' ghế';
    }

    function buildRow(branch, room, time) {
      var count = seatsLeft(room, time);
      var row = document.createElement('a');
      row.className = 'board__row' + (state.show && state.show === time ? ' is-current' : '');
      row.setAttribute('href', 'seatmap.html?branch=' + encodeURIComponent(branch.id) +
        '&room=' + encodeURIComponent(room.name) + '&show=' + encodeURIComponent(time) +
        '&day=' + state.day);
      if (count === 0) {
        row.setAttribute('aria-disabled', 'true');
        row.removeAttribute('href');
      }
      row.innerHTML =
        '<span class="board__cell board__time"><time>' + time + '</time></span>' +
        '<span class="board__cell board__room">' + room.name + ' (' + room.size + ')</span>' +
        '<span class="board__cell board__price">' + vnd(branch.base) + '</span>' +
        '<span class="board__cell board__seats ' + seatsClass(count) + '">' + seatsLabel(count) + '</span>' +
        '<span class="board__cell board__cta">' + (count === 0 ? 'Hết chỗ' : 'Chọn suất') + '</span>';
      return row;
    }

    function render() {
      board.innerHTML = '';
      var shown = 0;
      var day = DAYS[state.day - 1];

      if (dayNote) dayNote.textContent = day.dow + ' ' + day.date;
      board.setAttribute('aria-labelledby', 'daytab-' + state.day);

      BRANCHES.forEach(function (branch) {
        if (state.city !== 'all' && branch.city !== state.city) return;
        if (state.branch !== 'all' && branch.id !== state.branch) return;

        var group = document.createElement('section');
        group.className = 'board__group';

        var head = document.createElement('div');
        head.className = 'board__grouphead';
        head.innerHTML =
          '<div><div class="board__branch">' + branch.name + '</div>' +
          '<div class="board__address">' + branch.address + ', ' + branch.cityName + '</div></div>' +
          '<div class="board__count">Từ ' + vnd(branch.base) + '</div>';

        group.appendChild(head);

        branch.rooms.forEach(function (room) {
          room.times.forEach(function (time) {
            group.appendChild(buildRow(branch, room, time));
            shown += 1;
          });
        });

        board.appendChild(group);
      });

      if (shown === 0) {
        var emptyBox = $('#board-empty');
        if (emptyBox) emptyBox.hidden = false;
      } else {
        var emptyBox2 = $('#board-empty');
        if (emptyBox2) emptyBox2.hidden = true;
      }

      if (countNote) countNote.textContent = shown + ' suất chiếu';
    }

    tabs.forEach(function (tab) {
      tab.addEventListener('click', function (event) {
        event.preventDefault();
        tabs.forEach(function (t) { t.setAttribute('aria-selected', 'false'); });
        tab.setAttribute('aria-selected', 'true');
        state.day = parseInt(tab.getAttribute('data-day'), 10);
        render();
      });
    });

    var tablist = $('#daytabs');
    if (tablist) {
      tablist.addEventListener('keydown', function (event) {
        var current = tabs.indexOf(document.activeElement);
        if (current === -1) return;
        var next = current;

        if (event.key === 'ArrowRight') next = (current + 1) % tabs.length;
        else if (event.key === 'ArrowLeft') next = (current - 1 + tabs.length) % tabs.length;
        else if (event.key === 'Home') next = 0;
        else if (event.key === 'End') next = tabs.length - 1;
        else return;

        event.preventDefault();
        tabs[next].focus();
        tabs[next].click();
      });
    }

    cityChips.forEach(function (chip) {
      chip.addEventListener('click', function () {
        cityChips.forEach(function (c) { c.setAttribute('aria-pressed', 'false'); });
        chip.setAttribute('aria-pressed', 'true');
        state.city = chip.getAttribute('data-city');
        state.branch = 'all';
        branchChips.forEach(function (c) { c.setAttribute('aria-pressed', 'false'); });
        render();
      });
    });

    branchChips.forEach(function (chip) {
      chip.addEventListener('click', function () {
        branchChips.forEach(function (c) { c.setAttribute('aria-pressed', 'false'); });
        chip.setAttribute('aria-pressed', 'true');
        state.branch = chip.getAttribute('data-branch');
        render();
      });
    });

    tabs.forEach(function (tab) {
      tab.setAttribute('aria-selected', tab.getAttribute('data-day') === String(state.day) ? 'true' : 'false');
    });
    cityChips.forEach(function (chip) {
      chip.setAttribute('aria-pressed', chip.getAttribute('data-city') === state.city ? 'true' : 'false');
    });
    branchChips.forEach(function (chip) {
      chip.setAttribute('aria-pressed', chip.getAttribute('data-branch') === state.branch ? 'true' : 'false');
    });

    render();
  }

  /* ---------------- GHẾ: loại ghế theo hàng (CineSmart Nguyễn Huệ, Phòng 1) ---------------- */

  var ROW_TYPES = {
    A: 'standard', B: 'standard', C: 'vip',
    D: 'vip', E: 'vip', F: 'standard',
    G: 'standard', H: 'double'
  };

  var SEAT_PRICES = { standard: 95000, vip: 114000, double: 190000 };
  var SEAT_LABELS = { standard: 'Thường', vip: 'VIP', double: 'Đôi' };

  var TAKEN_SEATS = ['B2', 'B3', 'F5', 'F6', 'G7'];   /* đã đặt */
  var HELD_SEATS = ['D7', 'D8'];                    /* người khác đang giữ */

  function seatType(id) { return ROW_TYPES[id.charAt(0)]; }
  function seatPrice(id) { return SEAT_PRICES[seatType(id)]; }

  function initSeatmap() {
    var grid = $('#seatgrid');
    if (!grid) return;

    var query = params();
    var showTime = query.show || '18:45';
    var dayNumber = parseInt(query.day, 10) || 2;
    var day = DAYS[dayNumber - 1] || DAYS[1];

    var timerValue = $('#timer-value');
    var timerFill = $('#timer-fill');
    var timerBox = $('#hold-timer');
    var timerLabel = $('#timer-label');
    var seatList = $('#selected-seats');
    var seatCount = $('#selected-count');
    var totalEl = $('#order-total');
    var totalLive = $('#total-live');
    var checkoutBtn = $('#checkout-btn');
    var notice = $('#hold-notice');
    var noticeText = $('#hold-notice-text');
    var restartBtn = $('#hold-restart');
    var emptyState = $('#seats-empty');
    var simulateBtn = $('#simulate-expire');
    var sampleBtn = $('#sample-seats');

    var HOLD_SECONDS = 300;
    var remaining = HOLD_SECONDS;
    var tick = null;
    var expired = false;
    var selected = [];

    if ($('#seat-when')) $('#seat-when').textContent = day.dow + ' ' + day.date + ', suất ' + showTime;
    if ($('#seat-branch')) $('#seat-branch').textContent = 'CineSmart Nguyễn Huệ, Phòng 1 (8×12)';
    if ($('#seat-showtime')) $('#seat-showtime').textContent = showTime;
    if ($('#seat-day')) $('#seat-day').textContent = day.dow + ' ' + day.date;

    function seatLabel(id, state) {
      var price = seatPrice(id);
      if (state === 'taken') return 'Ghế ' + id + ', đã đặt, không chọn được';
      if (state === 'held') return 'Ghế ' + id + ', người khác đang giữ, không chọn được';
      if (state === 'mine') return 'Ghế ' + id + ', loại ' + SEAT_LABELS[seatType(id)] + ', ' + vnd(price) + ', bạn đang giữ';
      return 'Ghế ' + id + ', loại ' + SEAT_LABELS[seatType(id)] + ', ' + vnd(price) + ', còn trống';
    }

    function buildSeat(id, rowIndex, colIndex, isDouble) {
      var state = 'free';
      if (TAKEN_SEATS.indexOf(id) !== -1) state = 'taken';
      else if (HELD_SEATS.indexOf(id) !== -1) state = 'held';

      var button = document.createElement('button');
      button.type = 'button';
      button.className = 'seat';
      button.setAttribute('data-seat', id);
      button.setAttribute('data-state', state);
      button.setAttribute('data-type', seatType(id));
      button.setAttribute('data-price', String(seatPrice(id)));
      button.setAttribute('aria-label', seatLabel(id, state));

      var glyph = { mine: '✓', held: '●', taken: '×' }[state];
      if (glyph) {
        button.innerHTML = '<span class="seat__glyph" aria-hidden="true">' + glyph + '</span>' + id.replace(/^[A-H]/, '');
      } else {
        button.textContent = id.replace(/^[A-H]/, '');
      }

      if (isDouble) button.classList.add('seat--double');
      if (state === 'held') {
        button.classList.add('seat--held');
        button.setAttribute('aria-disabled', 'true');
      }
      if (state === 'taken') {
        button.classList.add('seat--taken');
        button.setAttribute('aria-disabled', 'true');
      }
      if (state === 'free') button.setAttribute('aria-pressed', 'false');

      button.style.gridColumn = isDouble
        ? (colIndex < 3 ? String(2 + colIndex * 2) + ' / span 2' : String(9 + (colIndex - 3) * 2) + ' / span 2')
        : (colIndex < 6 ? String(2 + colIndex) : String(3 + colIndex));

      button.addEventListener('click', function () {
        if (button.getAttribute('aria-disabled') === 'true') return;
        if (expired) return;
        toggleSeat(button);
      });

      return button;
    }

    function renderGrid() {
      grid.innerHTML = '';
      'ABCDEFG'.split('').forEach(function (rowId) {
        var label = document.createElement('span');
        label.className = 'seatgrid__rowlabel';
        label.textContent = rowId;
        grid.appendChild(label);

        for (var c = 1; c <= 12; c++) {
          grid.appendChild(buildSeat(rowId + c, rowId, c - 1, false));
        }
      });

      var lastLabel = document.createElement('span');
      lastLabel.className = 'seatgrid__rowlabel';
      lastLabel.textContent = 'H';
      grid.appendChild(lastLabel);

      for (var d = 1; d <= 6; d++) {
        grid.appendChild(buildSeat('H' + d, 'H', d - 1, true));
      }
    }

    function toggleSeat(button) {
      var id = button.getAttribute('data-seat');
      var index = selected.indexOf(id);

      if (index === -1) {
        selected.push(id);
        button.setAttribute('data-state', 'mine');
        button.setAttribute('aria-pressed', 'true');
        button.classList.remove('seat--free');
        button.classList.add('seat--mine');
        button.setAttribute('aria-label', seatLabel(id, 'mine'));
        button.innerHTML = '<span class="seat__glyph" aria-hidden="true">✓</span>' + id.replace(/^[A-H]/, '');
      } else {
        selected.splice(index, 1);
        button.setAttribute('data-state', 'free');
        button.setAttribute('aria-pressed', 'false');
        button.classList.remove('seat--mine', 'seat--expired');
        button.setAttribute('aria-label', seatLabel(id, 'free'));
        button.textContent = id.replace(/^[A-H]/, '');
      }

      updateSummary();
    }

    function updateSummary() {
      var total = selected.reduce(function (sum, id) { return sum + seatPrice(id); }, 0);

      if (seatList) {
        seatList.innerHTML = '';
        if (selected.length === 0) {
          var li = document.createElement('li');
          li.className = 'summary__empty';
          li.textContent = 'Chưa chọn ghế nào.';
          seatList.appendChild(li);
        } else {
          selected.forEach(function (id) {
            var li = document.createElement('li');
            li.className = 'summary__row';
            li.innerHTML = '<span class="summary__key">Ghế ' + id + ' (' + SEAT_LABELS[seatType(id)] + ')</span>' +
              '<span class="summary__val summary__val--num">' + vnd(seatPrice(id)) + '</span>';
            seatList.appendChild(li);
          });
        }
      }

      if (seatCount) seatCount.textContent = selected.length + ' ghế';
      if (totalEl) totalEl.textContent = vnd(total);
      if (emptyState) emptyState.hidden = selected.length !== 0;

      if (totalLive) {
        totalLive.textContent = selected.length === 0
          ? 'Chưa chọn ghế nào.'
          : 'Đã chọn ' + selected.length + ' ghế, tổng tiền ' + vnd(total) + '.';
      }

      if (checkoutBtn) {
        if (selected.length === 0 || expired) {
          checkoutBtn.setAttribute('disabled', 'disabled');
        } else {
          checkoutBtn.removeAttribute('disabled');
        }
      }
    }

    function paintTimer() {
      if (timerValue) timerValue.textContent = formatClock(remaining);
      if (timerFill) timerFill.style.width = (remaining / HOLD_SECONDS * 100) + '%';
      if (timerBox) {
        timerBox.classList.toggle('timer--warn', remaining <= 60 && remaining > 0);
        timerBox.classList.toggle('timer--expired', remaining === 0);
      }
      if (timerLabel) {
        timerLabel.textContent = remaining === 0 ? 'đã hết thời gian giữ ghế' : 'thời gian giữ ghế';
      }
    }

    function startTimer() {
      if (tick) window.clearInterval(tick);
      tick = window.setInterval(function () {
        remaining -= 1;
        if (remaining <= 0) {
          remaining = 0;
          window.clearInterval(tick);
          tick = null;
          expire();
        }
        paintTimer();
      }, 1000);
    }

    function expire() {
      expired = true;
      var released = selected.slice();
      selected = [];
      $$('.seat--mine', grid).forEach(function (seat) {
        seat.setAttribute('data-state', 'free');
        seat.setAttribute('aria-pressed', 'false');
        seat.classList.remove('seat--mine');
        seat.classList.add('seat--expired');
        seat.setAttribute('aria-label', seatLabel(seat.getAttribute('data-seat'), 'free'));
        seat.textContent = seat.getAttribute('data-seat').replace(/^[A-H]/, '');
      });
      updateSummary();
      if (notice) {
        notice.hidden = false;
        notice.className = 'notice notice--warn';
      }
      if (noticeText) {
        noticeText.textContent = released.length
          ? 'Hết 5 phút giữ ghế. ' + released.join(', ') + ' đã được trả lại cho khách khác. Bạn có thể chọn lại ghế khác.'
          : 'Hết 5 phút giữ ghế. Bạn có thể chọn lại ghế.';
      }
      if (restartBtn) restartBtn.focus();
    }

    if (restartBtn) {
      restartBtn.addEventListener('click', function () {
        expired = false;
        remaining = HOLD_SECONDS;
        $$('.seat--expired', grid).forEach(function (seat) { seat.classList.remove('seat--expired'); });
        if (notice) notice.hidden = true;
        paintTimer();
        startTimer();
        updateSummary();
      });
    }

    if (simulateBtn) {
      simulateBtn.addEventListener('click', function () {
        remaining = 3;
        paintTimer();
      });
    }

    if (checkoutBtn) {
      checkoutBtn.addEventListener('click', function () {
        if (selected.length === 0 || expired) return;
        window.location.href = 'checkout.html?seats=' + encodeURIComponent(selected.join(',')) +
          '&show=' + encodeURIComponent(showTime) + '&day=' + dayNumber;
      });
    }

    if (sampleBtn) {
      sampleBtn.addEventListener('click', function () {
        ['C3', 'C4'].forEach(function (id) {
          var seat = $('[data-seat="' + id + '"]', grid);
          if (seat && seat.getAttribute('data-state') === 'free') toggleSeat(seat);
        });
      });
    }

    renderGrid();
    paintTimer();
    updateSummary();
    startTimer();
  }

  /* ---------------- THANH TOÁN: kiểm tra biểu mẫu + trạng thái gửi ---------------- */

  function initCheckout() {
    var form = $('#checkout-form');
    if (!form) return;

    var query = params();
    var seats = (query.seats || '').split(',').filter(Boolean);
    var total = seats.reduce(function (sum, id) { return sum + seatPrice(id.toUpperCase()); }, 0);

    var sumSeats = $('#sum-seats');
    var sumSeatsEmpty = $('#sum-seats-empty');
    var sumBranch = $('#sum-branch');
    var sumRoom = $('#sum-room');
    var sumShow = $('#sum-show');
    var sumTotal = $('#sum-total');
    var submitBtn = $('#submit-order');
    var emptyNotice = $('#checkout-empty');
    var errorBanner = $('#server-error');
    var successPanel = $('#order-success');
    var simulate = $('#simulate-error');

    if (sumBranch) sumBranch.textContent = 'CineSmart Nguyễn Huệ';
    if (sumRoom) sumRoom.textContent = 'Phòng 1';
    if (sumShow) sumShow.textContent = query.show || '18:45';

    if (sumSeats) {
      sumSeats.innerHTML = '';
      seats.forEach(function (id) {
        var row = document.createElement('li');
        row.className = 'summary__row';
        row.innerHTML = '<span class="summary__key">Ghế ' + id + ' (' + SEAT_LABELS[seatType(id.charAt(0))] + ')</span>' +
          '<span class="summary__val summary__val--num">' + vnd(seatPrice(id.charAt(0))) + '</span>';
        sumSeats.appendChild(row);
      });
    }

    if (sumSeatsEmpty) sumSeatsEmpty.hidden = seats.length !== 0;
    if (sumTotal) sumTotal.textContent = vnd(total);
    if (emptyNotice) emptyNotice.hidden = seats.length !== 0;
    if (submitBtn) {
      if (seats.length === 0) submitBtn.setAttribute('disabled', 'disabled');
      else submitBtn.removeAttribute('disabled');
    }

    /* Hiện/ẩn phần nhập thẻ theo phương thức thanh toán */
    var cardFields = $('#card-fields');
    var methodInputs = $$('input[name="payment"]');
    function syncMethod() {
      var selected = methodInputs.filter(function (i) { return i.checked; })[0];
      var isCard = selected && selected.value === 'card';
      if (cardFields) cardFields.hidden = !isCard;
    }
    methodInputs.forEach(function (input) { input.addEventListener('change', syncMethod); });
    syncMethod();

    /* Kiểm tra dữ liệu */
    var rules = {
      name: function (value) {
        if (!value.trim()) return 'Vui lòng nhập họ và tên.';
        if (value.trim().length < 2) return 'Họ và tên quá ngắn.';
        return '';
      },
      email: function (value) {
        if (!value.trim()) return 'Vui lòng nhập email để nhận vé.';
        if (!/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(value.trim())) return 'Email chưa đúng định dạng, ví dụ ten@ban.com.';
        return '';
      },
      phone: function (value) {
        var v = value.replace(/[\s.-]/g, '');
        if (!v) return 'Vui lòng nhập số điện thoại.';
        if (!/^(0|\+84)\d{9,10}$/.test(v)) return 'Số điện thoại phải gồm 10 số, bắt đầu bằng 0 hoặc +84.';
        return '';
      },
      card: function (value) {
        if (!value.trim()) return 'Vui lòng nhập số thẻ.';
        if (!/^\d{4}\s?\d{4}\s?\d{4}\s?\d{4}$/.test(value.trim())) return 'Số thẻ phải gồm 16 chữ số.';
        return '';
      },
      expiry: function (value) {
        if (!value.trim()) return 'Vui lòng nhập ngày hết hạn.';
        if (!/^(0[1-9]|1[0-2])\/\d{2}$/.test(value.trim())) return 'Ngày hết hạn phải có dạng MM/YY.';
        return '';
      }
    };

    var fields = [
      { id: 'field-name', input: 'name', rule: rules.name },
      { id: 'field-email', input: 'email', rule: rules.email },
      { id: 'field-phone', input: 'phone', rule: rules.phone },
      { id: 'field-card', input: 'card', rule: rules.card },
      { id: 'field-expiry', input: 'expiry', rule: rules.expiry }
    ];

    function validateField(field) {
      var input = $('[name="' + field.input + '"]', form);
      if (!input) return true;
      var box = input.closest('.field') || input.closest('.checkline-wrap');
      var message = field.rule(input.value);
      var errorNode = box ? $('.field__error', box) : null;

      if (message) {
        input.setAttribute('aria-invalid', 'true');
        if (errorNode) {
          errorNode.textContent = message;
          input.setAttribute('aria-describedby', errorNode.id);
        }
        return false;
      }

      input.removeAttribute('aria-invalid');
      if (errorNode) {
        errorNode.textContent = '';
        input.removeAttribute('aria-describedby');
      }
      return true;
    }

    fields.forEach(function (field) {
      var input = $('[name="' + field.input + '"]', form);
      if (!input) return;
      input.addEventListener('blur', function () {
        if (input.value.trim() !== '') validateField(field);
      });
    });

    form.addEventListener('submit', function (event) {
      event.preventDefault();
      if (errorBanner) errorBanner.hidden = true;

      var isCard = ($('input[name="payment"]:checked', form) || {}).value === 'card';
      var toCheck = fields.filter(function (field) {
        if (field.input === 'card' || field.input === 'expiry') return isCard;
        return true;
      });

      var firstInvalid = null;
      toCheck.forEach(function (field) {
        if (!validateField(field) && !firstInvalid) firstInvalid = field;
      });

      if (firstInvalid) {
        var input = $('[name="' + firstInvalid.input + '"]', form);
        if (input) input.focus();
        return;
      }

      var terms = $('[name="terms"]', form);
      var termsError = $('#field-terms');
      if (terms && !terms.checked) {
        if (termsError) {
          termsError.textContent = 'Bạn cần đồng ý điều khoản hoàn tiền để hoàn tất đặt vé.';
          terms.setAttribute('aria-invalid', 'true');
          terms.setAttribute('aria-describedby', 'field-terms');
        }
        terms.focus();
        return;
      }
      if (termsError) termsError.textContent = '';
      if (terms) terms.removeAttribute('aria-invalid');

      if (simulate && simulate.checked) {
        if (errorBanner) errorBanner.hidden = false;
        if (errorBanner) errorBanner.focus();
        return;
      }

      submitBtn.classList.add('btn--busy');
      submitBtn.setAttribute('aria-busy', 'true');
      submitBtn.textContent = 'Đang xử lý';

      window.setTimeout(function () {
        submitBtn.classList.remove('btn--busy');
        submitBtn.removeAttribute('aria-busy');
        submitBtn.textContent = 'Thanh toán';
        if (successPanel) {
          successPanel.hidden = false;
          successPanel.setAttribute('tabindex', '-1');
          successPanel.focus();
        }
        if ($('#order-code')) {
          $('#order-code').textContent = 'CB-2026-' + (seats.length + 1024);
        }
      }, 1400);
    });
  }

  /* ---------------- Khởi động ---------------- */

  function boot() {
    initHome();
    initMovie();
    initSeatmap();
    initCheckout();

    var year = $('#current-year');
    if (year) year.textContent = '2026';
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
