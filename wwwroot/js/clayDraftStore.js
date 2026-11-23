(() => {
    const DB_NAME = 'clayzor-drafts-v1';
    const FORMAT_VERSION = 1;
    const registrations = new Map();
    let dbPromise;

    function openDb() {
        if (!dbPromise) {
            dbPromise = new Promise((resolve, reject) => {
                const request = indexedDB.open(DB_NAME, 1);
                request.onupgradeneeded = () => {
                    const db = request.result;
                    if (!db.objectStoreNames.contains('drafts')) db.createObjectStore('drafts', { keyPath: 'id' });
                    if (!db.objectStoreNames.contains('keys')) db.createObjectStore('keys', { keyPath: 'id' });
                };
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        }
        return dbPromise;
    }

    async function request(store, mode, action) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, mode);
            const result = action(tx.objectStore(store));
            tx.oncomplete = () => resolve(result?.result);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error || new Error('Draft transaction aborted.'));
        });
    }

    function bytesToBase64(bytes) {
        let binary = '';
        for (let offset = 0; offset < bytes.length; offset += 0x8000)
            binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
        return btoa(binary);
    }

    function base64ToBytes(value) {
        const binary = atob(value);
        return Uint8Array.from(binary, char => char.charCodeAt(0));
    }

    async function userKey(userKey) {
        const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(userKey));
        const id = bytesToBase64(new Uint8Array(digest));
        const existing = await request('keys', 'readonly', store => store.get(id));
        if (existing?.key) return existing.key;
        const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
        await request('keys', 'readwrite', store => store.put({ id, key }));
        return key;
    }

    async function encrypt(key, value) {
        const iv = crypto.getRandomValues(new Uint8Array(12));
        const data = new TextEncoder().encode(JSON.stringify(value));
        const encrypted = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, data);
        return { iv: bytesToBase64(iv), data: bytesToBase64(new Uint8Array(encrypted)) };
    }

    async function decrypt(key, record) {
        const data = await crypto.subtle.decrypt(
            { name: 'AES-GCM', iv: base64ToBytes(record.iv) }, key, base64ToBytes(record.data));
        return JSON.parse(new TextDecoder().decode(data));
    }

    function draftKey(reg) {
        return `${reg.userKey}|${reg.formKey}|${reg.entityKey}|${reg.draftId}`;
    }

    async function put(reg) {
        const key = await userKey(reg.userKey);
        while (!reg.discarded) {
            const snapshot = {
                format: FORMAT_VERSION,
                schema: reg.schema,
                userKey: reg.userKey,
                formKey: reg.formKey,
                entityKey: reg.entityKey,
                draftId: reg.draftId,
                revision: reg.revision,
                epoch: reg.epoch,
                typedState: reg.typedState,
                operation: reg.operation,
                raw: reg.raw,
            };
            const payload = await encrypt(key, snapshot);
            if (reg.discarded || snapshot.epoch !== reg.epoch) return;
            if (snapshot.revision !== reg.revision) continue;
            await request('drafts', 'readwrite', store => store.put({ id: draftKey(reg), ...payload }));
            reg.lastError = null;
            if (snapshot.revision === reg.revision && snapshot.epoch === reg.epoch) return;
        }
    }

    function queue(reg) {
        reg.pending = reg.pending.catch(() => { }).then(() => put(reg));
        reg.pending.catch(() => { reg.lastError = new Error('Черновик не удалось сохранить.'); });
        return reg.pending;
    }

    async function remove(reg) {
        const id = draftKey(reg);
        await request('drafts', 'readwrite', store => {
            store.delete(`${id}:schema1-backup`);
            return store.delete(id);
        });
    }

    function fieldValue(element) {
        if (element.type === 'checkbox') return { kind: 'checked', value: element.checked };
        if (element.type === 'radio') return { kind: 'radio', value: element.checked, data: element.value };
        return { kind: 'value', value: element.value };
    }

    function fieldKey(element) {
        if (element.dataset.draftKey) {
            const matches = Array.from(document.querySelectorAll('[data-draft-key]'))
                .filter(item => item.dataset.draftKey === element.dataset.draftKey);
            return `data:${element.dataset.draftKey}:${Math.max(0, matches.indexOf(element))}`;
        }
        const parts = [];
        let current = element;
        while (current && current !== document.body) {
            const index = Array.from(current.parentElement?.children || [])
                .filter(item => item.tagName === current.tagName).indexOf(current);
            parts.unshift(`${current.tagName}:${Math.max(0, index)}`);
            current = current.parentElement;
        }
        return `path:${parts.join('/')}`;
    }

    function findField(key) {
        if (key.startsWith('data:')) {
            const separator = key.lastIndexOf(':');
            const name = key.slice(5, separator);
            const index = Number(key.slice(separator + 1));
            return Array.from(document.querySelectorAll('[data-draft-key]'))
                .filter(item => item.dataset.draftKey === name)[index] || null;
        }
        const parts = key.slice(5).split('/');
        let current = document.body;
        for (const part of parts) {
            const [tag, rawIndex] = part.split(':');
            const child = Array.from(current.children).filter(item => item.tagName === tag)[Number(rawIndex)];
            if (!child) return null;
            current = child;
        }
        return current.matches('input,textarea,select') ? current : null;
    }

    function captureField(reg, element) {
        if (!element?.matches?.('input,textarea,select') || element.disabled || element.type === 'file') return;
        if (!element.closest('.equipment-form,.mud-dialog,.mud-popover')) return;
        reg.raw[fieldKey(element)] = fieldValue(element);
        reg.revision++;
        queue(reg);
    }

    function captureAll(reg) {
        document.querySelectorAll('.equipment-form input,.equipment-form textarea,.equipment-form select,.mud-dialog input,.mud-dialog textarea,.mud-dialog select')
            .forEach(element => {
                if (!element.disabled && element.type !== 'file') reg.raw[fieldKey(element)] = fieldValue(element);
            });
    }

    async function applyRaw(reg) {
        for (const [key, state] of Object.entries(reg.raw)) {
            let element = findField(key);
            if (!element && (key.startsWith('data:computer.hd:') || key.startsWith('data:computer.ssd:'))) {
                const selector = key.startsWith('data:computer.hd:') ? '.equipment-add-hd' : '.equipment-add-ssd';
                document.querySelector(selector)?.click();
                for (let attempt = 0; attempt < 40 && !element; attempt++) {
                    await new Promise(resolve => setTimeout(resolve, 50));
                    element = findField(key);
                }
            }
            if (!element || element.disabled) continue;
            if (element.getAttribute('role') === 'combobox' && window.claySelect?.restoreDraftText) {
                await window.claySelect.restoreDraftText(element.id, state.value || '');
                continue;
            }
            if (state.kind === 'checked') element.checked = state.value;
            else if (state.kind === 'radio') element.checked = state.value && element.value === state.data;
            else element.value = state.value;
            element.dispatchEvent(new Event('input', { bubbles: true }));
            if (state.kind !== 'value') element.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    async function claimLock(name) {
        if (!navigator.locks) {
            if (!window.BroadcastChannel) return null;
            const channel = new BroadcastChannel(name);
            const owner = crypto.randomUUID();
            let conflict = false;
            channel.onmessage = event => {
                if (event.data?.type === 'claim' && event.data.owner !== owner)
                    channel.postMessage({ type: 'occupied', target: event.data.owner });
                if (event.data?.type === 'occupied' && event.data.target === owner) conflict = true;
            };
            channel.postMessage({ type: 'claim', owner });
            await new Promise(resolve => setTimeout(resolve, 60));
            if (conflict) {
                channel.close();
                return null;
            }
            return () => channel.close();
        }
        let release;
        const started = new Promise(resolve => {
            navigator.locks.request(name, { ifAvailable: true }, async lock => {
                if (!lock) { resolve(false); return; }
                resolve(true);
                await new Promise(done => { release = done; });
            }).catch(() => resolve(false));
        });
        return await started ? () => release?.() : null;
    }

    async function register(formKey, entityKey, userKeyValue, schema) {
        if (!formKey || !entityKey || !userKeyValue) throw new Error('Draft scope is required.');
        const sessionKey = `clay-draft:${userKeyValue}:${formKey}:${entityKey}`;
        let draftId = sessionStorage.getItem(sessionKey) || crypto.randomUUID();
        let releaseLock = await claimLock(`clay-draft:${formKey}:${entityKey}:${draftId}`);
        if (!releaseLock) {
            draftId = crypto.randomUUID();
            releaseLock = await claimLock(`clay-draft:${formKey}:${entityKey}:${draftId}`);
        }
        if (!releaseLock) throw new Error('Не удалось выделить независимый черновик для этой вкладки.');
        sessionStorage.setItem(sessionKey, draftId);
        const reg = {
            formKey, entityKey, userKey: userKeyValue, schema, draftId,
            revision: 0, epoch: 0, typedState: null, operation: null, raw: {},
            pending: Promise.resolve(), lastError: null, discarded: false, releaseLock,
        };
        const id = draftKey(reg);
        const stored = await request('drafts', 'readonly', store => store.get(id));
        let migrated = false;
        if (stored) {
            const restored = await decrypt(await userKey(userKeyValue), stored);
            if (restored.format !== FORMAT_VERSION)
                throw new Error('Сохранённый черновик имеет неподдерживаемую версию и не был изменён.');
            if (restored.schema !== schema) {
                if (restored.schema === 1 && schema === 2) {
                    await request('drafts', 'readwrite', store => store.put({ ...stored, id: `${id}:schema1-backup` }));
                    migrated = true;
                }
                else {
                    throw new Error('Сохранённый черновик имеет неподдерживаемую версию и не был изменён.');
                }
            }
            if (restored.userKey !== userKeyValue || restored.formKey !== formKey || restored.entityKey !== entityKey)
                throw new Error('Черновик принадлежит другому контексту и не был открыт.');
            Object.assign(reg, {
                revision: restored.revision || 0,
                epoch: restored.epoch || 0,
                typedState: restored.typedState,
                operation: restored.operation,
                raw: restored.raw || {},
            });
        }
        const registrationId = crypto.randomUUID();
        reg.listener = event => captureField(reg, event.target);
        document.addEventListener('input', reg.listener, true);
        document.addEventListener('change', reg.listener, true);
        registrations.set(registrationId, reg);
        if (migrated) await queue(reg);
        return {
            registrationId,
            hasDraft: !!stored,
            revision: reg.revision,
            typedState: reg.typedState,
            operation: reg.operation,
        };
    }

    function get(id) {
        const reg = registrations.get(id);
        if (!reg || reg.discarded) throw new Error('Draft registration is not active.');
        return reg;
    }

    window.clayDraftStore = {
        register,
        async saveTyped(id, typedState) {
            const reg = get(id);
            reg.typedState = typedState;
            reg.revision++;
            await queue(reg);
            return reg.revision;
        },
        applyRaw(id) { applyRaw(get(id)); },
        async beginOperation(id, operation) {
            const reg = get(id);
            captureAll(reg);
            reg.operation = { status: 'running', revision: reg.revision + 1, data: operation };
            reg.revision++;
            await queue(reg);
            return reg.revision;
        },
        async rejectOperation(id, revision) {
            const reg = get(id);
            if (reg.operation?.revision === revision) reg.operation = null;
            await queue(reg);
        },
        async unknownOperation(id, revision) {
            const reg = get(id);
            if (reg.operation?.revision === revision) reg.operation.status = 'unknown';
            await queue(reg);
        },
        async commitOperation(id, revision, typedState) {
            const reg = get(id);
            if (typedState !== undefined && typedState !== null) reg.typedState = typedState;
            if (reg.operation?.revision === revision) reg.operation = null;
            if (reg.revision === revision && Object.keys(reg.raw).length === 0) {
                reg.epoch++;
                await remove(reg);
                return;
            }
            if (reg.revision === revision) {
                reg.epoch++;
                await remove(reg);
                return;
            }
            await queue(reg);
        },
        async discard(id) {
            const reg = get(id);
            reg.discarded = true;
            reg.epoch++;
            document.removeEventListener('input', reg.listener, true);
            document.removeEventListener('change', reg.listener, true);
            await reg.pending.catch(() => { });
            await remove(reg);
            registrations.delete(id);
            reg.releaseLock?.();
        },
        async unregister(id) {
            const reg = get(id);
            document.removeEventListener('input', reg.listener, true);
            document.removeEventListener('change', reg.listener, true);
            await reg.pending;
            registrations.delete(id);
            reg.releaseLock?.();
        },
        async canReload() {
            const active = [...registrations.values()];
            await Promise.all(active.map(reg => reg.pending.catch(() => { })));
            return active.every(reg => !reg.lastError);
        },
    };
})();
