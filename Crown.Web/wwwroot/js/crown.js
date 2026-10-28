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

// Infinite scroll списков (как ratchetInfiniteScroll): догрузка кусков при появлении sentinel.
(function () {
    function bind(tbody) {
        if (tbody.dataset.infiniteBound === '1') {
            return;
        }

        var moreUrl = tbody.getAttribute('data-more-url');
        if (!moreUrl) {
            return;
        }

        var skip = parseInt(tbody.getAttribute('data-skip') || '0', 10) || 0;
        var hasMore = tbody.getAttribute('data-has-more') === 'true';
        var loading = false;
        var wrap = tbody.closest('.crown-table-wrap');
        var sentinel = wrap && wrap.nextElementSibling;
        if (!sentinel || !sentinel.classList.contains('infinite-scroll-sentinel')) {
            sentinel = document.createElement('div');
            sentinel.className = 'infinite-scroll-sentinel';
            if (wrap && wrap.parentNode) {
                wrap.parentNode.insertBefore(sentinel, wrap.nextSibling);
            } else {
                return;
            }
        }

        function setSentinel() {
            if (hasMore) {
                sentinel.hidden = false;
                sentinel.innerHTML = loading
                    ? '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span><span class="ms-2">Загрузка…</span>'
                    : '';
            } else {
                sentinel.hidden = true;
                sentinel.innerHTML = '';
            }
        }

        async function loadMore() {
            if (!hasMore || loading) {
                return;
            }

            loading = true;
            setSentinel();
            try {
                var url = new URL(moreUrl, window.location.origin);
                url.searchParams.set('skip', String(skip));
                var res = await fetch(url.toString(), {
                    credentials: 'same-origin',
                    headers: { 'Accept': 'text/html' }
                });
                if (!res.ok) {
                    hasMore = false;
                    return;
                }

                var html = await res.text();
                hasMore = (res.headers.get('X-Has-More') || '').toLowerCase() === 'true';
                if (html && html.trim()) {
                    var empty = tbody.querySelector('.crown-empty-row');
                    if (empty) {
                        empty.remove();
                    }
                    tbody.insertAdjacentHTML('beforeend', html);
                    skip = tbody.querySelectorAll('tr:not(.crown-empty-row)').length;
                    tbody.setAttribute('data-skip', String(skip));
                } else {
                    hasMore = false;
                }
            } catch (err) {
                console.warn('infinite scroll', err);
                hasMore = false;
            } finally {
                loading = false;
                tbody.setAttribute('data-has-more', hasMore ? 'true' : 'false');
                setSentinel();
            }
        }

        tbody.dataset.infiniteBound = '1';
        setSentinel();
        if (!hasMore) {
            return;
        }

        var observer = new IntersectionObserver(function (entries) {
            for (var i = 0; i < entries.length; i++) {
                if (entries[i].isIntersecting) {
                    loadMore();
                }
            }
        }, { rootMargin: '300px' });
        observer.observe(sentinel);
    }

    function initAll() {
        document.querySelectorAll('tbody[data-infinite-scroll]').forEach(bind);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAll);
    } else {
        initAll();
    }
})();

// Фильтры: select/checkbox — сразу submit, поиск по тексту остаётся на «Найти»/Enter.
document.addEventListener('change', function (e) {
    var el = e.target;
    if (!el || (el.tagName !== 'SELECT' && el.type !== 'checkbox')) {
        return;
    }
    var form = el.closest('form.list-filters');
    if (form) {
        form.requestSubmit();
    }
});
