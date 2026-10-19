// Thông báo mặc định của jQuery Validate bằng tiếng Việt.
//
// Phần lớn lỗi đã có thông báo riêng từ attribute ([Required(ErrorMessage = …)]) được render
// thành data-val-*, nên chỉ những luật không có data-val-* đi kèm mới rơi vào bảng này —
// ví dụ min/max/step do HTML5 sinh cho <input type="number">. Nạp SAU jquery.validate.
(function ($) {
    if (!$ || !$.validator) return;

    $.extend($.validator.messages, {
        required: 'Vui lòng nhập trường này.',
        email: 'Email không hợp lệ.',
        url: 'URL không hợp lệ.',
        date: 'Ngày không hợp lệ.',
        dateISO: 'Ngày không hợp lệ (yyyy-mm-dd).',
        number: 'Vui lòng nhập số.',
        digits: 'Chỉ được nhập chữ số.',
        equalTo: 'Giá trị nhập lại không khớp.',
        maxlength: $.validator.format('Tối đa {0} ký tự.'),
        minlength: $.validator.format('Tối thiểu {0} ký tự.'),
        rangelength: $.validator.format('Độ dài từ {0} đến {1} ký tự.'),
        range: $.validator.format('Giá trị từ {0} đến {1}.'),
        max: $.validator.format('Giá trị tối đa là {0}.'),
        min: $.validator.format('Giá trị tối thiểu là {0}.'),
        step: $.validator.format('Giá trị phải là bội số của {0}.')
    });
})(window.jQuery);
