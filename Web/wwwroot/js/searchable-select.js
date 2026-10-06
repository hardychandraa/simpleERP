/*
 * Searchable dropdown — turns any <select data-searchable> into a type-to-filter box.
 * Used on lists that grow with the business (products, customers, suppliers, sales people,
 * expense categories, open invoices); fixed short choices stay native (HC, 2026-10-07).
 *
 * The original <select> stays in the page as the source of truth: it keeps its name,
 * its value and its change handler, and is only hidden visually. So every page's own
 * script (reading .value / .selectedIndex, inline onchange, resetting .value = '')
 * and the posted form keep working exactly as before. Picking an option sets the
 * select's value and fires a real 'change' event.
 *
 * Typing filters on every word you type, in any order, across the option text — so
 * "kulkas 2pt" or the SKU both find a product. Keys: ↑/↓ move, Enter picks, Esc closes.
 *
 * Self-hosted on purpose: the app's Content-Security-Policy only allows scripts from
 * its own origin, so a CDN widget (Select2, Tom Select…) would be blocked.
 */
(function () {
    'use strict';

    var T = window.SEARCHABLE_T || { noMatch: 'No matches', search: 'Type to search…' };

    function norm(s) { return (s || '').toLowerCase().replace(/\s+/g, ' ').trim(); }

    function enhance(sel) {
        if (sel.dataset.ssReady) return;
        sel.dataset.ssReady = '1';

        var wrap = document.createElement('div');
        wrap.className = 'ss-wrap';
        sel.parentNode.insertBefore(wrap, sel);
        wrap.appendChild(sel);
        sel.classList.add('ss-native');
        sel.tabIndex = -1;

        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'ss-input';
        input.autocomplete = 'off';
        input.spellcheck = false;
        input.placeholder = T.search;
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-expanded', 'false');
        if (sel.disabled) input.disabled = true;
        wrap.appendChild(input);

        var list = document.createElement('div');
        list.className = 'ss-list';
        list.setAttribute('role', 'listbox');
        wrap.appendChild(list);

        var shown = [];      // option indexes currently listed
        var active = -1;     // position in `shown` highlighted by the keyboard

        function currentText() {
            var o = sel.options[sel.selectedIndex];
            return o && o.value !== '' ? o.text.trim() : '';
        }
        function sync() { input.value = currentText(); }

        function render(filter) {
            var words = norm(filter).split(' ').filter(Boolean);
            list.innerHTML = '';
            shown = [];
            for (var i = 0; i < sel.options.length; i++) {
                var o = sel.options[i];
                if (o.disabled) continue;
                var text = norm(o.text);
                if (words.length && !words.every(function (w) { return text.indexOf(w) !== -1; })) continue;
                var item = document.createElement('div');
                item.className = 'ss-item' + (o.value === '' ? ' ss-blank' : '') + (i === sel.selectedIndex ? ' ss-selected' : '');
                item.textContent = o.text.trim();
                item.dataset.index = i;
                item.setAttribute('role', 'option');
                list.appendChild(item);
                shown.push(i);
            }
            if (!shown.length) {
                var none = document.createElement('div');
                none.className = 'ss-none';
                none.textContent = T.noMatch;
                list.appendChild(none);
            }
            active = shown.length ? 0 : -1;
            // Open on the current choice rather than the top of a long list.
            if (!words.length) {
                var at = shown.indexOf(sel.selectedIndex);
                if (at > 0) active = at;
            }
            highlight();
        }

        function highlight() {
            var items = list.querySelectorAll('.ss-item');
            for (var k = 0; k < items.length; k++) items[k].classList.toggle('ss-active', k === active);
            if (items[active]) items[active].scrollIntoView({ block: 'nearest' });
        }

        // Inside a scrolling table the list would be clipped by the wrapper, so there it
        // floats (position:fixed) under the box and closes if the page scrolls.
        var inScroller = !!wrap.closest('.table-scroll');
        function place() {
            if (!inScroller) return;
            var r = input.getBoundingClientRect();
            var vw = document.documentElement.clientWidth;
            var w  = Math.min(Math.max(r.width, 260), vw - 16);   // never wider than the screen
            list.style.position = 'fixed';
            list.style.left  = Math.max(8, Math.min(r.left, vw - w - 8)) + 'px';
            list.style.top   = (r.bottom + 2) + 'px';
            list.style.width = w + 'px';
            list.style.right = 'auto';
        }
        function onScroll(e) { if (!list.contains(e.target)) close(); }

        function open() {
            if (wrap.classList.contains('ss-open')) return;
            place();
            if (inScroller) window.addEventListener('scroll', onScroll, true);
            wrap.classList.add('ss-open');
            input.setAttribute('aria-expanded', 'true');
            render('');
            input.select();
        }
        function close() {
            if (inScroller) window.removeEventListener('scroll', onScroll, true);
            wrap.classList.remove('ss-open');
            input.setAttribute('aria-expanded', 'false');
            sync();
        }
        function pick(index) {
            var changed = sel.selectedIndex !== index;
            setIndex.call(sel, index);
            close();
            if (changed) sel.dispatchEvent(new Event('change', { bubbles: true }));
        }

        input.addEventListener('focus', open);
        input.addEventListener('click', open);
        input.addEventListener('input', function () {
            if (!wrap.classList.contains('ss-open')) {
                place();
                if (inScroller) window.addEventListener('scroll', onScroll, true);
                wrap.classList.add('ss-open'); input.setAttribute('aria-expanded', 'true');
            }
            render(input.value);
        });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                if (!wrap.classList.contains('ss-open')) { open(); return; }
                if (!shown.length) return;
                active = (active + (e.key === 'ArrowDown' ? 1 : -1) + shown.length) % shown.length;
                highlight();
            } else if (e.key === 'Enter') {
                // Never let Enter submit the whole form from inside the search box.
                e.preventDefault();
                if (wrap.classList.contains('ss-open') && active >= 0) pick(shown[active]);
            } else if (e.key === 'Escape') {
                close();
            } else if (e.key === 'Tab') {
                // Tabbing away with exactly one match left takes it.
                if (wrap.classList.contains('ss-open') && input.value && shown.length === 1) pick(shown[0]);
                else close();
            }
        });
        // mousedown, not click: it fires before the input's blur closes the list.
        list.addEventListener('mousedown', function (e) {
            var item = e.target.closest('.ss-item');
            e.preventDefault();
            if (item) pick(+item.dataset.index);
        });
        input.addEventListener('blur', function () { setTimeout(close, 0); });

        // Keep the box in step when page code sets the select directly — e.g. the
        // Sales/Purchases item builders reset it with `sel.value = ''` after Add.
        var proto = HTMLSelectElement.prototype;
        var valueDesc = Object.getOwnPropertyDescriptor(proto, 'value');
        var indexDesc = Object.getOwnPropertyDescriptor(proto, 'selectedIndex');
        var setIndex  = indexDesc.set;
        Object.defineProperty(sel, 'value', {
            configurable: true,
            get: function () { return valueDesc.get.call(this); },
            set: function (v) { valueDesc.set.call(this, v); sync(); }
        });
        Object.defineProperty(sel, 'selectedIndex', {
            configurable: true,
            get: function () { return indexDesc.get.call(this); },
            set: function (v) { setIndex.call(this, v); sync(); }
        });
        sel.addEventListener('change', sync);
        if (sel.form) sel.form.addEventListener('reset', function () { setTimeout(sync, 0); });

        // A `required` select is hidden, so the browser can't point at it; point the
        // user at the search box instead.
        sel.addEventListener('invalid', function () { input.focus(); });

        sync();
    }

    function enhanceAll(root) {
        (root || document).querySelectorAll('select[data-searchable]').forEach(enhance);
    }

    window.SearchableSelect = { enhance: enhance, enhanceAll: enhanceAll };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { enhanceAll(); });
    else enhanceAll();
})();
