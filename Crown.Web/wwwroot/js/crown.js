// Подтверждение опасных действий: <button data-confirm="Текст?">.
document.addEventListener('click', function (e) {
    var el = e.target.closest('[data-confirm]');
    if (el && !window.confirm(el.getAttribute('data-confirm'))) {
        e.preventDefault();
    }
});

// Клик по строке таблицы → data-href (как table-row-action в Ratchet).
document.addEventListener('click', function (e) {
    if (e.target.closest('a, button, input, label, select, textarea, .table-row-actions')) {
        return;
    }
    var row = e.target.closest('tr.table-row-action[data-href]');
    if (!row) {
        return;
    }
    var href = row.getAttribute('data-href');
    if (href) {
        window.location.href = href;
    }
});

document.addEventListener('keydown', function (e) {
    if (e.key !== 'Enter' && e.key !== ' ') {
        return;
    }
    var row = e.target.closest('tr.table-row-action[data-href]');
    if (!row || e.target !== row) {
        return;
    }
    e.preventDefault();
    var href = row.getAttribute('data-href');
    if (href) {
        window.location.href = href;
    }
});
