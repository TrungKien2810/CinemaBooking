/* ==========================================================================
   CinemaBooking.Smart — hướng J — app.js
   Tĩnh, không build, chạy được bằng file://. Không thư viện.
   Việc: vẽ vạch chia độ, chọn ghế + đồng hồ giữ ghế, lọc suất, lọc phim,
   validate phiếu thanh toán.
   ========================================================================== */
(function () {
  "use strict";

  var $ = function (sel, root) { return (root || document).querySelector(sel); };
  var $$ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };

  var VND = new Intl.NumberFormat("vi-VN");
  function money(n) { return VND.format(n) + " ₫"; }

  /* ------------------------------------------------------------------------
     1. Vạch chia độ — "còn 12 trên 96" vẽ thành đại lượng, không chỉ là chữ.
        Mỗi chỗ trống là một vạch; vạch còn trống in đậm, vạch đã bán để mờ.
        Ẩn với trình đọc màn hình: con số thật nằm ở chữ ngay cạnh.
     ---------------------------------------------------------------------- */
  $$("[data-gauge]").forEach(function (el) {
    var total = parseInt(el.getAttribute("data-total"), 10) || 0;
    var left = parseInt(el.getAttribute("data-left"), 10) || 0;
    if (!total) return;
    el.setAttribute("aria-hidden", "true");
    var frag = document.createDocumentFragment();
    for (var i = 0; i < total; i++) {
      var tick = document.createElement("span");
      tick.className = "gauge__tick" + (i < left ? " is-on" : "");
      frag.appendChild(tick);
    }
    el.appendChild(frag);
  });

  /* ------------------------------------------------------------------------
     2. Sơ đồ ghế
     ---------------------------------------------------------------------- */
  var grid = $("[data-seatgrid]");
  if (grid) {
    var PRICE = { thuong: 95000, vip: 114000, doi: 190000 };
    var KIND = { thuong: "thường", vip: "VIP", doi: "đôi" };
    var STATE = {
      free: "còn trống",
      mine: "bạn đang giữ",
      held: "người khác đang giữ",
      booked: "đã đặt"
    };
    var HOLD_SECONDS = 300;

    var seats = $$(".seat", grid);
    var picked = [];
    var remain = HOLD_SECONDS;
    var ticker = null;
    var expired = false;

    var out = {
      time: $("#hold-time"),
      bar: $("#hold-bar"),
      list: $("#picked"),
      codes: $("#seat-codes"),
      total: $("#sum-total"),
      note: $("#seat-note"),
      cta: $("#to-checkout"),
      clear: $("#clear-seats"),
      msg: $("#seat-msg")
    };

    function seatLabel(code, kind, state) {
      return "Ghế " + code + ", hạng " + KIND[kind] + ", " + money(PRICE[kind]) + " — " + STATE[state];
    }

    function paint(btn, state) {
      var kind = btn.getAttribute("data-kind");
      btn.setAttribute("data-state", state);
      btn.className = "seat seat--" + state;
      btn.setAttribute("aria-label", seatLabel(btn.getAttribute("data-code"), kind, state));
      if (state === "mine") {
        btn.setAttribute("aria-pressed", "true");
      } else {
        btn.setAttribute("aria-pressed", "false");
      }
      if (state === "held") {
        btn.setAttribute("aria-disabled", "true");
      } else {
        btn.removeAttribute("aria-disabled");
      }
    }

    seats.forEach(function (btn) {
      paint(btn, btn.getAttribute("data-state"));
      if (btn.getAttribute("data-state") === "booked") btn.disabled = true;
    });

    function total() {
      return picked.reduce(function (sum, code) {
        var btn = grid.querySelector('[data-code="' + code + '"]');
        return sum + PRICE[btn.getAttribute("data-kind")];
      }, 0);
    }

    function kinds() {
      var set = {};
      picked.forEach(function (code) {
        var btn = grid.querySelector('[data-code="' + code + '"]');
        set[KIND[btn.getAttribute("data-kind")]] = true;
      });
      return Object.keys(set);
    }

    function render() {
      if (out.total) out.total.textContent = money(total());
      if (out.codes) out.codes.textContent = picked.length ? picked.slice().sort().join(" ") : "chưa chọn ghế";
      if (out.note) {
        out.note.textContent = picked.length
          ? picked.length + " ghế, hạng " + kinds().join(" và ")
          : "Chưa chọn ghế nào";
      }
      if (out.list) {
        out.list.innerHTML = "";
        picked.slice().sort().forEach(function (code) {
          var btn = grid.querySelector('[data-code="' + code + '"]');
          var kind = btn.getAttribute("data-kind");
          var li = document.createElement("li");
          var b = document.createElement("button");
          b.type = "button";
          b.className = "picked__chip";
          b.setAttribute("data-remove", code);
          b.setAttribute("aria-label", "Bỏ chọn ghế " + code);
          b.innerHTML =
            '<span class="picked__code mono">' + code + "</span>" +
            '<span class="picked__price mono">' + money(PRICE[kind]) + "</span>" +
            '<span class="picked__x" aria-hidden="true"></span>';
          li.appendChild(b);
          out.list.appendChild(li);
        });
      }
      if (out.cta) out.cta.disabled = picked.length === 0;
    }

    function showMsg(text) {
      if (out.msg) out.msg.textContent = text;
    }

    function drawClock() {
      if (!out.time) return;
      var m = Math.floor(remain / 60);
      var s = remain % 60;
      out.time.textContent = (m < 10 ? "0" : "") + m + ":" + (s < 10 ? "0" : "") + s;
      if (out.bar) out.bar.style.width = (remain / HOLD_SECONDS) * 100 + "%";
    }

    function stopClock() {
      if (ticker) { clearInterval(ticker); ticker = null; }
    }

    function startClock() {
      stopClock();
      remain = HOLD_SECONDS;
      drawClock();
      ticker = setInterval(function () {
        remain -= 1;
        if (remain <= 0) {
          remain = 0;
          drawClock();
          expire();
          return;
        }
        drawClock();
      }, 1000);
    }

    function expire() {
      stopClock();
      var lost = picked.slice();
      lost.forEach(function (code) {
        paint(grid.querySelector('[data-code="' + code + '"]'), "free");
      });
      picked = [];
      expired = true;
      render();
      showMsg(
        "Đã hết 5 phút giữ ghế. Ghế " + lost.join(", ") +
        " đã được trả lại. Mời bạn chọn lại và bấm giữ ghế lần nữa."
      );
      if (out.msg) out.msg.classList.add("is-strong");
      if (out.time) out.time.classList.add("is-over");
    }

    seats.forEach(function (btn) {
      var initial = btn.getAttribute("data-state");
      if (initial === "mine") picked.push(btn.getAttribute("data-code"));
      btn.addEventListener("click", function () {
        var state = btn.getAttribute("data-state");
        var code = btn.getAttribute("data-code");
        if (state === "booked") return;
        if (state === "held") {
          showMsg("Ghế " + code + " đang được người khác giữ. Bạn chọn ghế khác nhé.");
          if (out.msg) out.msg.classList.remove("is-strong");
          return;
        }
        if (state === "mine") {
          picked = picked.filter(function (c) { return c !== code; });
          paint(btn, "free");
          showMsg("Đã bỏ giữ ghế " + code + ".");
        } else {
          picked.push(code);
          paint(btn, "mine");
          showMsg("Đã giữ ghế " + code + ". Bạn có 5 phút để hoàn tất.");
          if (expired) { expired = false; if (out.time) out.time.classList.remove("is-over"); }
        }
        if (out.msg) out.msg.classList.remove("is-strong");
        render();
        if (picked.length && !ticker) startClock();
        if (!picked.length) stopClock();
      });
    });

    if (out.list) {
      out.list.addEventListener("click", function (e) {
        var chip = e.target.closest("[data-remove]");
        if (!chip) return;
        var code = chip.getAttribute("data-remove");
        paint(grid.querySelector('[data-code="' + code + '"]'), "free");
        picked = picked.filter(function (c) { return c !== code; });
        render();
        showMsg("Đã bỏ giữ ghế " + code + ".");
        if (!picked.length) stopClock();
      });
    }

    if (out.clear) {
      out.clear.addEventListener("click", function () {
        picked.forEach(function (code) {
          paint(grid.querySelector('[data-code="' + code + '"]'), "free");
        });
        picked = [];
        stopClock();
        render();
        showMsg("Đã bỏ chọn toàn bộ ghế. Đồng hồ giữ ghế dừng.");
        if (out.time) out.time.classList.remove("is-over");
      });
    }

    if (out.cta) {
      out.cta.addEventListener("click", function () {
        if (picked.length) window.location.href = "checkout.html";
      });
    }

    render();
    drawClock();
    startClock();
  }

  /* ------------------------------------------------------------------------
     3. Trang phim — chọn ngày, lọc thành phố / chi nhánh
     ---------------------------------------------------------------------- */
  var daybar = $("#daybar");
  if (daybar) {
    var days = $$(".day", daybar);
    days.forEach(function (d) {
      d.addEventListener("click", function () {
        days.forEach(function (x) { x.setAttribute("aria-pressed", String(x === d)); });
      });
    });

    var branches = $$("[data-branch]");
    var cityBtns = $$("[data-city-filter]");
    var branchSelect = $("#branch-select");
    var slotNote = $("#slot-note");
    var empty = $("#slots-empty");
    var city = "all";

    function applyFilter() {
      var only = branchSelect ? branchSelect.value : "all";
      var shown = 0;
      branches.forEach(function (b) {
        var okCity = city === "all" || b.getAttribute("data-city") === city;
        var okBranch = only === "all" || b.getAttribute("data-branch") === only;
        var on = okCity && okBranch;
        b.hidden = !on;
        if (on) shown += $$(".show:not(.show--full)", b).length;
      });
      if (slotNote) {
        slotNote.textContent = shown
          ? shown + " suất còn chỗ trong " + branches.filter(function (b) { return !b.hidden; }).length + " chi nhánh"
          : "Không còn suất nào khớp bộ lọc";
      }
      if (empty) empty.hidden = shown > 0;
    }

    cityBtns.forEach(function (b) {
      b.addEventListener("click", function () {
        city = b.getAttribute("data-city-filter");
        cityBtns.forEach(function (x) { x.setAttribute("aria-pressed", String(x === b)); });
        applyFilter();
      });
    });
    if (branchSelect) branchSelect.addEventListener("change", applyFilter);
    applyFilter();
  }

  /* ------------------------------------------------------------------------
     4. Trang chủ — tìm phim + lọc thể loại
     ---------------------------------------------------------------------- */
  var films = $("#films");
  if (films) {
    var items = $$(".film", films);
    var q = $("#film-search");
    var chips = $$("[data-genre-filter]");
    var emptyBox = $("#films-empty");
    var genre = "all";

    var plain = function (s) {
      return s.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase().replace(/đ/g, "d");
    };

    function applyFilms() {
      var needle = q ? plain(q.value.trim()) : "";
      var glued = needle.replace(/\s+/g, "");
      var shown = 0;
      items.forEach(function (it) {
        var hay = plain(it.getAttribute("data-title") + " " + it.getAttribute("data-genre-label"));
        var okGenre = genre === "all" || it.getAttribute("data-genre") === genre;
        var okText = !needle || hay.indexOf(needle) !== -1 || hay.replace(/\s+/g, "").indexOf(glued) !== -1;
        var on = okGenre && okText;
        it.hidden = !on;
        if (on) shown += 1;
      });
      if (emptyBox) emptyBox.hidden = shown > 0;
      var count = $("#film-count");
      if (count) count.textContent = shown + "/5 phim";
    }

    if (q) q.addEventListener("input", applyFilms);
    chips.forEach(function (c) {
      c.addEventListener("click", function () {
        genre = c.getAttribute("data-genre-filter");
        chips.forEach(function (x) { x.setAttribute("aria-pressed", String(x === c)); });
        applyFilms();
      });
    });
    applyFilms();
  }

  /* ------------------------------------------------------------------------
     5. Phiếu thanh toán — validate tại chỗ, gửi giả lập, banner lỗi máy chủ
     ---------------------------------------------------------------------- */
  var form = $("#pay-form");
  if (form) {
    var RULES = {
      name: function (v) {
        return v.trim().length >= 2 ? "" : "Vui lòng nhập họ và tên, ít nhất 2 ký tự.";
      },
      email: function (v) {
        return /^[^\s@]+@[^\s@]+\.[a-zA-Z]{2,}$/.test(v.trim())
          ? ""
          : "Email chưa đúng định dạng, ví dụ ten@example.com.";
      },
      phone: function (v) {
        return /^0\d{9}$/.test(v.trim().replace(/[\s.\-]/g, ""))
          ? ""
          : "Số điện thoại cần 10 chữ số và bắt đầu bằng 0, ví dụ 0901234567.";
      }
    };

    var submit = $("#pay-submit");
    var banner = $("#server-error");
    var state = $("#pay-state");
    var idleLabel = submit ? submit.getAttribute("data-idle") : "";
    var busy = false;

    function fieldError(input) {
      var key = input.getAttribute("data-rule");
      var box = document.getElementById("err-" + key);
      var msg = RULES[key](input.value);
      if (box) box.textContent = msg;
      if (msg) {
        input.setAttribute("aria-invalid", "true");
      } else {
        input.removeAttribute("aria-invalid");
      }
      return !msg;
    }

    var inputs = $$("input[data-rule]", form);
    inputs.forEach(function (input) {
      input.addEventListener("blur", function () {
        if (input.value.trim() || input.getAttribute("aria-invalid") === "true") fieldError(input);
      });
      input.addEventListener("input", function () {
        if (input.getAttribute("aria-invalid") === "true") fieldError(input);
      });
    });

    form.addEventListener("submit", function (e) {
      e.preventDefault();
      if (busy) return;

      var firstBad = null;
      inputs.forEach(function (input) {
        var ok = fieldError(input);
        if (!ok && !firstBad) firstBad = input;
      });
      if (firstBad) {
        if (state) state.textContent = "Còn " + inputs.filter(function (i) {
          return i.getAttribute("aria-invalid") === "true";
        }).length + " ô cần sửa trước khi thanh toán.";
        firstBad.focus();
        return;
      }
      if (banner) banner.hidden = true;
      if (state) state.textContent = "Đang gửi yêu cầu thanh toán.";

      busy = true;
      submit.disabled = true;
      submit.setAttribute("aria-busy", "true");
      submit.textContent = "Đang xử lý…";

      window.setTimeout(function () {
        busy = false;
        submit.disabled = false;
        submit.removeAttribute("aria-busy");
        submit.textContent = idleLabel.trim();
        if (state) {
          state.textContent =
            "Máy chủ thanh toán không phản hồi. Ghế của bạn vẫn được giữ thêm 4:12.";
        }
        if (banner) banner.hidden = false;
      }, 1200);
    });
  }
})();
