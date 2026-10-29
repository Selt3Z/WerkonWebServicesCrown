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
    function bind(host) {
        if (host.dataset.infiniteBound === '1') {
            return;
        }

        var moreUrl = host.getAttribute('data-more-url');
        if (!moreUrl) {
            return;
        }

        var skip = parseInt(host.getAttribute('data-skip') || '0', 10) || 0;
        var hasMore = host.getAttribute('data-has-more') === 'true';
        var loading = false;
        var isCards = host.classList.contains('schedule-mobile-cards');
        var wrap = host.closest('.crown-table-wrap') || host;
        var sentinel = document.querySelector('.infinite-scroll-sentinel');
        if (!sentinel) {
            sentinel = document.createElement('div');
            sentinel.className = 'infinite-scroll-sentinel';
            if (wrap.parentNode) {
                wrap.parentNode.insertBefore(sentinel, wrap.nextSibling);
            } else {
                return;
            }
        }

        function setSentinel() {
            // На мобиле крутим карточки, на десктопе — tbody; sentinel общий.
            var active = isCards
                ? window.matchMedia('(max-width: 640px)').matches
                : !window.matchMedia('(max-width: 640px)').matches;
            if (!active) {
                return;
            }

            if (hasMore) {
                sentinel.hidden = false;
                sentinel.innerHTML = loading
                    ? '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span><span class="ms-2">Загрузка…</span>'
                    : '';
            } else if (!document.querySelector('[data-infinite-scroll][data-has-more="true"]')) {
                sentinel.hidden = true;
                sentinel.innerHTML = '';
            }
        }

        async function loadMore() {
            var active = isCards
                ? window.matchMedia('(max-width: 640px)').matches
                : !window.matchMedia('(max-width: 640px)').matches;
            if (!active || !hasMore || loading) {
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
                } else {
                    hasMore = false;
                }
            } catch (err) {
                console.warn('infinite scroll', err);
                hasMore = false;
            } finally {
                loading = false;
                host.setAttribute('data-has-more', hasMore ? 'true' : 'false');
                setSentinel();
            }
        }

        host.dataset.infiniteBound = '1';
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
