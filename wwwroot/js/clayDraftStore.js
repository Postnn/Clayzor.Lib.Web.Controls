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
    const OPERATION_VERSION = 1;
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
        if (url.protocol !== 'https:' || url.origin !== window.location.origin)
            throw new Error('Для защищённого черновика требуется HTTPS.');

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
            operation: reg.operation,
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

    function restoreDraftState(reg, restored) {
        const raw = withoutHistoricalSecrets(reg.formKey, restored.raw);
        Object.assign(reg, {
            revision: restored.revision || 0,
            epoch: restored.epoch || 0,
            typedState: restored.typedState ?? null,
            typedRevision: restored.typedRevision ?? restored.revision ?? 0,
            typedScopes: normalizeTypedScopes(restored.typedScopes),
            operation: normalizeOperation(restored.operation, restored.revision || 0,
                restored.typedRevision ?? restored.revision ?? 0),
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
            operation: reg.operation ? JSON.stringify(reg.operation) : null,
            cancelledScopes: Object.keys(reg.cancelledScopes),
            typedScopes: Object.fromEntries(Object.entries(reg.typedScopes).map(([scope, value]) => [scope, value.data])),
        };
    }

    const draftIdPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

    function ownsSnapshot(reg, state, draftId) {
        return state.format === FORMAT_VERSION && state.userKey === reg.userKey &&
            state.formKey === reg.formKey && state.entityKey === reg.entityKey && state.draftId === draftId;
    }

    function openExistingLegacyDb() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(DB_NAME);
            let absent = false;
            let blocked = false;
            request.onupgradeneeded = event => {
                absent = event.oldVersion === 0;
                request.transaction.abort();
            };
            request.onblocked = () => {
                blocked = true;
                reject(new Error('Хранилище прежних операций заблокировано.'));
            };
            request.onerror = () => absent ? resolve(null) : reject(request.error);
            request.onsuccess = () => {
                if (blocked) request.result.close();
                else resolve(request.result);
            };
        });
    }

    const legacyEvidenceError = 'Не удалось безопасно прочитать прежнюю операцию.';
    const positiveIntPattern = /^[1-9][0-9]{0,9}$/;
    const controlCharacterPattern = /[\u0000-\u001f\u007f-\u009f]/;

    function positiveInt(value) {
        return Number.isInteger(value) && value > 0 && value <= 2147483647;
    }

    function canonicalPositiveInt(value) {
        return typeof value === 'string' && positiveIntPattern.test(value) &&
            positiveInt(Number(value)) && String(Number(value)) === value;
    }

    function legacyTelNewScope(value) {
        if (value === 'new' || value === 'new:0') return true;
        return value.startsWith('new:') && canonicalPositiveInt(value.slice(4));
    }

    function legacyModelNewScope(value) {
        if (value === 'new') return true;
        return value.startsWith('new:') && canonicalInt32(value.slice(4));
    }

    function canonicalInt32(value) {
        const number = Number(value);
        return Number.isInteger(number) && number >= -2147483648 && number <= 2147483647 &&
            String(number) === value;
    }

    function legacyEquipmentCreationScope(value) {
        if (value === 'new') return true;
        const parts = value.split(':');
        return parts.length === 2 && parts.every(canonicalInt32);
    }

    function canonicalDutyDate(value) {
        if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000-'))
            return false;
        const date = new Date(value + 'T00:00:00Z');
        return Number.isFinite(date.valueOf()) && date.toISOString().slice(0, 10) === value;
    }

    function canonicalPoolRoute(value) {
        const separator = value.includes('\u001f') ? '\u001f' : ':';
        const parts = value.split(separator);
        if (parts.length !== 2 || !canonicalPositiveInt(parts[0])) return false;
        const type = parts[1];
        if (!type.trim() || type.length > 50 || controlCharacterPattern.test(type) ||
            (separator === '\u001f') !== type.includes(':')) return false;

        const sqlCharacters = new Set(new TextDecoder('windows-1251')
            .decode(Uint8Array.from({ length: 256 }, (_, byte) => byte)));
        return [...type].every(character => sqlCharacters.has(character));
    }

    function canonicalSlBlScope(entityKey) {
        const [address, kind, extra] = entityKey.split(':');
        return extra === undefined && (kind === '0' || kind === '1') &&
            !!address.trim() && address.length <= 260 && !controlCharacterPattern.test(address);
    }

    function legacyTerritoryNewScope(value) {
        return value === 'new' || value.startsWith('new:') &&
            (value.slice(4) === '0' || canonicalPositiveInt(value.slice(4)));
    }

    function legacyPhoneCodeNewScope(value) {
        return value === 'new' || value.startsWith('new:') &&
            value.length <= 512 && !controlCharacterPattern.test(value);
    }

    function legacyStringKey(value) {
        return typeof value === 'string' && value.length > 0 && value.length <= 20 && value !== '0' &&
            !controlCharacterPattern.test(value);
    }

    function legacyPhoneCodeKey(value) {
        return typeof value === 'string' && !!value.trim() && value.length <= 20 &&
            !controlCharacterPattern.test(value);
    }

    function legacyFmtnKey(value, length) {
        return typeof value === 'string' && value.length > 0 && value.length <= length &&
            !/[^0-9()_]/.test(value);
    }

    function validLegacyEvidenceScope(formKey, entityKey) {
        if (typeof entityKey !== 'string') return false;
        switch (formKey) {
            case 'maintenance': return entityKey === 'list';
            case 'duty':
                return entityKey === 'new' || /^\d{8}$/.test(entityKey) &&
                    canonicalDutyDate(entityKey.replace(/^(\d{4})(\d{2})(\d{2})$/, '$1-$2-$3'));
            case 'beeline-parameters':
                return !!entityKey.trim() && entityKey.length <= 50 && !controlCharacterPattern.test(entityKey);
            case 'phone-pool-attribute': return canonicalPoolRoute(entityKey);
            case 'slbl': return canonicalSlBlScope(entityKey);
            case 'inventory.net': return canonicalPositiveInt(entityKey);
            case 'inventory.tel': return canonicalPositiveInt(entityKey) || legacyTelNewScope(entityKey);
            case 'inventory.model-equipment': return canonicalPositiveInt(entityKey) || legacyModelNewScope(entityKey);
            case 'inventory.phone-template': return canonicalPositiveInt(entityKey);
            case 'equipment-ins': return legacyEquipmentCreationScope(entityKey);
            case 'equipment-form': return canonicalPositiveInt(entityKey);
            case 'ping-host': return entityKey.length > 0 && entityKey.length <= 253 &&
                entityKey === entityKey.trim() && !entityKey.includes('|') && !controlCharacterPattern.test(entityKey);
            case 'location-fact': return entityKey === 'workplace';
            case 'territory-tree-move': return entityKey === 'unrestricted';
            case 'territory-form': return canonicalPositiveInt(entityKey) || legacyTerritoryNewScope(entityKey);
            case 'phone-code-form': return legacyPhoneCodeNewScope(entityKey) ||
                entityKey.startsWith('existing:') && legacyPhoneCodeKey(entityKey.slice(9));
            case 'num-pool': return entityKey === 'new' || entityKey.startsWith('id:') && legacyStringKey(entityKey.slice(3));
            case 'sim-fmtn': return entityKey === 'new' || legacyFmtnKey(entityKey, 20);
            default: return false;
        }
    }

    function legacyCardProjection(formKey, entityKey, kind, snapshot) {
        const { Original, Current } = snapshot ?? {};
        if (formKey === 'territory-form') {
            const from = Original?.Record;
            const to = Current?.Record;
            const creating = legacyTerritoryNewScope(entityKey);
            const id = creating ? 0 : Number(entityKey);
            const parent = creating ? Number(entityKey.slice(4)) || null : from?.ParentId;
            if (!(creating ? kind === 'territory.create' : kind === 'territory.update' || kind === 'territory.delete') ||
                from?.Id !== id || to?.Id !== id || from.ParentId !== parent || to.ParentId !== parent ||
                parent !== null && !positiveInt(parent))
                throw new Error(legacyEvidenceError);
            return JSON.stringify({ Id: id, ParentId: parent });
        }
        if (formKey === 'phone-code-form') {
            const creating = legacyPhoneCodeNewScope(entityKey);
            const source = creating ? null : entityKey.slice(9);
            const target = Current?.Code;
            if (!(creating ? kind === 'phone-code.insert' : kind === 'phone-code.update' || kind === 'phone-code.delete') ||
                Original?.Code !== (source ?? '') || !legacyPhoneCodeKey(target) ||
                kind !== 'phone-code.delete' && target === '0' || !creating && target !== source)
                throw new Error(legacyEvidenceError);
            return JSON.stringify({ Source: source, Target: target });
        }
        if (formKey === 'sim-fmtn') {
            const source = entityKey === 'new' ? null : entityKey;
            const deleting = kind === 'delete';
            const target = deleting ? source : Current?.Prefix;
            if (kind !== 'save' && !deleting || Original?.Prefix !== (source ?? '') ||
                typeof Current?.Prefix !== 'string' || !legacyFmtnKey(target, deleting ? 20 : 11) ||
                !deleting && target === '0')
                throw new Error(legacyEvidenceError);
            return JSON.stringify({ Source: source, Target: target });
        }

        const creating = entityKey === 'new';
        const deleting = kind === 'numpool.delete';
        const number = creating ? Current?.Data?.Header?.Number : entityKey.slice(3);
        if (kind !== 'numpool.save' && !deleting || creating && deleting || !legacyStringKey(number) ||
            Original?.Data?.Header?.Number !== (creating ? '' : number) || Current?.Data?.Header?.Number !== number)
            throw new Error(legacyEvidenceError);

        const from = legacyNumPoolPhones(Original.Data.Phones);
        const to = legacyNumPoolPhones(Current.Data.Phones);
        if (creating && from.size !== 0) throw new Error(legacyEvidenceError);
        if (deleting) to.clear();
        const ids = new Set([...from.keys(), ...to.keys()]);
        const changed = [...ids].filter(id => from.get(id) !== to.get(id)).sort((a, b) => a - b);
        return JSON.stringify({ Number: number, PhoneIds: changed });
    }

    function legacyNumPoolPhones(links) {
        if (!Array.isArray(links)) throw new Error(legacyEvidenceError);
        const phones = new Map();
        for (const link of links) {
            if (!positiveInt(link?.Id) || !Number.isInteger(link.Private) || link.Private < 0 || link.Private > 255 ||
                phones.has(link.Id))
                throw new Error(legacyEvidenceError);
            phones.set(link.Id, link.Private);
        }
        return phones;
    }

    function projectLegacyEquipmentOperation(entityKey, kind, data, snapshot, scopes) {
        const equipmentId = Number(entityKey);
        if (snapshot?.Kind !== kind || snapshot.EquipmentId !== equipmentId ||
            !Array.isArray(scopes) || scopes.length > 1 ||
            scopes.some(scope => typeof scope?.name !== 'string'))
            throw new Error(legacyEvidenceError);

        const { Editor, Intent, Origin } = snapshot;
        const scope = scopes[0]?.name;
        const child = (editor, expectedScope) => {
            if (Origin !== 'child' || Editor !== editor ||
                (expectedScope === undefined
                    ? typeof scope !== 'string' || !scope.startsWith('child:' + editor + ':') || !scope.endsWith(':root')
                    : scope !== expectedScope))
                throw new Error(legacyEvidenceError);
        };
        const quick = () => {
            if (Origin !== 'child' || Editor !== null || scopes.length !== 0)
                throw new Error(legacyEvidenceError);
        };
        const binding = (name, valid) => {
            if (!Intent || typeof Intent !== 'object' || Array.isArray(Intent) ||
                Object.keys(Intent).length !== 1 || !Object.hasOwn(Intent, name) || !valid(Intent[name]))
                throw new Error(legacyEvidenceError);
            return JSON.stringify({ [name]: Intent[name] });
        };
        const idFor = (name, inserting) => binding(name, id => inserting ? id === 0 : positiveInt(id));
        const inserted = kind.endsWith('.insert');
        const edited = editor => child(editor, 'child:' + editor + ':new:root');

        switch (kind) {
            case 'equipment-save':
            case 'equipment-phone-save':
                if (Origin !== 'main' || Editor !== null || scope !== 'main')
                    throw new Error(legacyEvidenceError);
                return '{}';
            case 'equipment-worknet-attach':
            case 'equipment-worknet-detach':
                quick();
                if (Intent?.EquipmentId !== equipmentId || Intent.Attach !== (kind === 'equipment-worknet-attach'))
                    throw new Error(legacyEvidenceError);
                return '{}';
            case 'equipment-accounting.insert':
            case 'equipment-accounting.update':
            case 'equipment-accounting.delete':
                if (kind.endsWith('.delete')) quick();
                else child('EquipmentAccountingDialog');
                return idFor('AccountingId', inserted);
            case 'equipment-software.insert':
            case 'equipment-software.update':
                child('EquipmentSoftwareDialog');
                return idFor('InstallationId', inserted);
            case 'equipment-sim-service.insert':
            case 'equipment-sim-service.update':
                child('EquipmentSimServiceDialog');
                return idFor('ServiceId', inserted);
            case 'equipment-sim.insert':
            case 'equipment-sim.update':
            case 'equipment-sim.delete':
                if (kind.endsWith('.delete')) quick();
                else edited('EquipmentSimDialog');
                return binding('DuplicateEquipmentId', id => id === null ||
                    !kind.endsWith('.delete') && positiveInt(id) && id !== equipmentId);
            case 'equipment-license.insert':
            case 'equipment-license.update':
                edited('EquipmentLicenseDialog');
                return binding('ExpectedExisting', value => value === !inserted);
            case 'equipment-computer.insert':
            case 'equipment-computer.update':
                edited('EquipmentComputerDialog');
                return binding('Date', value => typeof value === 'string' && /^\d{8}$/.test(value) &&
                    canonicalDutyDate(value.replace(/^(\d{4})(\d{2})(\d{2})$/, '$1-$2-$3')));
            case 'equipment-account.insert':
            case 'equipment-account.update':
            case 'equipment-account.delete':
                if (kind.endsWith('.delete')) quick();
                else child('EquipmentAccountDialog');
                return binding('Binding', value => typeof value === 'string' && /^[0-9a-f]{64}$/.test(value));
            case 'equipment-ups.link.insert':
            case 'equipment-ups.link.delete':
            case 'equipment-ups.details.save': {
                const result = binding('UpsEquipmentId', id => positiveInt(id) && id !== equipmentId);
                if (kind === 'equipment-ups.details.save')
                    child('EquipmentUpsDialog', 'child:EquipmentUpsDialog:' + Intent.UpsEquipmentId + ':root');
                else quick();
                return result;
            }
            case 'equipment-location-period.add':
            case 'equipment-location-period.update':
            case 'equipment-responsible-period.issue':
            case 'equipment-responsible-period.update':
            case 'equipment-responsible-period.withdraw': {
                binding('FromUtcTicks', Number.isInteger);
                // .NET ticks exceed Number.MAX_SAFE_INTEGER. Keep the exact decimal token.
                const ticks = [...data.matchAll(/"FromUtcTicks"\s*:\s*([1-9]\d{0,18})\s*(?=[,}])/g)];
                if (ticks.length !== 1 || BigInt(ticks[0][1]) > 3155378975999999999n ||
                    Number(ticks[0][1]) !== Intent.FromUtcTicks)
                    throw new Error(legacyEvidenceError);
                const value = ticks[0][1];
                if (kind === 'equipment-location-period.add') edited('EquipmentLocationDialog');
                else if (kind === 'equipment-location-period.update')
                    child('EquipmentLocationDialog', 'child:EquipmentLocationDialog:' + value + ':root');
                else {
                    const key = kind.endsWith('.issue') ? 'Issue:' :
                        (kind.endsWith('.update') ? 'Edit:' : 'Withdraw:') + value;
                    child('EquipmentResponsibleDialog', 'child:EquipmentResponsibleDialog:' + key + ':root');
                }
                return JSON.stringify({ FromUtcTicks: value });
            }
            case 'configure-phone-equipment':
                if (Origin !== 'configure' || Editor !== null || scopes.length !== 0)
                    throw new Error(legacyEvidenceError);
                return '{}';
            case 'retry-phone-sync':
                quick();
                return '{}';
            case 'Добавление расположения':
            case 'Изменение расположения':
                child('EquipmentLocationDialog');
                return '{}';
            case 'Сохранение ответственного':
                child('EquipmentResponsibleDialog');
                return '{}';
            case 'Добавление учётной записи':
            case 'Изменение учётной записи':
                child('EquipmentAccountDialog');
                return '{}';
            case 'Сохранение конфигурации компьютера':
                child('EquipmentComputerDialog');
                return '{}';
            case 'Сохранение SIM':
                child('EquipmentSimDialog');
                return '{}';
            case 'Сохранение дополнительной услуги SIM':
                child('EquipmentSimServiceDialog');
                return '{}';
            case 'Сохранение лицензии':
                child('EquipmentLicenseDialog');
                return '{}';
            case 'Сохранение установленного ПО':
                child('EquipmentSoftwareDialog');
                return '{}';
            case 'Сохранение бухгалтерского учёта':
                child('EquipmentAccountingDialog');
                return '{}';
            case 'Сохранение подключения UPS':
                child('EquipmentUpsDialog');
                return '{}';
            case 'Удаление учётной записи':
            case 'Удаление сведений SIM':
            case 'Удаление бухгалтерского учёта':
            case 'Подключение к локальной сети':
            case 'Отключение от локальной сети':
            case 'Подключение UPS':
                quick();
                return '{}';
            default:
                throw new Error(legacyEvidenceError);
        }
    }

    function projectLegacyOperation(formKey, entityKey, kind, data, attemptId, scopes) {
        if (formKey === 'maintenance') {
            if (kind !== 'maintenance.complete' || !canonicalPositiveInt(data)) throw new Error(legacyEvidenceError);
            return data;
        }
        if (formKey === 'inventory.net' && kind === 'net.poe') {
            if (data !== entityKey) throw new Error(legacyEvidenceError);
            return entityKey;
        }

        if (data.length > (formKey === 'num-pool' || formKey === 'equipment-form' ? 1024 * 1024 : 16_384))
            throw new Error(legacyEvidenceError);
        let snapshot;
        try { snapshot = JSON.parse(data); }
        catch { throw new Error(legacyEvidenceError); }

        if (formKey === 'equipment-form')
            return projectLegacyEquipmentOperation(entityKey, kind, data, snapshot, scopes);

        if (['territory-form', 'phone-code-form', 'sim-fmtn', 'num-pool'].includes(formKey))
            return legacyCardProjection(formKey, entityKey, kind, snapshot);

        if (formKey === 'inventory.net' && kind === 'net.save') {
            const { PortId, SwitchToId: storedSwitch, ExchangeWithId: storedExchange } = snapshot ?? {};
            const SwitchToId = storedSwitch === -1 ? null : storedSwitch;
            const ExchangeWithId = storedExchange === -1 ? null : storedExchange;
            if (PortId !== Number(entityKey) ||
                SwitchToId !== null && !positiveInt(SwitchToId) ||
                ExchangeWithId !== null && !positiveInt(ExchangeWithId) ||
                SwitchToId === PortId || ExchangeWithId === PortId ||
                SwitchToId !== null && ExchangeWithId !== null)
                throw new Error(legacyEvidenceError);
            return JSON.stringify({ PortId, SwitchToId, ExchangeWithId });
        }
        if (formKey === 'inventory.tel' && kind === 'tel.write') {
            const { PhoneId, Deleting, ChangedAtUtc, Fingerprint } = snapshot ?? {};
            const newPhone = legacyTelNewScope(entityKey);
            if (PhoneId !== (newPhone ? 0 : Number(entityKey)) || typeof Deleting !== 'boolean' ||
                newPhone && Deleting || typeof ChangedAtUtc !== 'string' || !ChangedAtUtc ||
                typeof Fingerprint !== 'string' || !Fingerprint)
                throw new Error(legacyEvidenceError);
            return JSON.stringify({ PhoneId, Deleting });
        }

        if (formKey === 'inventory.model-equipment' || formKey === 'inventory.phone-template') {
            const model = formKey === 'inventory.model-equipment';
            const { Id, ExpectedChangedAt, Fingerprint } = snapshot ?? {};
            const expectedId = model && legacyModelNewScope(entityKey) ? 0 : Number(entityKey);
            if (kind !== (model ? 'equipment-model.save' : 'equipment-phone-template.save') ||
                Id !== expectedId || !(ExpectedChangedAt === null ||
                    typeof ExpectedChangedAt === 'string' && ExpectedChangedAt.length > 0) ||
                model && (typeof Fingerprint !== 'string' || Fingerprint.length !== 64 || !/^[0-9a-f]{64}$/i.test(Fingerprint)))
                throw new Error(legacyEvidenceError);
            return String(expectedId);
        }

        if (formKey === 'equipment-ins') {
            if (kind !== 'equipment.create' || entityKey === 'new' || snapshot?.Source !== entityKey)
                throw new Error(legacyEvidenceError);
            return entityKey;
        }

        if (formKey === 'location-fact') {
            const SiteId = snapshot?.Expected?.Site?.Id;
            const EquipmentId = snapshot?.Expected?.Equipment?.EquipmentId;
            if (kind !== 'confirm-location' || !positiveInt(SiteId) || !positiveInt(EquipmentId))
                throw new Error(legacyEvidenceError);
            return JSON.stringify({ SiteId, EquipmentId });
        }

        if (formKey === 'territory-tree-move') {
            if (kind !== 'territory.move' || snapshot?.To === undefined ||
                snapshot.AttemptId != null && canonicalLegacyAttemptId(snapshot.AttemptId) !== attemptId)
                throw new Error(legacyEvidenceError);
            const From = projectLegacyMoveNode(snapshot?.From);
            const To = snapshot.To === null ? null : projectLegacyMoveNode(snapshot.To);
            if (To?.Id === From.Id) throw new Error(legacyEvidenceError);
            return JSON.stringify({ From, To });
        }

        if (formKey === 'duty') {
            const date = snapshot?.Draft?.Date;
            if (kind !== 'duty.save' || !canonicalDutyDate(date) ||
                entityKey !== 'new' && date.replaceAll('-', '') !== entityKey)
                throw new Error(legacyEvidenceError);
            return date;
        }

        if (formKey === 'beeline-parameters' && kind === 'beeline.update' &&
            typeof snapshot?.Current?.TimeZone === 'string' && typeof snapshot.Current.EmailManager === 'string' ||
            formKey === 'phone-pool-attribute' &&
            ['phone-pool-attribute.insert', 'phone-pool-attribute.update', 'phone-pool-attribute.delete'].includes(kind) &&
            typeof snapshot?.Current?.Value === 'string') {
            // Former formats can omit Version; when present it must agree with the owning registration.
            if (snapshot.Version != null && snapshot.Version.Key !== entityKey) throw new Error(legacyEvidenceError);
            return entityKey;
        }

        throw new Error(legacyEvidenceError);
    }

    function projectLegacyMoveNode(node) {
        const { Id, ParentId, Left, Right } = node ?? {};
        if (!positiveInt(Id) || ParentId !== null && !positiveInt(ParentId) ||
            !Number.isInteger(Left) || Left < 0 || !positiveInt(Right) || Right <= Left)
            throw new Error(legacyEvidenceError);
        return { Id, ParentId, Left, Right };
    }

    function canonicalLegacyAttemptId(value) {
        if (typeof value !== 'string' || !(/^[0-9a-f]{32}$/i.test(value) || draftIdPattern.test(value)))
            throw new Error(legacyEvidenceError);
        const normalizedId = value.replaceAll('-', '').toLowerCase();
        if (normalizedId.length !== 32 || normalizedId === '0'.repeat(32))
            throw new Error(legacyEvidenceError);
        return normalizedId;
    }

    function legacySlBlOperationId(entityKey, kind, data) {
        // A character may occupy six bytes in either of the two JSON list values.
        if (kind !== 'slbl.sync' || data.length > 128 * 1024)
            throw new Error(legacyEvidenceError);
        let snapshot;
        try { snapshot = JSON.parse(data); }
        catch { throw new Error(legacyEvidenceError); }

        const { OperationId, Address, Kind } = snapshot ?? {};
        if (typeof Address !== 'string' || (Kind !== 0 && Kind !== 1) ||
            `${Address}:${Kind}` !== entityKey)
            throw new Error(legacyEvidenceError);
        return canonicalLegacyAttemptId(OperationId);
    }

    function legacyOperationEvidence(context, operation) {
        if (operation == null) return null;
        const { attemptId, kind, data, status } = operation;
        if (operation.version !== OPERATION_VERSION || !['running', 'unknown', 'confirmed'].includes(status) ||
            typeof data !== 'string')
            throw new Error(legacyEvidenceError);

        const normalizedId = canonicalLegacyAttemptId(attemptId);
        if (context.formKey === 'slbl')
            return { attemptId: legacySlBlOperationId(context.entityKey, kind, data), kind, data: context.entityKey };
        return { attemptId: normalizedId, kind, data: projectLegacyOperation(context.formKey, context.entityKey, kind, data, normalizedId, operation.scopes) };
    }


    const activeLegacyTransfers = new Map();
    const usedLegacyTransferIds = new Set();

    function legacyTransferId(value) {
        if (typeof value !== 'string' || !(draftIdPattern.test(value) || /^[0-9a-f]{32}$/i.test(value)))
            throw new Error('Неверный идентификатор передачи прежней операции.');
        const id = value.replaceAll('-', '').toLowerCase();
        if (id === '0'.repeat(32)) throw new Error('Неверный идентификатор передачи прежней операции.');
        return id;
    }

    async function releaseLegacyEvidenceLocks(lease) {
        lease.finished = true;
        lease.cleanup ??= (async () => {
            await lease.retirement?.catch(() => { });
            await Promise.all([
                ...lease.locks.splice(0).reverse().map(release => release()),
                lease.pendingLock?.catch(() => { }),
            ]);
        })();
        await lease.cleanup;
    }

    async function holdLegacyEvidenceLock(lease, name, conflictMessage) {
        if (lease.finished) throw new Error('Передача прежней операции уже завершена.');
        lease.pendingLock = claimLock(name).then(async release => {
            if (!release) throw new Error(conflictMessage);
            if (lease.finished) {
                await release();
                throw new Error('Передача прежней операции уже завершена.');
            }
            lease.locks.push(release);
        });
        try { await lease.pendingLock; }
        finally { lease.pendingLock = null; }
    }

    async function beginLegacyOperationEvidenceTransfer(formKey, entityKey, userKey, leaseId, stageForRetirement = false) {
        const id = legacyTransferId(leaseId);
        if (usedLegacyTransferIds.has(id))
            throw new Error('Идентификатор передачи прежней операции уже использован.');
        if (!navigator.locks?.request)
            throw new Error('Не удалось проверить открытые вкладки прежнего редактора.');

        // IDs are single-use in a document, including a finish delivered before begin.
        usedLegacyTransferIds.add(id);
        const lease = { locks: [], finished: false, records: stageForRetirement ? [] : null };
        activeLegacyTransfers.set(id, lease);
        try {
            const evidence = await readLegacyOperationEvidenceUnderLock(formKey, entityKey, userKey, lease, true);
            lease.ready = true;
            return evidence;
        } catch (error) {
            await finishLegacyOperationEvidenceTransfer(id);
            throw error;
        }
    }

    async function retireLegacyOperationEvidenceTransfer(leaseId) {
        const lease = activeLegacyTransfers.get(legacyTransferId(leaseId));
        if (!lease?.ready || lease.finished || !lease.records)
            throw new Error('Перенос прежних черновиков не подтверждён.');
        lease.retirement ??= retireLegacySnapshots(lease);
        return lease.retirement;
    }

    async function retireLegacySnapshots(lease) {
        if (lease.records.length === 0) return;
        const db = await openExistingLegacyDb();
        if (!db) throw new Error('Прежние черновики недоступны.');
        try {
            if (lease.finished) throw new Error('Перенос прежних черновиков уже завершён.');
            await new Promise((resolve, reject) => {
                const tx = db.transaction('drafts', 'readwrite');
                const store = tx.objectStore('drafts');
                tx.oncomplete = resolve;
                tx.onerror = () => reject(tx.error);
                tx.onabort = () => reject(tx.error || new Error('Прежний черновик изменился во время переноса.'));
                for (const { id, record } of lease.records) {
                    const read = store.get(id);
                    read.onsuccess = () => {
                        const current = read.result;
                        if (!current) return;
                        if (current.iv !== record.iv || current.data !== record.data) {
                            tx.abort();
                            return;
                        }
                        store.delete(id);
                    };
                }
            });
            for (const { sessionKey, draftId } of lease.records) {
                if (sessionStorage.getItem(sessionKey) === draftId)
                    sessionStorage.removeItem(sessionKey);
            }
        } finally {
            db.close();
        }
    }

    async function finishLegacyOperationEvidenceTransfer(leaseId) {
        const id = legacyTransferId(leaseId);
        usedLegacyTransferIds.add(id);
        const lease = activeLegacyTransfers.get(id);
        if (!lease) return;
        try { await releaseLegacyEvidenceLocks(lease); }
        finally { activeLegacyTransfers.delete(id); }
    }

    async function readLegacyOperationEvidence(formKey, entityKey, userKey) {
        const lease = { locks: [], finished: false };
        try {
            return await readLegacyOperationEvidenceUnderLock(formKey, entityKey, userKey, lease, false);
        } finally {
            await releaseLegacyEvidenceLocks(lease);
        }
    }

    async function readLegacyOperationEvidenceUnderLock(formKey, entityKey, userKey, lease, holdAbsentScope) {
        const validUserKey = formKey === 'equipment-form'
            ? typeof userKey === 'string' && userKey.startsWith('inventory:') && canonicalPositiveInt(userKey.slice(10))
            : canonicalPositiveInt(userKey);
        if (!validLegacyEvidenceScope(formKey, entityKey) || !validUserKey)
            throw new Error('Неверный контекст прежней операции.');

        const newScopeValidator = entityKey !== 'new' ? null :
            formKey === 'inventory.tel' ? legacyTelNewScope :
            formKey === 'inventory.model-equipment' ? legacyModelNewScope :
            formKey === 'equipment-ins' ? legacyEquipmentCreationScope :
            formKey === 'territory-form' ? legacyTerritoryNewScope :
            formKey === 'phone-code-form' ? legacyPhoneCodeNewScope : null;
        const scopePrefix = formKey === 'equipment-ins' ? '' : 'new:';
        const defaultNewScope = formKey === 'phone-code-form' ? 'new:' : scopePrefix ? 'new:0' : '0:0';
        const initialScopeKeys = newScopeValidator ? ['new', defaultNewScope] : [entityKey];
        const lockedScopeKeys = new Set();
        const lockScopes = async scopeKeys => {
            if (!navigator.locks?.request) return;
            for (const scopeKey of scopeKeys) {
                if (lockedScopeKeys.has(scopeKey)) continue;
                await holdLegacyEvidenceLock(lease, `clay-draft-operation:${JSON.stringify([userKey, formKey, scopeKey])}`,
                    'Другая вкладка подтверждает прежнюю операцию.');
                lockedScopeKeys.add(scopeKey);
            }
        };
        if (holdAbsentScope) await lockScopes(initialScopeKeys);
        const db = await openExistingLegacyDb();
        if (!db && !holdAbsentScope) return [];
        try {
            const evidence = new Map();
            const scopeKeys = newScopeValidator
                ? await legacyNewScopes(db, formKey, userKey, initialScopeKeys, newScopeValidator, scopePrefix)
                : initialScopeKeys;
            await lockScopes(scopeKeys);
            if (db) {
                for (const scopeKey of scopeKeys)
                    await scanLegacyOperationScope(db, { formKey, entityKey: scopeKey, userKey }, evidence, lease, entityKey);
                if (newScopeValidator && [...await legacyNewScopes(db, formKey, userKey, initialScopeKeys, newScopeValidator, scopePrefix)]
                    .some(key => !scopeKeys.has(key)))
                    throw new Error('Другая вкладка открыла прежний редактор. Повторите проверку.');
            }
            if (lease.finished) throw new Error('Передача прежней операции уже завершена.');
            return [...evidence.values()];
        } finally {
            db?.close();
        }
    }

    async function legacyNewScopes(db, formKey, userKey, initialScopeKeys, validate, scopePrefix) {
        const scopes = new Set(initialScopeKeys);
        if (!db) return scopes;
        const prefix = draftPrefix({ formKey, entityKey: scopePrefix, userKey }).slice(0, -1);
        const records = await storeRequest(db, 'drafts', 'readonly', store =>
            store.getAllKeys(IDBKeyRange.bound(prefix, prefix + '\uffff')));
        for (const id of records) {
            const boundary = id.lastIndexOf('|');
            if (boundary < prefix.length || !draftIdPattern.test(id.slice(boundary + 1).replace(/:schema1-backup$/, '')))
                throw new Error('Не удалось определить владельца прежней операции.');
            const scope = scopePrefix + id.slice(prefix.length, boundary);
            if (!validate(scope))
                throw new Error('Не удалось определить владельца прежней операции.');
            scopes.add(scope);
        }
        return scopes;
    }

    async function scanLegacyOperationScope(db, context, evidence, lease, requestedEntityKey) {
        const { formKey, entityKey, userKey } = context;
        const prefix = draftPrefix(context);
        const backupSuffix = ':schema1-backup';
        const records = await storeRequest(db, 'drafts', 'readonly', store =>
            store.getAllKeys(IDBKeyRange.bound(prefix, prefix + '\uffff')));
        const draftIds = new Set();
        for (const id of records) {
            const name = id.slice(prefix.length);
            const draftId = name.endsWith(backupSuffix) ? name.slice(0, -backupSuffix.length) : name;
            const separator = draftId.lastIndexOf('|');
            // Legacy keys are delimiter-joined. A longer entity can share the queried prefix.
            if (separator >= 0 && draftIdPattern.test(draftId.slice(separator + 1))) continue;
            if (!draftIdPattern.test(draftId))
                throw new Error('Не удалось определить владельца прежней операции.');
            draftIds.add(draftId);
        }
        if (!draftIds.size) return;
        if (!navigator.locks?.request)
            throw new Error('Не удалось проверить открытые вкладки прежнего редактора.');

        const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(userKey));
        const keyId = bytesToBase64(new Uint8Array(digest));
        const storedKey = await storeRequest(db, 'keys', 'readonly', store => store.get(keyId));
        if (!storedKey?.key)
            throw new Error('Ключ прежней операции недоступен.');

        for (const draftId of draftIds) {
            await holdLegacyEvidenceLock(lease, `clay-draft:${formKey}:${entityKey}:${draftId}`,
                'Прежний редактор открыт в другой вкладке.');
            const snapshots = lease.records ? [] : null;
            let hasOperationEvidence = false;
            for (const id of [prefix + draftId, prefix + draftId + backupSuffix]) {
                const record = await storeRequest(db, 'drafts', 'readonly', store => store.get(id));
                if (!record) continue;
                const state = await decrypt(storedKey.key, record);
                if (!ownsSnapshot(context, state, draftId))
                    throw new Error('Не удалось подтвердить владельца прежней операции.');
                snapshots?.push({ id, record, draftId, sessionKey: `clay-draft:${userKey}:${formKey}:${entityKey}` });

                const candidate = legacyOperationEvidence(context, state.operation);
                if (!candidate) continue;
                const existing = evidence.get(candidate.attemptId);
                if (existing && (existing.kind !== candidate.kind || existing.data !== candidate.data))
                    throw new Error('Сохранены расходящиеся сведения о прежней операции.');
                evidence.set(candidate.attemptId, candidate);
                hasOperationEvidence = true;
            }
            // The aggregate scan may also find input from a different creation context.
            if (snapshots && (entityKey === requestedEntityKey || hasOperationEvidence))
                lease.records.push(...snapshots);
        }
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
            if (!state.operation && state.typedState == null && !Object.keys(state.typedScopes || {}).length &&
                !Object.keys(state.raw || {}).length) continue;
            candidates.push({
                draftId, revision: state.revision || 0,
                hasOperation: !!state.operation, canRecover: state.schema === reg.schema,
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
                operation: null, raw: {}, cancelledScopes: {}, pending: Promise.resolve(), pendingWrites: 0,
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

    function ensureAcceptedScopesRestored(reg, intent) {
        const scopes = new Set(intent.acceptedScopes || []);
        if (intent.acceptLegacyTyped) scopes.add('main');
        if ([...scopes].some(scope => reg.restoreFailures.has(scope) || reg.cancelledScopes[scope]) ||
            [...reg.restorePending].some(key => reg.raw[key] && scopes.has(reg.raw[key].scope || 'main'))) {
            throw new Error('Сначала полностью восстановите ввод в принимаемых областях.');
        }
    }

    async function begin(reg, intent) {
        if (!intent.blockOtherDraftOperations) return recordAttempt(reg, intent);

        if (!navigator.locks && !window.BroadcastChannel)
            throw new Error('В браузере недоступна проверка параллельной записи.');
        const context = JSON.stringify([reg.userKey, reg.formKey, reg.entityKey]);
        const release = await claimLock(`clay-draft-operation:${context}`);
        if (!release) throw new Error('Другая вкладка подтверждает запись в этом контексте.');
        try {
            const drafts = await listRecoveryCandidates(reg);
            if (drafts.some(candidate => candidate.hasOperation))
                throw new Error('В другом черновике есть незавершённая операция. Сверьте её перед записью.');
            return await recordAttempt(reg, intent);
        } finally {
            await release();
        }
    }

    async function recordAttempt(reg, intent) {
        if (reg.discarded || reg.released) throw new Error('Draft registration is not active.');
        ensureAcceptedScopesRestored(reg, intent);
        captureAll(reg);
        await waitForIdle(reg);
        if (reg.discarded || reg.released) throw new Error('Draft registration is not active.');
        ensureAcceptedScopesRestored(reg, intent);
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

    function requireConfirmedOperation(reg, revision) {
        const operation = requireOperation(reg, revision);
        if (operation.status !== 'confirmed')
            throw new Error('Сначала подтвердите исход операции.');
        return operation;
    }

    function hasProtectedDraftState(reg) {
        return reg.typedState !== null || Object.keys(reg.typedScopes).length > 0 ||
            Object.keys(reg.raw).length > 0 || reg.operation !== null;
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
            hasOperation: !!reg.operation,
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
        const isCurrent = () => !reg.discarded && !reg.released && !reg.operation &&
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
        readLegacyOperationEvidence,
        beginLegacyOperationEvidenceTransfer,
        retireLegacyOperationEvidenceTransfer,
        finishLegacyOperationEvidenceTransfer,
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
            if (reg.ramOwner) get(id);
            return marker.revision;
        },
        async beginOperationIntent(id, intentJson) {
            const reg = get(id);
            const marker = await begin(reg, parseIntent(intentJson));
            if (reg.ramOwner) get(id);
            return JSON.stringify(marker);
        },
        async rejectOperation(id, revision) {
            const reg = get(id);
            await waitForIdle(reg);
            if (requireOperation(reg, revision).status === 'confirmed')
                throw new Error('Подтверждённый исход нельзя заменить отказом.');
            reg.operation = null;
            reg.revision++;
            await queue(reg);
        },
        async unknownOperation(id, revision) {
            const reg = get(id);
            await waitForIdle(reg);
            const operation = requireOperation(reg, revision);
            if (operation.status === 'confirmed')
                throw new Error('Подтверждённый исход нельзя заменить неизвестным.');
            reg.operation = { ...operation, status: 'unknown' };
            reg.revision++;
            await queue(reg);
        },
        async recordConfirmedOperation(id, revision) {
            const reg = get(id);
            if (!reg.operation && reg.confirmedCommitRevision === revision) return;
            const operation = requireOperation(reg, revision);
            if (operation.status === 'confirmed') {
                if (reg.lastError) await queue(reg);
                await waitForIdle(reg);
                return;
            }

            reg.operation = { ...operation, status: 'confirmed' };
            reg.revision++;
            await queue(reg);
        },
        async commitOperation(id, revision, typedState) {
            const reg = get(id);
            if (!reg.operation && reg.confirmedCommitRevision === revision) {
                await flushCommittedDraft(reg);
                return;
            }
            requireConfirmedOperation(reg, revision);
            if (reg.lastError) await queue(reg);
            await waitForIdle(reg);
            const operation = requireConfirmedOperation(reg, revision);
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
            if (reg.ramOwner) throw new Error('Для завершения временного черновика требуется закрыть редактор.');
            reg.discarded = true;
            abortEditorWrite(reg);
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
                const payload = await encrypt(await encryptionKey(reg), state);
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
        async retryStorage(id, notSentAttemptId) {
            const reg = get(id);
            if (notSentAttemptId && reg.operation?.attemptId === notSentAttemptId && reg.operation.status === 'running') {
                reg.operation = null;
                reg.revision++;
            }
            await queue(reg);
            return reg.operation ? JSON.stringify(reg.operation) : null;
        },
        async readEditorScope(id, scope) {
            const reg = get(id);
            validateScope(scope);
            await waitForIdle(reg);
            return {
                revision: reg.revision,
                hasRaw: Object.values(reg.raw).some(field => (field.scope || 'main') === scope),
                hasOperation: !!reg.operation,
            };
        },
        async readEditorCloseState(id) {
            const reg = get(id);
            await waitForIdle(reg);
            if (get(id) !== reg) throw new Error('Редактор уже закрыт.');
            return {
                revision: reg.revision,
                hasRaw: Object.keys(reg.raw).length !== 0,
                hasOperation: !!reg.operation,
                typedScopes: Object.keys(reg.typedScopes).sort(),
            };
        },
        async readCloseOperationEvidence(id, expectedRevision) {
            const reg = get(id);
            await waitForIdle(reg);
            if (!reg.ramOwner || get(id) !== reg || reg.revision !== expectedRevision || !reg.operation)
                throw new Error('Изменилась операция закрываемого редактора.');
            return legacyOperationEvidence(reg, reg.operation);
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
            const isCurrent = () => !reg.discarded && !reg.released && !reg.operation &&
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
