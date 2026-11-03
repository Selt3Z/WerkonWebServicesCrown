// Подтверждение опасных действий: <button data-confirm="Текст?">.
document.addEventListener('click', function (e) {
    var el = e.target.closest('[data-confirm]');
    if (el && !window.confirm(el.getAttribute('data-confirm'))) {
        e.preventDefault();
    }
});

// Клик по строке/карточке → data-href (как table-row-action в Ratchet).
document.addEventListener('click', function (e) {
    if (e.target.closest('a, button, input, label, select, textarea, .table-row-actions')) {
        return;
    }
    var row = e.target.closest('.table-row-action[data-href]');
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
    var row = e.target.closest('.table-row-action[data-href]');
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
    var mqMobile = window.matchMedia('(max-width: 640px)');

    function bind(host) {
        if (host.dataset.infiniteBound === '1') {
            return;
        }

        var moreUrl = host.getAttribute('data-more-url');
        if (!moreUrl) {
            return;
        }

        var skip = parseInt(host.getAttribute('data-skip') || '0', 10) || 0;
        var loading = false;
        var isCards = host.classList.contains('schedule-mobile-cards');
        var wrap = host.closest('.crown-table-wrap') || host;
        var sentinel = document.querySelector('.infinite-scroll-sentinel');
        var observer = null;
        if (!sentinel) {
            sentinel = document.createElement('div');
            sentinel.className = 'infinite-scroll-sentinel';
            if (wrap.parentNode) {
                wrap.parentNode.insertBefore(sentinel, wrap.nextSibling);
            } else {
                return;
            }
        }

        function isActive() {
            // На мобиле крутим карточки, на десктопе — tbody; sentinel общий.
            return isCards ? mqMobile.matches : !mqMobile.matches;
        }

        function readHasMore() {
            return host.getAttribute('data-has-more') === 'true';
        }

        // Таблица и карточки — один список: общий флаг, иначе спиннер зависает на парном host.
        function publishHasMore(value) {
            document.querySelectorAll('[data-infinite-scroll]').forEach(function (el) {
                el.setAttribute('data-has-more', value ? 'true' : 'false');
            });
        }

        function setSentinel() {
            if (!isActive()) {
                return;
            }

            if (readHasMore()) {
                sentinel.hidden = false;
                sentinel.innerHTML = loading
                    ? '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span><span class="ms-2">Загрузка…</span>'
                    : '';
            } else {
                sentinel.hidden = true;
                sentinel.innerHTML = '';
                if (observer) {
                    observer.disconnect();
                    observer = null;
                }
            }
        }

        async function loadMore() {
            if (!isActive() || !readHasMore() || loading) {
                return;
            }

            loading = true;
            setSentinel();
            var prevSkip = skip;
            try {
                var url = new URL(moreUrl, window.location.origin);
                url.searchParams.set('skip', String(skip));
                var res = await fetch(url.toString(), {
                    credentials: 'same-origin',
                    headers: { 'Accept': 'text/html' }
                });
                if (!res.ok) {
                    publishHasMore(false);
                    return;
                }

                var html = await res.text();
                var more = (res.headers.get('X-Has-More') || '').toLowerCase() === 'true';
                if (html && html.trim()) {
                    if (isCards) {
                        var empty = host.querySelector('p.text-muted');
                        if (empty) {
                            empty.remove();
                        }
                        host.insertAdjacentHTML('beforeend', html);
                        skip = host.querySelectorAll('.schedule-card').length;
                    } else {
                        var emptyRow = host.querySelector('.crown-empty-row');
                        if (emptyRow) {
                            emptyRow.remove();
                        }
                        host.insertAdjacentHTML('beforeend', html);
                        skip = host.querySelectorAll('tr:not(.crown-empty-row)').length;
                    }
                    host.setAttribute('data-skip', String(skip));
                    // skip не вырос — ответ пустой/дубль, стоп (иначе вечный спиннер).
                    if (skip <= prevSkip) {
                        more = false;
                    }
                    publishHasMore(more);
                } else {
                    publishHasMore(false);
                }
            } catch (err) {
                console.warn('infinite scroll', err);
                publishHasMore(false);
            } finally {
                loading = false;
                setSentinel();
            }
        }

        host.dataset.infiniteBound = '1';
        setSentinel();
        if (!readHasMore()) {
            return;
        }

        function ensureObserver() {
            if (observer || !readHasMore()) {
                return;
            }
            observer = new IntersectionObserver(function (entries) {
                for (var i = 0; i < entries.length; i++) {
                    if (entries[i].isIntersecting) {
                        loadMore();
                    }
                }
            }, { rootMargin: '300px' });
            observer.observe(sentinel);
        }

        ensureObserver();

        mqMobile.addEventListener('change', function () {
            setSentinel();
            if (isActive()) {
                ensureObserver();
            }
        });
    }

    function initAll() {
        document.querySelectorAll('[data-infinite-scroll]').forEach(bind);
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
