(() => {
    const DB_NAME = 'clayzor-drafts-v1';
    const RAM_DB_NAME = 'clayzor-drafts-v2';
    const FORMAT_VERSION = 1;
    const RAM_FORMAT_VERSION = 2;
    const ramOwners = new Map();
    const ramCloseHandles = new Map();
    const closedRamOwners = new Set();
    const closedRamHandles = new Set();
    let ramDocumentId;
    let ramIdentity;
    let ramIdentityChanged = false;
    let ramKey;
    const registrations = new Map();
    const detachedDrafts = new Set();
    let pendingRegistrations = 0;
    const dbPromises = new Map();

    function openDb(name = DB_NAME) {
        if (!dbPromises.has(name)) {
            const pending = new Promise((resolve, reject) => {
                const request = indexedDB.open(name, 1);
                request.onupgradeneeded = () => {
                    const db = request.result;
                    if (!db.objectStoreNames.contains('drafts')) db.createObjectStore('drafts', { keyPath: 'id' });
                    if (name === DB_NAME && !db.objectStoreNames.contains('keys'))
                        db.createObjectStore('keys', { keyPath: 'id' });
                };
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
            dbPromises.set(name, pending);
            pending.catch(() => dbPromises.delete(name));
        }
        return dbPromises.get(name);
    }

    async function request(store, mode, action, database) {
        return storeRequest(await openDb(database), store, mode, action);
    }

    function storeRequest(db, store, mode, action) {
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, mode);
            const result = action(tx.objectStore(store));
            tx.oncomplete = () => resolve(result?.result);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error || new Error('Draft transaction aborted.'));
        });
    }

    function requestDraft(reg, mode, action) {
        return request('drafts', mode, action, reg.ramOwner ? RAM_DB_NAME : DB_NAME);
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

    async function userKey(userKeyValue) {
        const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(userKeyValue));
        const id = bytesToBase64(new Uint8Array(digest));
        const existing = await request('keys', 'readonly', store => store.get(id));
        if (existing?.key) return existing.key;

        const candidate = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
        const db = await openDb();
        return new Promise((resolve, reject) => {
            let authoritative = candidate;
            const tx = db.transaction('keys', 'readwrite');
            const store = tx.objectStore('keys');
            const getRequest = store.get(id);
            getRequest.onsuccess = () => {
                if (getRequest.result?.key) {
                    authoritative = getRequest.result.key;
                    return;
                }
                store.put({ id, key: candidate });
            };
            tx.oncomplete = () => resolve(authoritative);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error || new Error('Draft key transaction aborted.'));
        });
    }

    function invalidateRamIdentity() {
        ramIdentityChanged = true;
        ramKey = undefined;
        for (const owner of ramOwners.values()) {
            const reg = owner.registration;
            if (!reg) continue;
            if (protectsDocument(reg)) detachedDrafts.add(draftKey(reg));
            reg.discarded = true;
            reg.epoch++;
            reg.applyVersion++;
            detachListeners(reg);
            abortEditorWrite(reg);
        }
    }

    async function fetchRamKey(employee) {
        const base = new URL(document.baseURI);
        const url = new URL('api/drafts/key', base);
        const loopbackHttp = url.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]', '::1'].includes(url.hostname);
        if ((url.protocol !== 'https:' && !loopbackHttp) || url.origin !== window.location.origin)
            throw new Error('Для защищённого черновика требуется HTTPS или loopback.');

        const response = await fetch(url, {
            method: 'POST', mode: 'same-origin', credentials: 'same-origin',
            cache: 'no-store', redirect: 'error',
            headers: { 'X-Clay-Draft-Key': '1' },
        });
        if (response.status === 401) invalidateRamIdentity();
        if (response.status !== 200) throw new Error('Ключ черновика недоступен.');
        const material = new Uint8Array(await response.arrayBuffer());
        try {
            if (response.headers.get('X-Clay-Draft-Employee') !== employee) {
                invalidateRamIdentity();
                throw new Error('Сотрудник для черновика изменился.');
            }
            if (material.byteLength !== 32 || response.headers.get('Content-Type') !== 'application/octet-stream')
                throw new Error('Получен неверный формат ключа черновика.');
            const key = await crypto.subtle.importKey('raw', material, 'AES-GCM', false, ['encrypt', 'decrypt']);
            if (ramIdentityChanged) throw new Error('Сотрудник для черновика изменился.');
            return key;
        } finally {
            material.fill(0);
        }
    }

    function getRamKey(employee) {
        if (ramIdentityChanged || ramIdentity !== employee)
            throw new Error('Сотрудник для черновика изменился.');
        if (!ramKey) {
            const pending = fetchRamKey(employee);
            ramKey = pending;
            pending.catch(() => { if (ramKey === pending) ramKey = undefined; });
        }
        return ramKey;
    }

    function encryptionKey(reg) {
        return reg.ramOwner ? getRamKey(reg.userKey) : userKey(reg.userKey);
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

    function draftPrefix(reg) {
        return `${reg.userKey}|${reg.formKey}|${reg.entityKey}|`;
    }

    function draftKey(reg) {
        return reg.ramOwner?.storageId ?? draftPrefix(reg) + reg.draftId;
    }

    function snapshot(reg) {
        return {
            format: reg.ramOwner ? RAM_FORMAT_VERSION : FORMAT_VERSION,
            ...(reg.ramOwner ? { documentId: ramDocumentId, instanceId: reg.ramOwner.instance } : {}),
            schema: reg.schema,
            userKey: reg.userKey,
            formKey: reg.formKey,
            entityKey: reg.entityKey,
            draftId: reg.draftId,
            revision: reg.revision,
            epoch: reg.epoch,
            typedState: reg.typedState,
            typedRevision: reg.typedRevision,
            typedScopes: reg.typedScopes,
            raw: reg.raw,
            cancelledScopes: reg.cancelledScopes,
        };
    }

    async function put(reg) {
        if (reg.discarded || reg.released) return;
        const key = await encryptionKey(reg);
        while (!reg.discarded && !reg.released) {
            const state = snapshot(reg);
            const payload = await encrypt(key, state);
            if (reg.discarded || reg.released || state.epoch !== reg.epoch) return;
            if (state.revision !== reg.revision) continue;
            await requestDraft(reg, 'readwrite', store => store.put({ id: draftKey(reg), ...payload }));
            reg.persistedRevision = state.revision;
            if (state.revision === reg.revision && state.epoch === reg.epoch) {
                reg.lastError = null;
                return;
            }
        }
    }

    function abortEditorWrite(reg) {
        // IndexedDB can finish the transaction before delivering its completion event.
        try { reg.editorWrite?.abort(); } catch (error) {
            if (error.name !== 'InvalidStateError') throw error;
        }
    }

    function queue(reg) {
        abortEditorWrite(reg);
        reg.pendingWrites++;
        const pending = reg.pending.catch(() => { }).then(() => put(reg));
        reg.pending = pending;
        pending.then(
            () => { reg.pendingWrites--; },
            error => {
                reg.pendingWrites--;
                reg.lastError = error instanceof Error ? error : new Error('Черновик не удалось сохранить.');
            });
        return pending;
    }

    async function waitForIdle(reg) {
        while (reg.pendingWrites > 0)
            await reg.pending.catch(() => { });
        if (reg.lastError) throw reg.lastError;
        if (!reg.discarded && !reg.released && reg.persistedRevision !== reg.revision)
            throw new Error('Последний ввод ещё не зафиксирован в защищённом хранилище.');
    }

    async function remove(reg, includeBackup) {
        const id = draftKey(reg);
        await requestDraft(reg, 'readwrite', store => {
            if (includeBackup) store.delete(`${id}:schema1-backup`);
            return store.delete(id);
        });
        reg.persistedRevision = reg.revision;
        reg.lastError = null;
    }

    function fieldValue(element) {
        if (element.type === 'checkbox') return { kind: 'checked', value: element.checked };
        if (element.type === 'radio') return { kind: 'radio', value: element.checked, data: element.value };
        return { kind: 'value', value: element.value };
    }

    function fieldScope(element, reg) {
        const explicit = element.dataset.draftScope || element.closest('[data-draft-scope]')?.dataset.draftScope;
        if (explicit) return explicit;
        if (reg?.activeScope && element.closest('.mud-dialog')) return reg.activeScope;
        return 'main';
    }

    function registrationRoots(reg) {
        if (reg.rootSelector)
            return Array.from(document.querySelectorAll(reg.rootSelector));
        const roots = reg.root ? [reg.root] : [];
        if (reg.activeScope && reg.activeScope !== 'main') {
            const claimants = [...registrations.values()]
                .filter(item => !item.released && !item.discarded && item.activeScope === reg.activeScope);
            if (claimants.length === 1 && claimants[0] === reg) {
                for (const portal of document.querySelectorAll('.mud-dialog,.mud-popover')) {
                    if (portal.dataset.draftScope === reg.activeScope ||
                        Array.from(portal.querySelectorAll('[data-draft-scope]'))
                            .some(item => item.dataset.draftScope === reg.activeScope))
                        roots.push(portal);
                }
            }
        }
        return roots;
    }

    function ownedBy(reg, element, roots = registrationRoots(reg)) {
        if (!roots.some(root => root === element || root.contains(element))) return false;
        for (const other of registrations.values()) {
            if (other !== reg && !other.released && !other.discarded &&
                registrationRoots(other).some(root => root === element || root.contains(element)))
                return false;
        }
        return true;
    }

    function ownedFields(reg) {
        const roots = registrationRoots(reg);
        return Array.from(document.querySelectorAll('input,textarea,select'))
            .filter(element => ownedBy(reg, element, roots));
    }

    function fieldKey(element, reg) {
        if (element.dataset.draftKey) {
            const matches = ownedFields(reg)
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

    function findField(key, reg) {
        let element;
        if (key.startsWith('data:')) {
            const separator = key.lastIndexOf(':');
            const name = key.slice(5, separator);
            const index = Number(key.slice(separator + 1));
            element = ownedFields(reg).filter(item => item.dataset.draftKey === name)[index] || null;
        } else {
            const parts = key.slice(5).split('/');
            let current = document.body;
            for (const part of parts) {
                const [tag, rawIndex] = part.split(':');
                const child = Array.from(current.children).filter(item => item.tagName === tag)[Number(rawIndex)];
                if (!child) return null;
                current = child;
            }
            element = current.matches('input,textarea,select') ? current : null;
        }
        return element && ownedBy(reg, element) ? element : null;
    }

    function sameFieldValue(left, right) {
        return left?.kind === right.kind && left?.value === right.value && left?.data === right.data;
    }

    function isHistoricalSecretKey(formKey, key) {
        return formKey === 'equipment-form' && /^data:(?:account\.password|sim\.(?:pin2?|puk)):\d+$/.test(key);
    }

    function withoutHistoricalSecrets(formKey, raw) {
        if (!raw || !Object.keys(raw).some(key => isHistoricalSecretKey(formKey, key))) return raw;
        return Object.fromEntries(Object.entries(raw).filter(([key]) => !isHistoricalSecretKey(formKey, key)));
    }

    function excludesDraftValue(element) {
        return element?.type === 'password' || element?.dataset?.draftExclude !== undefined;
    }

    function dropExcludedRaw(reg, key) {
        if (!Object.hasOwn(reg.raw, key)) return;
        delete reg.raw[key];
        reg.restorePending.delete(key);
        reg.revision++;
        queue(reg).catch(() => { });
    }

    function captureField(reg, element, confirmedInput = false) {
        if (reg.discarded || reg.released ||
            !element?.matches?.('input,textarea,select') || element.disabled || element.type === 'file' ||
            !ownedBy(reg, element)) return;
        const scope = fieldScope(element, reg);
        if (reg.cancelledScopes[scope]) return;
        const key = fieldKey(element, reg);
        if (excludesDraftValue(element) || isHistoricalSecretKey(reg.formKey, key)) {
            if (reg.raw[key]?.scope === scope) dropExcludedRaw(reg, key);
            return;
        }
        const value = fieldValue(element);
        const previous = reg.raw[key];
        if (confirmedInput && previous?.scope === scope) reg.restorePending.delete(key);
        if (previous?.scope === scope && sameFieldValue(previous, value)) return;
        reg.raw[key] = { ...value, scope, revision: ++reg.revision };
        queue(reg).catch(() => { });
    }

    function captureAll(reg) {
        for (const element of ownedFields(reg)) {
            const scope = fieldScope(element, reg);
            if (reg.restoreFailures.has(scope) || reg.restorePending.has(fieldKey(element, reg))) continue;
            captureField(reg, element);
        }
    }

    function isCurrentApply(reg, id, version) {
        return registrations.get(id) === reg && !reg.discarded && !reg.released && reg.applyVersion === version;
    }

    function scopeResult(result, scope) {
        return result.scopes[scope] ||= { applied: [], pending: [] };
    }

    function appliedRaw(reg, result, key, scope) {
        result.applied.push(key);
        scopeResult(result, scope).applied.push(key);
        reg.restorePending.delete(key);
    }

    function pendingRaw(reg, result, key, scope) {
        result.pending.push(key);
        scopeResult(result, scope).pending.push(key);
        reg.restorePending.add(key);
    }

    function notifyRestoredField(element) {
        if (!element.closest?.('[data-clay-field-watch-raw]')) return;
        element.setAttribute('data-clay-field-restored', '');
        element.dispatchEvent(new Event('clay:field-restored', { bubbles: true }));
    }

    async function applyRaw(id, scope, notifyInput = true, pendingOnly = false) {
        const reg = get(id);
        if (scope !== undefined && scope !== null) validateScope(scope);
        if (scope) reg.restoreFailures.delete(scope);
        else reg.restoreFailures.clear();
        const version = ++reg.applyVersion;
        const result = { applied: [], pending: [], scopes: {} };
        for (const key of Object.keys(reg.raw)) {
            while (isCurrentApply(reg, id, version)) {
                const state = reg.raw[key];
                const stateScope = state?.scope || 'main';
                if (!state || (scope && stateScope !== scope) || reg.cancelledScopes[stateScope] ||
                    (pendingOnly && !reg.restorePending.has(key))) break;
                try {
                    if (isHistoricalSecretKey(reg.formKey, key)) {
                        dropExcludedRaw(reg, key);
                        break;
                    }
                    let element = findField(key, reg);
                    if (!element && (key.startsWith('data:computer.hd:') || key.startsWith('data:computer.ssd:'))) {
                        const selector = key.startsWith('data:computer.hd:') ? '.equipment-add-hd' : '.equipment-add-ssd';
                        registrationRoots(reg).map(root => root.querySelector(selector)).find(Boolean)?.click();
                        for (let attempt = 0; attempt < 40 && !element; attempt++) {
                            await new Promise(resolve => setTimeout(resolve, 50));
                            if (!isCurrentApply(reg, id, version)) break;
                            element = findField(key, reg);
                        }
                    }
                    if (!isCurrentApply(reg, id, version)) break;
                    if (excludesDraftValue(element) && fieldScope(element, reg) === stateScope) {
                        dropExcludedRaw(reg, key);
                        break;
                    }
                    if (!element || element.disabled || fieldScope(element, reg) !== stateScope) {
                        pendingRaw(reg, result, key, stateScope);
                        break;
                    }
                    if (notifyInput && element.getAttribute('role') === 'combobox' && element.closest('.clay-select')) {
                        const accepted = await window.claySelect?.restoreDraftText(element.id, state.value || '');
                        if (!isCurrentApply(reg, id, version)) break;
                        if (reg.raw[key] !== state) continue;
                        if (pendingOnly && !reg.restorePending.has(key)) break;
                        if (!accepted) {
                            pendingRaw(reg, result, key, stateScope);
                            break;
                        }
                        appliedRaw(reg, result, key, stateScope);
                        notifyRestoredField(element);
                        break;
                    }
                    if (state.kind === 'checked') element.checked = state.value;
                    else if (state.kind === 'radio') element.checked = state.value && element.value === state.data;
                    else element.value = state.value;
                    if (notifyInput) {
                        element.dispatchEvent(new Event('input', { bubbles: true }));
                        if (state.kind !== 'value') element.dispatchEvent(new Event('change', { bubbles: true }));
                    }
                    appliedRaw(reg, result, key, stateScope);
                    notifyRestoredField(element);
                    break;
                } catch (error) {
                    reg.restorePending.add(key);
                    reg.restoreFailures.add(stateScope);
                    throw error;
                }
            }
        }
        return result;
    }

    async function claimLock(name) {
        if (!navigator.locks) {
            if (!window.BroadcastChannel) return () => { };
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
        let lockRequest;
        const started = new Promise(resolve => {
            lockRequest = navigator.locks.request(name, { ifAvailable: true }, async lock => {
                if (!lock) { resolve(false); return; }
                resolve(true);
                await new Promise(done => { release = done; });
            }).catch(() => resolve(false));
        });
        return await started ? async () => { release?.(); await lockRequest; } : null;
    }

    function normalizeRaw(raw, revision) {
        const result = {};
        for (const [key, value] of Object.entries(raw || {})) {
            result[key] = {
                ...value,
                scope: value.scope || 'main',
                revision: value.revision || revision,
            };
        }
        return result;
    }

    function normalizeTypedScopes(scopes) {
        const result = {};
        for (const [scope, value] of Object.entries(scopes || {})) {
            result[scope] = typeof value === 'string'
                ? { data: value, revision: 0 }
                : { data: value.data, revision: value.revision || 0 };
        }
        return result;
    }

    function restoreDraftState(reg, restored) {
        const raw = withoutHistoricalSecrets(reg.formKey, restored.raw);
        Object.assign(reg, {
            revision: restored.revision || 0,
            epoch: restored.epoch || 0,
            typedState: restored.typedState ?? null,
            typedRevision: restored.typedRevision ?? restored.revision ?? 0,
            typedScopes: normalizeTypedScopes(restored.typedScopes),
            raw: normalizeRaw(raw, restored.revision || 0),
            cancelledScopes: restored.cancelledScopes || {},
            persistedRevision: restored.revision || 0,
        });
        reg.restorePending = new Set(Object.entries(reg.raw)
            .filter(([, value]) => !reg.cancelledScopes[value.scope || 'main'])
            .map(([key]) => key));
        reg.restoreFailures.clear();
        reg.applyVersion++;
        return raw !== restored.raw;
    }

    function describeRegistration(registrationId, reg, hasDraft) {
        return {
            registrationId, hasDraft, revision: reg.revision, typedState: reg.typedState,
            cancelledScopes: Object.keys(reg.cancelledScopes),
            typedScopes: Object.fromEntries(Object.entries(reg.typedScopes).map(([scope, value]) => [scope, value.data])),
        };
    }

    const draftIdPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

    function ownsSnapshot(reg, state, draftId) {
        return state.format === FORMAT_VERSION && state.userKey === reg.userKey &&
            state.formKey === reg.formKey && state.entityKey === reg.entityKey && state.draftId === draftId;
    }

    async function listRecoveryCandidates(reg) {
        if (reg.ramOwner) return [];
        const prefix = draftPrefix(reg);
        const records = await request('drafts', 'readonly', store =>
            store.getAll(IDBKeyRange.bound(prefix, prefix + '\uffff')));
        const key = await encryptionKey(reg);
        const candidates = [];
        for (const record of records) {
            const draftId = record.id.slice(prefix.length);
            if (draftId === reg.draftId || !draftIdPattern.test(draftId)) continue;
            const state = await decrypt(key, record);
            if (!ownsSnapshot(reg, state, draftId)) continue;
            if (state.typedState == null && !Object.keys(state.typedScopes || {}).length &&
                !Object.keys(state.raw || {}).length) continue;
            candidates.push({
                draftId, revision: state.revision || 0,
                canRecover: state.schema === reg.schema,
            });
        }
        return candidates.sort((a, b) => b.revision - a.revision);
    }

    async function recoverListedDraft(id, draftId) {
        const reg = get(id);
        if (reg.ramOwner) return null;
        if (!draftIdPattern.test(draftId)) throw new Error('Неверный идентификатор черновика.');
        if (reg.recoveredDraftId === draftId) return describeRegistration(id, reg, hasDraftState(reg));
        await waitForIdle(reg);
        if (draftId === reg.draftId || hasDraftState(reg)) return null;
        const revision = reg.revision;
        const release = await claimLock(`clay-draft:${reg.formKey}:${reg.entityKey}:${draftId}`);
        if (!release) return null;
        let adopted = false;
        try {
            const stored = await request('drafts', 'readonly', store => store.get(draftPrefix(reg) + draftId));
            if (!stored) return null;
            const state = await decrypt(await encryptionKey(reg), stored);
            if (!ownsSnapshot(reg, state, draftId) || state.schema !== reg.schema ||
                reg.discarded || reg.released || reg.revision !== revision || hasDraftState(reg)) return null;

            const releasePrevious = reg.releaseLock;
            sessionStorage.setItem(`clay-draft:${reg.userKey}:${reg.formKey}:${reg.entityKey}`, draftId);
            const scrubbed = restoreDraftState(reg, state);
            reg.draftId = reg.recoveredDraftId = draftId;
            reg.releaseLock = release;
            detachedDrafts.delete(draftKey(reg));
            adopted = true;
            await releasePrevious?.();
            if (scrubbed) {
                reg.revision++;
                await queue(reg);
            }
            return describeRegistration(id, reg, hasDraftState(reg));
        } finally {
            if (!adopted) await release();
        }
    }

    async function register(formKey, entityKey, userKeyValue, schema, rootSelector, ramOwner) {
        if (!formKey || !entityKey || !userKeyValue) throw new Error('Draft scope is required.');
        if (rootSelector !== undefined && rootSelector !== null &&
            (typeof rootSelector !== 'string' || !rootSelector.trim()))
            throw new Error('Draft DOM root selector is invalid.');
        const roots = rootSelector
            ? Array.from(document.querySelectorAll(rootSelector))
            : Array.from(document.querySelectorAll('.equipment-form'));
        if (rootSelector && roots.length === 0)
            throw new Error('Draft DOM root is not available.');
        if (!rootSelector && roots.length !== 1)
            throw new Error('Draft DOM root is ambiguous; pass an explicit root selector.');
        const sessionKey = ramOwner?.sessionKey ?? `clay-draft:${userKeyValue}:${formKey}:${entityKey}`;
        let releaseLock;
        let reg;
        pendingRegistrations++;
        try {
            let draftId = ramOwner?.instance ?? (sessionStorage.getItem(sessionKey) || crypto.randomUUID());
            if (ramOwner) await getRamKey(userKeyValue);
            releaseLock = await claimLock(ramOwner?.storageId ?? `clay-draft:${formKey}:${entityKey}:${draftId}`);
            if (!releaseLock && !ramOwner) {
                draftId = crypto.randomUUID();
                releaseLock = await claimLock(`clay-draft:${formKey}:${entityKey}:${draftId}`);
            }
            if (!releaseLock) throw new Error('Не удалось выделить независимый черновик для этой вкладки.');

            if (!ramOwner) sessionStorage.setItem(sessionKey, draftId);
            reg = {
                formKey, entityKey, userKey: userKeyValue, schema, draftId, ramOwner,
                rootSelector: rootSelector || null, root: rootSelector ? null : roots[0],
                revision: 0, epoch: 0, typedState: null, typedRevision: 0, typedScopes: {},
                raw: {}, cancelledScopes: {}, pending: Promise.resolve(), pendingWrites: 0,
                persistedRevision: 0, lastError: null, discarded: false, released: false, releaseLock,
                applyVersion: 0, restorePending: new Set(), restoreFailures: new Set(),
            };
            const id = draftKey(reg);
            let stored = await requestDraft(reg, 'readonly', store => store.get(id));
            let migrated = false;
            let scrubbed = false;
            let restored;
            let discardedUnreadableRam = false;
            if (stored) {
                const key = await encryptionKey(reg);
                try {
                    restored = await decrypt(key, stored);
                } catch (error) {
                    if (!ramOwner || !['OperationError', 'SyntaxError', 'InvalidCharacterError'].includes(error?.name))
                        throw error;
                    await requestDraft(reg, 'readwrite', store => store.delete(id));
                    stored = null;
                    discardedUnreadableRam = true;
                }
            }
            if (stored) {
                if (restored.format !== (ramOwner ? RAM_FORMAT_VERSION : FORMAT_VERSION))
                    throw new Error('Сохранённый черновик имеет неподдерживаемую версию и не был изменён.');
                if (restored.userKey !== userKeyValue || restored.formKey !== formKey || restored.entityKey !== entityKey ||
                    ramOwner && (restored.documentId !== ramDocumentId || restored.instanceId !== ramOwner.instance ||
                        restored.draftId !== draftId))
                    throw new Error('Черновик принадлежит другому контексту и не был открыт.');
                if (restored.schema !== schema && !(restored.schema === 1 && schema === 2))
                    throw new Error('Сохранённый черновик имеет неподдерживаемую версию и не был изменён.');
                if (restored.schema !== schema) {
                    const raw = withoutHistoricalSecrets(formKey, restored.raw);
                    const backup = raw === restored.raw ? stored
                        : await encrypt(await encryptionKey(reg), { ...restored, raw });
                    await requestDraft(reg, 'readwrite', store => store.put({ ...backup, id: `${id}:schema1-backup` }));
                    migrated = true;
                }
                scrubbed = restoreDraftState(reg, restored);
            }
            const detached = ramOwner?.registration;
            if (detached && (discardedUnreadableRam || detached.lastError || detached.revision !== detached.persistedRevision)) {
                scrubbed = restoreDraftState(reg, snapshot(detached)) || scrubbed;
                reg.persistedRevision = detached.persistedRevision;
                reg.lastError = detached.lastError;
                if (discardedUnreadableRam) scrubbed = true;
            }
            if (scrubbed) reg.revision++;
            if (migrated || scrubbed) await queue(reg);
            const hasDraft = hasDraftState(reg);
            if (stored && !hasDraft) {
                // A failed physical deletion is still a clean, empty record on the next open.
                try { await removeEmptyDraft(reg); } catch { }
            }
            if (ramOwner) {
                if (ramIdentityChanged) throw new Error('Сотрудник для черновика изменился.');
                if (ramOwner.closing) throw new Error('Редактор закрывается.');
                sessionStorage.setItem(sessionKey, draftId);
            }
            const registrationId = crypto.randomUUID();
            reg.listener = event => captureField(reg, event.target, event.isTrusted);
            document.addEventListener('input', reg.listener, true);
            document.addEventListener('change', reg.listener, true);
            registrations.set(registrationId, reg);
            if (ramOwner) {
                if (ramOwner.handle) ramCloseHandles.delete(ramOwner.handle);
                ramOwner.registration = reg;
                ramOwner.handle = registrationId;
                ramCloseHandles.set(registrationId, ramOwner);
            }
            detachedDrafts.delete(id);
            return describeRegistration(registrationId, reg, hasDraft);
        } catch (error) {
            if (reg) {
                reg.released = true;
                if (reg.listener) {
                    document.removeEventListener('input', reg.listener, true);
                    document.removeEventListener('change', reg.listener, true);
                }
            }
            await releaseLock?.();
            throw error;
        } finally {
            pendingRegistrations--;
        }
    }

    async function registerRam(formKey, entityKey, userKeyValue, schema, rootSelector, editorInstanceId) {
        if (!formKey || !entityKey || !/^[1-9]\d*$/.test(userKeyValue) || typeof editorInstanceId !== 'string' ||
            !(/^[0-9a-f]{32}$/i.test(editorInstanceId) || draftIdPattern.test(editorInstanceId)))
            throw new Error('Неверный контекст черновика.');

        if (ramIdentity !== undefined && ramIdentity !== userKeyValue) invalidateRamIdentity();
        if (ramIdentityChanged) throw new Error('Сотрудник для черновика изменился.');
        ramIdentity = userKeyValue;
        ramDocumentId ??= crypto.randomUUID();
        const instance = editorInstanceId.replaceAll('-', '').toLowerCase();
        const ownerId = JSON.stringify([userKeyValue, formKey, entityKey, ramDocumentId, instance]);
        if (closedRamOwners.has(ownerId)) throw new Error('Редактор уже закрыт.');

        let owner = ramOwners.get(ownerId);
        if (!owner) {
            owner = { instance, storageId: `ram:${ownerId}`, sessionKey: `clay-draft-ram:${ownerId}` };
            ramOwners.set(ownerId, owner);
        }
        if (owner.closing) throw new Error('Редактор закрывается.');
        if (owner.detaching) {
            try { await owner.detaching; } catch { }
            if (owner.closing || ramIdentityChanged) throw new Error('Редактор недоступен.');
        }
        if (owner.attach) return owner.attach;
        if (owner.registration && !owner.registration.released &&
            (owner.registration.schema !== schema || owner.registration.rootSelector !== (rootSelector || null)))
            throw new Error('Экземпляр редактора уже зарегистрирован с другим контекстом.');
        if (owner.registration && !owner.registration.released)
            return describeRegistration(owner.handle, owner.registration, hasDraftState(owner.registration));

        const pending = register(formKey, entityKey, userKeyValue, schema, rootSelector, owner);
        owner.attach = pending;
        try {
            return await pending;
        } catch (error) {
            if (!owner.registration) {
                ramOwners.delete(ownerId);
                if (!ramOwners.size) ramKey = undefined;
            }
            throw error;
        } finally {
            owner.attach = undefined;
        }
    }

    function detachListeners(reg) {
        document.removeEventListener('input', reg.listener, true);
        document.removeEventListener('change', reg.listener, true);
    }

    async function closeRam(registrationId, expectedRevision) {
        if (expectedRevision !== undefined && (!Number.isSafeInteger(expectedRevision) || expectedRevision < 0))
            throw new Error('Неверная ревизия при закрытии редактора.');
        if (closedRamHandles.has(registrationId)) return true;
        const owner = ramCloseHandles.get(registrationId);
        if (!owner) throw new Error('Редактор не найден.');
        if (owner.cleanup) return owner.cleanup;
        const reg = owner.registration;
        if (expectedRevision !== undefined && reg.revision !== expectedRevision)
            return false;

        owner.closing = true;
        reg.discarded = true;
        reg.epoch++;
        reg.applyVersion++;
        detachListeners(reg);
        detachedDrafts.add(draftKey(reg));

        const cleanup = (async () => {
            abortEditorWrite(reg);
            await reg.pending.catch(() => { });
            if (owner.detaching) await owner.detaching.catch(() => { });
            if (owner.attach) await owner.attach.catch(() => { });
            await remove(reg, true);
            sessionStorage.removeItem(owner.sessionKey);
            await reg.releaseLock?.();
            reg.released = true;
            registrations.delete(registrationId);
            detachedDrafts.delete(draftKey(reg));
            ramCloseHandles.delete(registrationId);
            closedRamHandles.add(registrationId);
            const ownerId = owner.storageId.slice('ram:'.length);
            closedRamOwners.add(ownerId);
            ramOwners.delete(ownerId);
            if (!ramOwners.size) ramKey = undefined;
            return true;
        })();
        owner.cleanup = cleanup;
        try { return await cleanup; }
        finally { owner.cleanup = undefined; }
    }

    function get(id) {
        const reg = registrations.get(id);
        if (!reg || reg.discarded || reg.released || reg.ramOwner?.detaching) throw new Error('Draft registration is not active.');
        return reg;
    }

    function validateScope(scope) {
        if (!scope || typeof scope !== 'string') throw new Error('Draft scope is required.');
    }

    function hasProtectedDraftState(reg) {
        return reg.typedState !== null || Object.keys(reg.typedScopes).length > 0 ||
            Object.keys(reg.raw).length > 0;
    }

    function hasDraftState(reg) {
        return hasProtectedDraftState(reg) || Object.keys(reg.cancelledScopes).length > 0;
    }

    function protectsDocument(reg) {
        return hasProtectedDraftState(reg) || reg.lastError !== null ||
            reg.pendingWrites !== 0 || reg.persistedRevision !== reg.revision;
    }

    function hasProtectedDocumentState() {
        return pendingRegistrations > 0 || detachedDrafts.size > 0 ||
            [...registrations.values()].some(protectsDocument);
    }

    async function health(reg) {
        try { await waitForIdle(reg); } catch { }
        return {
            healthy: !reg.lastError && reg.pendingWrites === 0 && reg.persistedRevision === reg.revision,
            pendingWrites: reg.pendingWrites,
            lastError: reg.lastError?.message || null,
            revision: reg.revision,
            persistedRevision: reg.persistedRevision,
        };
    }

    async function removeEmptyDraft(reg) {
        const revision = reg.revision;
        await requestDraft(reg, 'readwrite', store => {
            if (!reg.discarded && !reg.released && reg.revision === revision && !hasDraftState(reg))
                return store.delete(draftKey(reg));
        });
    }

    async function flushCommittedDraft(reg) {
        await queue(reg);
        if (!hasDraftState(reg)) await removeEmptyDraft(reg);
    }

    window.addEventListener?.('beforeunload', event => {
        if (!hasProtectedDocumentState()) return;
        event.preventDefault();
        event.returnValue = true;
    });

    async function replaceEditorScope(reg, scope, expectedRevision, replacement, nestedChange) {
        validateScope(scope);
        await waitForIdle(reg);
        const isCurrent = () => !reg.discarded && !reg.released &&
            !reg.cancelledScopes[scope] && reg.revision === expectedRevision;

        if (!isCurrent()) return false;

        const state = snapshot(reg);
        state.revision++;
        state.raw = Object.fromEntries(Object.entries(reg.raw)
            .filter(([, field]) => (field.scope || 'main') !== scope));
        state.typedScopes = { ...reg.typedScopes };
        state.cancelledScopes = { ...reg.cancelledScopes };

        if (nestedChange) {
            delete state.typedScopes[scope];
            if (nestedChange.close) state.cancelledScopes[scope] = true;
            state.typedState = nestedChange.parentState;
            state.typedRevision = state.revision;
        } else if (replacement === null) {
            delete state.typedScopes[scope];
            state.cancelledScopes[scope] = true;
        } else {
            state.typedScopes[scope] = { data: replacement, revision: state.revision };
        }
        if (scope === 'main') {
            state.typedState = null;
            state.typedRevision = state.revision;
        }

        return writeEditorScopeState(reg, scope, state, isCurrent);
    }

    async function writeEditorScopeState(reg, scope, state, isCurrent) {
        const key = await encryptionKey(reg);
        const payload = await encrypt(key, state);
        const db = await openDb(reg.ramOwner ? RAM_DB_NAME : DB_NAME);
        if (!isCurrent() || reg.pendingWrites !== 0) return false;

        const tx = db.transaction('drafts', 'readwrite');
        const write = new Promise((resolve, reject) => {
            tx.oncomplete = () => {
                if (!isCurrent()) {
                    // A queued autosave will persist the still-visible input with the newer revision.
                    resolve(false);
                    return;
                }
                reg.typedState = state.typedState;
                reg.typedRevision = state.typedRevision;
                reg.typedScopes = state.typedScopes;
                reg.raw = state.raw;
                reg.cancelledScopes = state.cancelledScopes;
                for (const key of reg.restorePending) {
                    if (!reg.raw[key]) reg.restorePending.delete(key);
                }
                reg.restoreFailures.delete(scope);
                reg.applyVersion++;
                reg.revision = reg.persistedRevision = state.revision;
                resolve(true);
            };
            tx.onabort = () => {
                if (!isCurrent()) resolve(false);
                else reject(tx.error || new Error('Не удалось сохранить изменение черновика.'));
            };
            tx.objectStore('drafts').put({ id: draftKey(reg), ...payload });
        });
        reg.editorWrite = tx;
        reg.pendingWrites++;
        reg.pending = write;
        try { return await write; }
        finally {
            reg.editorWrite = null;
            reg.pendingWrites--;
        }
    }

    async function streamRestore(restore) {
        if (!restore) return null;
        try {
            return {
                registrationId: restore.registrationId,
                payload: DotNet.createJSStreamReference(new Blob([JSON.stringify(restore)], { type: 'application/json' }))
            };
        } catch {
            await window.clayDraftStore.unregister(restore.registrationId);
            throw new Error('Не удалось открыть защищённые данные черновика для чтения.');
        }
    }

    window.clayDraftStore = {
        register(formKey, entityKey, userKeyValue, schema, rootSelector) {
            return register(formKey, entityKey, userKeyValue, schema, rootSelector);
        },
        registerRam,
        closeRam,
        async registerRamStream(...args) { return streamRestore(await registerRam(...args)); },
        async registerStream(formKey, entityKey, userKeyValue, schema, rootSelector) {
            return streamRestore(await register(formKey, entityKey, userKeyValue, schema, rootSelector));
        },
        async recoverListedDraftStream(id, draftId) { return streamRestore(await recoverListedDraft(id, draftId)); },
        listRecoveryCandidates(id) { return listRecoveryCandidates(get(id)); },
        recoverListedDraft,
        async saveTyped(id, typedState) {
            const reg = get(id);
            reg.typedState = typedState;
            reg.typedRevision = ++reg.revision;
            await queue(reg);
            return reg.revision;
        },
        async saveTypedScope(id, scope, typedState) {
            const reg = get(id);
            validateScope(scope);
            if (reg.cancelledScopes[scope]) throw new Error('Отменённая область не может быть восстановлена поздним autosave.');
            reg.typedScopes[scope] = { data: typedState, revision: ++reg.revision };
            await queue(reg);
            return reg.revision;
        },
        applyRaw,
        async applyPendingRaw(id, scope, notifyInput = true) {
            validateScope(scope);
            return applyRaw(id, scope, notifyInput, true);
        },
        async openScope(id, scope) {
            const reg = get(id);
            validateScope(scope);
            reg.activeScope = scope;
            if (!reg.cancelledScopes[scope]) return;
            delete reg.cancelledScopes[scope];
            reg.revision++;
            await queue(reg);
        },
        async discardScope(id, scope) {
            const reg = get(id);
            validateScope(scope);
            reg.activeScope = 'main';
            await waitForIdle(reg);
            reg.cancelledScopes[scope] = true;
            delete reg.typedScopes[scope];
            for (const [key, value] of Object.entries(reg.raw)) {
                if ((value.scope || 'main') === scope) {
                    delete reg.raw[key];
                    reg.restorePending.delete(key);
                }
            }
            reg.restoreFailures.delete(scope);
            reg.revision++;
            await queue(reg);
        },
        async discard(id) {
            const reg = get(id);
            if (reg.ramOwner) throw new Error('Для завершения временного черновика требуется закрыть редактор.');
            reg.discarded = true;
            abortEditorWrite(reg);
            reg.epoch++;
            document.removeEventListener('input', reg.listener, true);
            document.removeEventListener('change', reg.listener, true);
            await reg.pending.catch(() => { });
            await remove(reg, true);
            registrations.delete(id);
            detachedDrafts.delete(draftKey(reg));
            await reg.releaseLock?.();
        },
        async unregister(id) {
            const reg = registrations.get(id);
            if (!reg) throw new Error('Draft registration is not active.');
            const owner = reg.ramOwner;
            if (owner?.detaching) return owner.detaching;
            if (owner?.closing) return owner.cleanup;
            if (owner) reg.applyVersion++;
            detachListeners(reg);
            const detach = (async () => {
                try {
                    await waitForIdle(reg);
                } finally {
                    if (protectsDocument(reg)) detachedDrafts.add(draftKey(reg));
                    reg.released = true;
                    registrations.delete(id);
                    const releaseLock = reg.releaseLock;
                    reg.releaseLock = undefined;
                    await releaseLock?.();
                }
            })();
            if (owner) owner.detaching = detach;
            try { await detach; }
            finally { if (owner) owner.detaching = undefined; }
        },
        async readEditorScope(id, scope) {
            const reg = get(id);
            validateScope(scope);
            await waitForIdle(reg);
            return {
                revision: reg.revision,
                hasRaw: Object.values(reg.raw).some(field => (field.scope || 'main') === scope),
                };
        },
        async readEditorCloseState(id) {
            const reg = get(id);
            await waitForIdle(reg);
            if (get(id) !== reg) throw new Error('Редактор уже закрыт.');
            return {
                revision: reg.revision,
                hasRaw: Object.keys(reg.raw).length !== 0,
                    typedScopes: Object.keys(reg.typedScopes).sort(),
            };
        },
        async replaceEditorScope(id, scope, expectedRevision, replacement) {
            if (!Number.isSafeInteger(expectedRevision) || expectedRevision < 0 ||
                (replacement !== null && typeof replacement !== 'string'))
                throw new Error('Неверный снимок отмены изменений.');
            return replaceEditorScope(get(id), scope, expectedRevision, replacement);
        },
        async replaceNestedEditorScope(id, scope, expectedRevision, parentState, close) {
            if (scope === 'main' || !Number.isSafeInteger(expectedRevision) || expectedRevision < 0 ||
                typeof parentState !== 'string' || typeof close !== 'boolean')
                throw new Error('Неверный снимок вложенного редактора.');
            return replaceEditorScope(get(id), scope, expectedRevision, null, { parentState, close });
        },
        async readRebaseState(id, scope) {
            const reg = get(id);
            validateScope(scope);
            await waitForIdle(reg);
            return {
                revision: reg.revision,
                rawKeys: Object.keys(reg.raw).filter(key => (reg.raw[key]?.scope || 'main') === scope)
            };
        },
        async rebaseScope(id, scope, expectedRevision, snapshotJson, discardedRaw) {
            const reg = get(id);
            validateScope(scope);
            await waitForIdle(reg);
            const isCurrent = () => !reg.discarded && !reg.released &&
                reg.revision === expectedRevision && !reg.cancelledScopes[scope];
            if (!isCurrent()) throw new Error('Черновик изменился после открытия сверки.');
            const hasUnrestoredInput = () => reg.restoreFailures.has(scope) ||
                [...reg.restorePending].some(key => (reg.raw[key]?.scope || 'main') === scope);
            if (hasUnrestoredInput()) throw new Error('Сначала полностью восстановите ввод этой области.');

            if (!Array.isArray(discardedRaw) || discardedRaw.some(key => !reg.raw[key] || (reg.raw[key].scope || 'main') !== scope))
                throw new Error('Выбранный для замены ввод принадлежит другой области.');
            const deleted = new Set(discardedRaw);
            const state = snapshot(reg);
            state.revision++;
            state.raw = Object.fromEntries(Object.entries(reg.raw).filter(([key]) => !deleted.has(key)));
            state.typedScopes = { ...reg.typedScopes, [scope]: { data: snapshotJson, revision: state.revision } };
            try {
                return await writeEditorScopeState(reg, scope, state, () => isCurrent() && !hasUnrestoredInput());
            } catch {
                return false;
            }
        },
        async getHealth(id) { return health(get(id)); },
        async getRestoreReadiness(id) {
            const reg = get(id);
            const pendingScopes = new Set();
            for (const key of reg.restorePending) {
                if (reg.raw[key]) pendingScopes.add(reg.raw[key].scope || 'main');
            }
            const failedScopes = [...reg.restoreFailures].sort();
            return {
                complete: pendingScopes.size === 0 && failedScopes.length === 0,
                pendingScopes: [...pendingScopes].sort(),
                failedScopes,
            };
        },
        async reportRestoreScopeFailure(id, scope) {
            const reg = get(id);
            validateScope(scope);
            reg.restoreFailures.add(scope);
        },
        async getReloadHealth() {
            const active = [...registrations.values()];
            const states = await Promise.all(active.map(health));
            return {
                healthy: active.length > 0 && states.every(state => state.healthy),
                participants: active.length,
                pendingWrites: states.reduce((sum, state) => sum + state.pendingWrites, 0),
            };
        },
        async canReload() {
            return registrations.size > 0 && !hasProtectedDocumentState();
        },
    };
})();
