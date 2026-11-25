(() => {
    const DB_NAME = 'clayzor-drafts-v1';
    const FORMAT_VERSION = 1;
    const OPERATION_VERSION = 1;
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
            dbPromise.catch(() => { dbPromise = null; });
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

    function snapshot(reg) {
        return {
            format: FORMAT_VERSION,
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
            operation: reg.operation,
            raw: reg.raw,
            cancelledScopes: reg.cancelledScopes,
        };
    }

    async function put(reg) {
        const key = await userKey(reg.userKey);
        while (!reg.discarded && !reg.released) {
            const state = snapshot(reg);
            const payload = await encrypt(key, state);
            if (reg.discarded || reg.released || state.epoch !== reg.epoch) return;
            if (state.revision !== reg.revision) continue;
            await request('drafts', 'readwrite', store => store.put({ id: draftKey(reg), ...payload }));
            reg.persistedRevision = state.revision;
            if (state.revision === reg.revision && state.epoch === reg.epoch) {
                reg.lastError = null;
                return;
            }
        }
    }

    function queue(reg) {
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
        await request('drafts', 'readwrite', store => {
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

    function captureField(reg, element) {
        if (reg.discarded || reg.released ||
            !element?.matches?.('input,textarea,select') || element.disabled || element.type === 'file' ||
            !ownedBy(reg, element)) return;
        const scope = fieldScope(element, reg);
        if (reg.cancelledScopes[scope]) return;
        const key = fieldKey(element, reg);
        const value = fieldValue(element);
        const previous = reg.raw[key];
        if (previous?.scope === scope && sameFieldValue(previous, value)) return;
        reg.raw[key] = { ...value, scope, revision: ++reg.revision };
        queue(reg).catch(() => { });
    }

    function captureAll(reg) {
        ownedFields(reg).forEach(element => captureField(reg, element));
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

    async function applyRaw(id, scope) {
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
                if (!state || (scope && stateScope !== scope) || reg.cancelledScopes[stateScope]) break;
                try {
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
                    if (!element || element.disabled || fieldScope(element, reg) !== stateScope) {
                        pendingRaw(reg, result, key, stateScope);
                        break;
                    }
                    if (element.getAttribute('role') === 'combobox' && window.claySelect?.restoreDraftText) {
                        await window.claySelect.restoreDraftText(element.id, state.value || '');
                        if (!isCurrentApply(reg, id, version)) break;
                        if (reg.raw[key] !== state) continue;
                        appliedRaw(reg, result, key, stateScope);
                        break;
                    }
                    if (state.kind === 'checked') element.checked = state.value;
                    else if (state.kind === 'radio') element.checked = state.value && element.value === state.data;
                    else element.value = state.value;
                    element.dispatchEvent(new Event('input', { bubbles: true }));
                    if (state.kind !== 'value') element.dispatchEvent(new Event('change', { bubbles: true }));
                    appliedRaw(reg, result, key, stateScope);
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

    function normalizeOperation(operation, revision, typedRevision) {
        if (!operation) return null;
        if (operation.version === OPERATION_VERSION && operation.attemptId) return operation;
        let kind = 'legacy';
        try { kind = JSON.parse(operation.data)?.Kind || kind; } catch { }
        return {
            version: OPERATION_VERSION,
            attemptId: `legacy-${revision}`,
            kind,
            status: operation.status === 'unknown' ? 'unknown' : 'running',
            revision: operation.revision || revision,
            data: operation.data || '',
            scopes: [],
            acceptLegacyTyped: false,
            legacyTypedRevision: typedRevision,
        };
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

    async function register(formKey, entityKey, userKeyValue, schema, rootSelector) {
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
        const sessionKey = `clay-draft:${userKeyValue}:${formKey}:${entityKey}`;
        let draftId = sessionStorage.getItem(sessionKey) || crypto.randomUUID();
        let releaseLock = await claimLock(`clay-draft:${formKey}:${entityKey}:${draftId}`);
        if (!releaseLock) {
            draftId = crypto.randomUUID();
            releaseLock = await claimLock(`clay-draft:${formKey}:${entityKey}:${draftId}`);
        }
        if (!releaseLock) throw new Error('Не удалось выделить независимый черновик для этой вкладки.');

        let reg;
        try {
            sessionStorage.setItem(sessionKey, draftId);
            reg = {
                formKey, entityKey, userKey: userKeyValue, schema, draftId,
                rootSelector: rootSelector || null, root: rootSelector ? null : roots[0],
                revision: 0, epoch: 0, typedState: null, typedRevision: 0, typedScopes: {},
                operation: null, raw: {}, cancelledScopes: {}, pending: Promise.resolve(), pendingWrites: 0,
                persistedRevision: 0, lastError: null, discarded: false, released: false, releaseLock,
                applyVersion: 0, restorePending: new Set(), restoreFailures: new Set(),
            };
            const id = draftKey(reg);
            const stored = await request('drafts', 'readonly', store => store.get(id));
            let migrated = false;
            if (stored) {
                const restored = await decrypt(await userKey(userKeyValue), stored);
                if (restored.format !== FORMAT_VERSION)
                    throw new Error('Сохранённый черновик имеет неподдерживаемую версию и не был изменён.');
                if (restored.userKey !== userKeyValue || restored.formKey !== formKey || restored.entityKey !== entityKey)
                    throw new Error('Черновик принадлежит другому контексту и не был открыт.');
                if (restored.schema !== schema && !(restored.schema === 1 && schema === 2))
                    throw new Error('Сохранённый черновик имеет неподдерживаемую версию и не был изменён.');
                if (restored.schema !== schema) {
                    await request('drafts', 'readwrite', store => store.put({ ...stored, id: `${id}:schema1-backup` }));
                    migrated = true;
                }
                Object.assign(reg, {
                    revision: restored.revision || 0,
                    epoch: restored.epoch || 0,
                    typedState: restored.typedState,
                    typedRevision: restored.typedRevision ?? restored.revision ?? 0,
                    typedScopes: normalizeTypedScopes(restored.typedScopes),
                    operation: normalizeOperation(restored.operation, restored.revision || 0,
                        restored.typedRevision ?? restored.revision ?? 0),
                    raw: normalizeRaw(restored.raw, restored.revision || 0),
                    cancelledScopes: restored.cancelledScopes || {},
                    persistedRevision: restored.revision || 0,
                });
            }
            reg.restorePending = new Set(Object.entries(reg.raw)
                .filter(([, value]) => !reg.cancelledScopes[value.scope || 'main'])
                .map(([key]) => key));
            if (migrated) await queue(reg);
            const registrationId = crypto.randomUUID();
            reg.listener = event => captureField(reg, event.target);
            document.addEventListener('input', reg.listener, true);
            document.addEventListener('change', reg.listener, true);
            registrations.set(registrationId, reg);
            return {
                registrationId,
                hasDraft: !!stored,
                revision: reg.revision,
                typedState: reg.typedState,
                operation: reg.operation ? JSON.stringify(reg.operation) : null,
                typedScopes: Object.fromEntries(Object.entries(reg.typedScopes).map(([scope, value]) => [scope, value.data])),
            };
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
        }
    }

    function get(id) {
        const reg = registrations.get(id);
        if (!reg || reg.discarded || reg.released) throw new Error('Draft registration is not active.');
        return reg;
    }

    function validateScope(scope) {
        if (!scope || typeof scope !== 'string') throw new Error('Draft scope is required.');
    }

    function parseIntent(json) {
        const intent = JSON.parse(json);
        if (intent.version !== OPERATION_VERSION || !intent.kind || typeof intent.data !== 'string')
            throw new Error('Operation intent has an unsupported format.');
        const scopes = intent.acceptedScopes || [];
        if (!Array.isArray(scopes) || new Set(scopes).size !== scopes.length) {
            scopes.forEach(validateScope);
            throw new Error('Operation accepted scopes are invalid.');
        }
        scopes.forEach(validateScope);
        return intent;
    }

    async function begin(reg, intent) {
        captureAll(reg);
        await waitForIdle(reg);
        if (reg.operation) throw new Error('Предыдущая команда не сверена; новая попытка заблокирована.');
        const revision = ++reg.revision;
        const scopes = (intent.acceptedScopes || []).map(scope => ({
            name: scope,
            typedRevision: reg.typedScopes[scope]?.revision || 0,
            rawRevisions: Object.fromEntries(Object.entries(reg.raw)
                .filter(([, value]) => (value.scope || 'main') === scope)
                .map(([key, value]) => [key, value.revision])),
        }));
        reg.confirmedCommitRevision = null;
        reg.operation = {
            version: OPERATION_VERSION,
            attemptId: intent.attemptId || crypto.randomUUID(),
            kind: intent.kind,
            status: 'running',
            revision,
            data: intent.data,
            scopes,
            acceptLegacyTyped: !!intent.acceptLegacyTyped,
            legacyTypedRevision: reg.typedRevision,
        };
        await queue(reg);
        return reg.operation;
    }

    function requireOperation(reg, revision) {
        if (!reg.operation || reg.operation.revision !== revision)
            throw new Error('Operation completion does not match the durable attempt.');
        return reg.operation;
    }

    function hasDraftState(reg) {
        return reg.typedState !== null || Object.keys(reg.typedScopes).length > 0 ||
            Object.keys(reg.raw).length > 0 || reg.operation !== null ||
            Object.keys(reg.cancelledScopes).length > 0;
    }

    async function health(reg) {
        try { await waitForIdle(reg); } catch { }
        return {
            healthy: !reg.lastError && reg.pendingWrites === 0 && reg.persistedRevision === reg.revision,
            pendingWrites: reg.pendingWrites,
            lastError: reg.lastError?.message || null,
            hasOperation: !!reg.operation,
            revision: reg.revision,
            persistedRevision: reg.persistedRevision,
        };
    }

    async function flushCommittedDraft(reg) {
        await queue(reg);
        if (!hasDraftState(reg)) await remove(reg, false);
    }

    window.clayDraftStore = {
        register,
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
        async beginOperation(id, operation) {
            const reg = get(id);
            let kind = 'legacy';
            try { kind = JSON.parse(operation)?.Kind || kind; } catch { }
            const marker = await begin(reg, {
                kind,
                data: String(operation),
                acceptedScopes: [],
                acceptLegacyTyped: false,
            });
            return marker.revision;
        },
        async beginOperationIntent(id, intentJson) {
            const reg = get(id);
            const marker = await begin(reg, parseIntent(intentJson));
            return JSON.stringify(marker);
        },
        async rejectOperation(id, revision) {
            const reg = get(id);
            await waitForIdle(reg);
            requireOperation(reg, revision);
            reg.operation = null;
            reg.revision++;
            await queue(reg);
        },
        async unknownOperation(id, revision) {
            const reg = get(id);
            await waitForIdle(reg);
            const operation = requireOperation(reg, revision);
            reg.operation = { ...operation, status: 'unknown' };
            reg.revision++;
            await queue(reg);
        },
        async commitOperation(id, revision, typedState) {
            const reg = get(id);
            if (!reg.operation && reg.confirmedCommitRevision === revision) {
                await flushCommittedDraft(reg);
                return;
            }
            requireOperation(reg, revision);
            if (reg.lastError) await queue(reg);
            await waitForIdle(reg);
            const operation = requireOperation(reg, revision);
            for (const scope of operation.scopes || []) {
                if (reg.typedScopes[scope.name]?.revision === scope.typedRevision)
                    delete reg.typedScopes[scope.name];
                for (const [key, acceptedRevision] of Object.entries(scope.rawRevisions || {})) {
                    if (reg.raw[key]?.revision === acceptedRevision &&
                        (reg.raw[key]?.scope || 'main') === scope.name) {
                        delete reg.raw[key];
                        reg.restorePending.delete(key);
                    }
                }
            }
            if (reg.typedRevision === operation.legacyTypedRevision) {
                if (typedState !== undefined && typedState !== null) reg.typedState = typedState;
                else if (operation.acceptLegacyTyped) reg.typedState = null;
            }
            reg.operation = null;
            // Повтор после ошибки хранилища завершает уже подтверждённую попытку этого handle.
            reg.confirmedCommitRevision = revision;
            reg.revision++;
            await flushCommittedDraft(reg);
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
            reg.discarded = true;
            reg.epoch++;
            document.removeEventListener('input', reg.listener, true);
            document.removeEventListener('change', reg.listener, true);
            await reg.pending.catch(() => { });
            if (reg.operation) {
                reg.typedState = null;
                reg.typedScopes = {};
                reg.raw = {};
                reg.cancelledScopes = {};
                reg.restorePending.clear();
                reg.restoreFailures.clear();
                reg.revision++;
                const state = snapshot(reg);
                const payload = await encrypt(await userKey(reg.userKey), state);
                await request('drafts', 'readwrite', store => {
                    store.delete(`${draftKey(reg)}:schema1-backup`);
                    return store.put({ id: draftKey(reg), ...payload });
                });
                reg.persistedRevision = state.revision;
                reg.lastError = null;
            } else {
                await remove(reg, true);
            }
            registrations.delete(id);
            await reg.releaseLock?.();
        },
        async unregister(id) {
            const reg = get(id);
            document.removeEventListener('input', reg.listener, true);
            document.removeEventListener('change', reg.listener, true);
            try {
                await waitForIdle(reg);
            } finally {
                reg.released = true;
                registrations.delete(id);
                await reg.releaseLock?.();
            }
        },
        async retryStorage(id, notSentAttemptId) {
            const reg = get(id);
            if (notSentAttemptId && reg.operation?.attemptId === notSentAttemptId && reg.operation.status === 'running') {
                reg.operation = null;
                reg.revision++;
            }
            await queue(reg);
            return reg.operation ? JSON.stringify(reg.operation) : null;
        },
        async readRebaseState(id, scope) {
            const reg = get(id);
            validateScope(scope);
            await waitForIdle(reg);
            if (reg.operation) throw new Error('Сначала требуется сверка предыдущей операции.');
            return {
                revision: reg.revision,
                rawKeys: Object.keys(reg.raw).filter(key => (reg.raw[key]?.scope || 'main') === scope)
            };
        },
        async rebaseScope(id, scope, expectedRevision, snapshotJson, discardedRaw) {
            const reg = get(id);
            validateScope(scope);
            await waitForIdle(reg);
            if (reg.operation || reg.revision !== expectedRevision || reg.cancelledScopes[scope])
                throw new Error('Черновик изменился после открытия сверки.');
            const pending = [...reg.restorePending].some(key => (reg.raw[key]?.scope || 'main') === scope);
            if (pending || reg.restoreFailures.has(scope))
                throw new Error('Сначала полностью восстановите ввод этой области.');

            if (!Array.isArray(discardedRaw) || discardedRaw.some(key => !reg.raw[key] || (reg.raw[key].scope || 'main') !== scope))
                throw new Error('Выбранный для замены ввод принадлежит другой области.');
            for (const key of discardedRaw) delete reg.raw[key];
            reg.typedScopes[scope] = { data: snapshotJson, revision: ++reg.revision };
            try {
                await queue(reg);
                return true;
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
            const active = [...registrations.values()];
            const states = await Promise.all(active.map(health));
            return active.length > 0 && states.every(state => state.healthy);
        },
    };
})();
