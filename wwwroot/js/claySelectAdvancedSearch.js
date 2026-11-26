window.claySelectAdvancedSearch = window.claySelectAdvancedSearch || {
    requests: Object.create(null),
    prepared: null,

    open: function (url, requestId, dotnetRef, multiple = false, acknowledged = false, recovery = null) {
        const target = new URL(url, window.location.href);
        if (!['http:', 'https:'].includes(target.protocol) || target.username || target.password)
            throw new Error('Invalid advanced search URL');
        const searchUrl = target.href;
        target.searchParams.set('return', multiple ? '2' : '1');
        target.searchParams.set('requestId', requestId);
        target.searchParams.set('returnOrigin', window.location.origin);
        if (acknowledged) target.searchParams.set('returnAck', '1');
        else target.searchParams.delete('returnAck');
        const formKey = 'advanced-search:' + target.origin + target.pathname.toLowerCase().replace(/\.aspx$/, '');
        const child = window.clayFormWindow.openHandle(target.toString(), formKey,
            { width: 1100, height: 700, left: 50, top: 50, resizable: true }, `clay-search-${requestId}`);
        if (!child) {
            void dotnetRef.invokeMethodAsync('CancelAsync', requestId).catch(() => {});
            return;
        }

        const request = {
            child, cleanup: null, applying: false, dispatched: false, searchUrl, multiple,
            accepted: null,
            proof: acknowledged ? recovery?.key : null,
            binding: { id: recovery?.bindingId, receiver: dotnetRef }
        };
        const cleanup = () => {
            window.removeEventListener('message', onMessage);
            window.clearInterval(closeTimer);
            if (this.requests[requestId] === request)
                delete this.requests[requestId];
        };
        request.cleanup = cleanup;
        this.requests[requestId] = request;

        const acknowledge = (message, decision) => {
            // Только исходная страница закрывает себя после проверки контекста подтверждения.
            try {
                child.postMessage({ type: 'clay-select-ack', requestId, revision: message.revision,
                    receipt: message.receipt, decision }, target.origin);
            } catch {
                // Подтверждённая квитанция остаётся в текущем запросе до повторной доставки или закрытия.
            }
        };

        const onMessage = async event => {
            if (event.origin !== target.origin || event.source !== child || this.requests[requestId] !== request
                || ((!acknowledged || request.proof) && request.applying)) return;

            const message = event.data;
            if (message?.type !== 'clay-select-return' || message.requestId !== requestId
                || Array.isArray(message.value) !== multiple || !Object.hasOwn(message, 'value') || message.value == null) return;
            if (acknowledged && (!Number.isInteger(message.revision) || message.revision < 1
                || message.revision > 2147483647 || !/^[0-9a-f]{32}$/.test(message.receipt))) return;

            let json = null;
            if (acknowledged) {
                try { json = JSON.stringify(message.value); }
                catch { return; }

                if (request.accepted) {
                    const same = request.accepted.revision === message.revision && request.accepted.json === json;
                    acknowledge(message, same ? 'Accepted' : 'Stale');
                    return;
                }
            }
            if (request.proof) {
                if (request.submitted && (request.submitted.revision !== message.revision || request.submitted.json !== json)) return;
                request.submitted = { revision: message.revision, json };
            }
            const binding = request.binding;
            request.applying = !acknowledged || !!request.proof;
            let decision;
            try {
                if (request.proof && (!binding?.receiver ||
                    !await binding.receiver.invokeMethodAsync('ReadyForSelectionAsync', requestId, message.revision) ||
                    request.binding !== binding || this.requests[requestId] !== request || child.closed)) {
                    decision = 'Unavailable';
                } else if (acknowledged) {
                    if (request.proof) request.dispatched = true;
                    decision = await binding.receiver.invokeMethodAsync('ApplyAsync', requestId, message.revision, message.value);
                } else {
                    await dotnetRef.invokeMethodAsync('CompleteAsync', requestId, message.value);
                    decision = 'Accepted';
                }
            } catch {
                decision = 'Unavailable';
            } finally {
                request.applying = false;
            }

            if (this.requests[requestId] !== request || child.closed) return;
            if (request.proof && decision === 'Rejected') request.submitted = null;
            if (acknowledged) {
                if (decision === 'Accepted')
                    request.accepted ??= { revision: message.revision, json };
                acknowledge(message, decision);
            } else if (decision === 'Accepted') {
                cleanup();
                child.close();
            }
        };

        const closeTimer = window.setInterval(() => {
            if (!child.closed) return;
            cleanup();
            void request.binding?.receiver.invokeMethodAsync('CancelAsync', requestId).catch(() => {});
        }, 500);
        window.addEventListener('message', onMessage);
    },

    resumeUndispatched: async function (url, requestId, proof, bindingId, dotnetRef, multiple) {
        const request = this.requests[requestId];
        if (!request?.proof || request.proof !== proof || request.searchUrl !== new URL(url, window.location.href).href
            || request.multiple !== multiple || request.dispatched || request.applying || request.child.closed) return false;
        const previous = request.binding;
        if (previous?.receiver) {
            let live = false;
            try { live = await previous.receiver.invokeMethodAsync('ReadyForSelectionAsync', requestId, request.submitted?.revision ?? 1); }
            catch { /* No selection is sent during the readiness check. */ }
            if (live) return false;
        }
        if (this.requests[requestId] !== request || request.binding !== previous || request.dispatched ||
            request.applying || request.child.closed) return false;
        request.binding = { id: bindingId, receiver: dotnetRef };
        return true;
    },

    detach: function (requestId, bindingId) {
        const request = this.requests[requestId];
        if (request?.proof && request.binding?.id === bindingId)
            request.binding = null;
    },

    cancel: function (requestId) {
        const request = this.requests[requestId];
        if (!request) return;
        request.cleanup();
        if (!request.child.closed)
            request.child.close();
    },

    returnValue: function (requestId, value) {
        const opener = window.opener;
        if (!opener || opener.closed || !/^[a-f\d]{32}$/i.test(requestId) || value == null) return false;
        const query = new URLSearchParams(window.location.search);
        if (query.get('return') !== (Array.isArray(value) ? '2' : '1') || query.get('requestId') !== requestId) return false;
        let openerOrigin;
        try { openerOrigin = opener.location.origin; }
        catch (_) { openerOrigin = document.referrer ? new URL(document.referrer).origin : null; }
        if (!openerOrigin || openerOrigin === 'null' || query.get('returnOrigin') !== openerOrigin) return false;

        const message = { type: 'clay-select-return', requestId, value };
        if (query.get('returnAck') !== '1') {
            opener.postMessage(message, openerOrigin);
            return true;
        }

        const context = window.location.href;
        let prepared = this.prepared;
        if (prepared?.context !== context || prepared.opener !== opener) {
            prepared = { context, opener, revision: 1, json: null, sending: null };
            this.prepared = prepared;
        }

        const json = JSON.stringify(value);
        // При потерянном ответе повторяем только ту же ревизию и то же значение.
        if (prepared.json !== null && prepared.json !== json) return false;
        if (prepared.sending) return prepared.sending;
        prepared.json = json;
        message.revision = prepared.revision;
        message.receipt = Array.from(crypto.getRandomValues(new Uint8Array(16)), n => n.toString(16).padStart(2, '0')).join('');

        prepared.sending = new Promise(resolve => {
            const finish = decision => {
                window.removeEventListener('message', onAck);
                window.clearTimeout(timeout);
                if (decision === 'Rejected') {
                    prepared.revision++;
                    prepared.json = null;
                }
                resolve(decision === 'Accepted');
                if (decision === 'Accepted') window.close();
            };
            const onAck = event => {
                const ack = event.data;
                if (this.prepared !== prepared || window.location.href !== context || window.opener !== opener
                    || event.source !== opener || event.origin !== openerOrigin || ack?.type !== 'clay-select-ack'
                    || ack.requestId !== requestId || ack.revision !== message.revision || ack.receipt !== message.receipt) return;
                finish(ack.decision);
            };
            const timeout = window.setTimeout(() => finish('Unavailable'), 15000);
            window.addEventListener('message', onAck);
            try { opener.postMessage(message, openerOrigin); }
            catch { finish('Unavailable'); }
        }).finally(() => { prepared.sending = null; });

        return prepared.sending;
    },

    close: function () { if (window.opener) window.close(); }
};
