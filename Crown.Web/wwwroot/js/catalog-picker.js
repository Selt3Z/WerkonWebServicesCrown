/* Поиск по справочнику (act/work): локальный список или AJAX; свободный текст = новый пункт. */
(() => {
    function norm(s) {
        return String(s || '')
            .toLowerCase()
            .replace(/ё/g, 'е')
            .replace(/\s+/g, ' ')
            .trim();
    }

    function escapeHtml(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    /**
     * @param {HTMLElement} root .catalog-picker
     * @param {{ items?: {id:string,text:string}[], searchUrl?: string, onChange?: Function }} opts
     */
    function mountCatalogPicker(root, opts) {
        if (!root || root.dataset.pickerReady === '1') return;
        const items = Array.isArray(opts?.items) ? opts.items : [];
        const searchUrl = opts?.searchUrl || '';
        const idInput = root.querySelector('.catalog-picker-id');
        const manualInput = root.querySelector('.catalog-picker-manual');
        const textInput = root.querySelector('.catalog-picker-input');
        let menu = root.querySelector('.catalog-picker-menu');
        if (!idInput || !manualInput || !textInput) return;

        if (!menu) {
            menu = document.createElement('div');
            menu.className = 'catalog-picker-menu d-none';
            root.appendChild(menu);
        }

        root.dataset.pickerReady = '1';
        root._catalogItems = items;
        let searchTimer = 0;
        let searchToken = 0;

        function setManual(text) {
            idInput.value = '';
            manualInput.value = 'true';
            textInput.value = text;
            opts?.onChange?.({ id: null, text, manual: true });
        }

        function setSelected(item) {
            idInput.value = item.id;
            manualInput.value = 'false';
            textInput.value = item.text;
            // Запоминаем выбранный пункт в локальном кэше (чтобы не потерять после AJAX).
            const list = root._catalogItems || [];
            if (!list.some(x => x.id === item.id)) {
                root._catalogItems = [item, ...list].slice(0, 80);
            }
            opts?.onChange?.({ id: item.id, text: item.text, manual: false });
        }

        function hide() {
            menu.classList.add('d-none');
            menu.innerHTML = '';
        }

        function renderMatched(matched, q) {
            if (matched.length === 0) {
                menu.innerHTML = q
                    ? `<div class="catalog-picker-hint">Нет в справочнике — сохранится как новый пункт</div>`
                    : `<div class="catalog-picker-hint">Начните ввод для поиска</div>`;
                menu.classList.remove('d-none');
                return;
            }

            menu.innerHTML = matched.map(x =>
                `<button type="button" class="catalog-picker-option" data-id="${escapeHtml(x.id)}" title="${escapeHtml(x.text)}">${escapeHtml(x.text)}</button>`
            ).join('');
            menu.classList.remove('d-none');
        }

        function showLocal() {
            const q = norm(textInput.value);
            const list = root._catalogItems || [];
            const matched = !q
                ? list.slice(0, 40)
                : list.filter(x => norm(x.text).includes(q)).slice(0, 40);
            renderMatched(matched, q);
        }

        async function showRemote() {
            const q = textInput.value.trim();
            const token = ++searchToken;
            menu.innerHTML = `<div class="catalog-picker-hint">Поиск…</div>`;
            menu.classList.remove('d-none');
            try {
                const url = searchUrl + (searchUrl.includes('?') ? '&' : '?')
                    + 'q=' + encodeURIComponent(q)
                    + '&take=40';
                const res = await fetch(url, { headers: { 'Accept': 'application/json' } });
                if (!res.ok) throw new Error('search failed');
                const data = await res.json();
                if (token !== searchToken) return;
                // Массив или { items: [...] } — на всякий случай.
                const matched = Array.isArray(data)
                    ? data
                    : (Array.isArray(data?.items) ? data.items : []);
                root._catalogItems = matched;
                renderMatched(matched, norm(q));
            } catch {
                if (token !== searchToken) return;
                menu.innerHTML = `<div class="catalog-picker-hint">Ошибка поиска — можно ввести новый пункт</div>`;
            }
        }

        function showFiltered() {
            if (searchUrl) {
                clearTimeout(searchTimer);
                searchTimer = setTimeout(showRemote, 220);
            } else {
                showLocal();
            }
        }

        function syncFromDom() {
            const id = idInput.value;
            if (id) {
                const hit = (root._catalogItems || []).find(x => x.id === id);
                if (hit && !textInput.value) textInput.value = hit.text;
                manualInput.value = 'false';
            } else {
                manualInput.value = textInput.value.trim() ? 'true' : 'true';
            }
        }

        textInput.addEventListener('focus', showFiltered);
        textInput.addEventListener('input', () => {
            // любой ввод сбрасывает привязку к id — пока не выберут из списка
            idInput.value = '';
            manualInput.value = 'true';
            showFiltered();
            opts?.onChange?.({ id: null, text: textInput.value, manual: true });
        });
        textInput.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') hide();
            if (e.key === 'ArrowDown') {
                const first = menu.querySelector('.catalog-picker-option');
                if (first) { e.preventDefault(); first.focus(); }
            }
        });

        menu.addEventListener('click', (e) => {
            const btn = e.target.closest('.catalog-picker-option');
            if (!btn) return;
            const id = btn.getAttribute('data-id');
            const hit = (root._catalogItems || []).find(x => String(x.id) === String(id));
            if (hit) {
                setSelected(hit);
                hide();
            }
        });

        menu.addEventListener('keydown', (e) => {
            const optsBtns = [...menu.querySelectorAll('.catalog-picker-option')];
            const i = optsBtns.indexOf(document.activeElement);
            if (e.key === 'ArrowDown' && i >= 0 && i < optsBtns.length - 1) {
                e.preventDefault();
                optsBtns[i + 1].focus();
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (i <= 0) textInput.focus();
                else optsBtns[i - 1].focus();
            } else if (e.key === 'Enter' && i >= 0) {
                e.preventDefault();
                optsBtns[i].click();
            } else if (e.key === 'Escape') {
                hide();
                textInput.focus();
            }
        });

        document.addEventListener('click', (e) => {
            if (!root.contains(e.target)) hide();
        });

        syncFromDom();

        root._setCatalogItems = (next) => {
            root._catalogItems = Array.isArray(next) ? next : [];
        };
        root._setSelected = setSelected;
        root._setManual = setManual;
    }

    window.CrownCatalogPicker = { mount: mountCatalogPicker, norm, escapeHtml };
})();
