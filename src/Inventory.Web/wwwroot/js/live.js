// The console's live client.
//
// One job: when the console says a stock level moved, ask this console for the regions that just went
// stale and put them back where they were. The browser never renders a template — it fetches the same
// server-rendered fragment the page was built from, so a live refresh and a reload cannot disagree
// about how a screen looks.
//
// It talks to the console's own hub, not to the Inventory service's. That service's hub wants a token,
// which the console's relay holds and this page never sees; this hub wants the session cookie the page
// already has. Nothing here is required for a page to render: without JavaScript, or with the hub
// unreachable, every screen still works and simply does not refresh itself.
(function () {
    'use strict';

    var body = document.body;
    var hub = body.dataset.liveHub;
    var station = body.dataset.liveStation || null;
    var state = document.querySelector('[data-live-state]');
    var stamp = document.querySelector('[data-live-stamp]');
    var signalR = window.signalR;

    if (!hub || !signalR) {
        return;
    }

    var DEBOUNCE_MS = 300;             // a burst of stock movements becomes one refresh
    var SETTLE_MS = [1500, 6500];      // when a movement a hop behind is expected to have arrived
    var RETRY_SECONDS = [0, 2, 5, 10];

    var timers = {};
    var busy = {};
    var deferred = {};
    var lastChange = null;

    // Whether the console itself is still following the Inventory service, and whether this page's
    // session is still open. Both are things the page cannot work out from what it is showing.
    var following = true;
    var ended = null;

    function region(kind) {
        return document.querySelector('[data-live="' + kind + '"]');
    }

    /**
     * What to ask for. A region that carries state in the address bar — the levels page, with
     * its filters — asks for itself with the query string the reader is looking at, so a
     * refresh never quietly changes the view they chose.
     */
    function sourceOf(element) {
        return element.dataset.liveQuery === 'true'
            ? element.dataset.liveSource + window.location.search
            : element.dataset.liveSource;
    }

    /**
     * The hub protocol's casing is not something a page should assume, so a field is read by
     * either spelling rather than trusting one of them.
     */
    function pick(source, name) {
        if (!source) {
            return null;
        }

        if (source[name] !== undefined) {
            return source[name];
        }

        var pascal = name.charAt(0).toUpperCase() + name.slice(1);

        return source[pascal] !== undefined ? source[pascal] : null;
    }

    function utcTime() {
        var now = new Date();
        var pad = function (value) {
            return ('0' + value).slice(-2);
        };

        return pad(now.getUTCHours()) + ':' + pad(now.getUTCMinutes()) + ':' + pad(now.getUTCSeconds());
    }

    function units() {
        var quantity = lastChange ? Number(lastChange.quantity) : null;

        if (quantity === null || isNaN(quantity)) {
            return '';
        }

        return quantity + (quantity === 1 ? ' unit' : ' units');
    }

    function say(text, failed) {
        if (!stamp) {
            return;
        }

        stamp.textContent = text;
        stamp.hidden = false;
        stamp.classList.toggle('is-failed', failed === true);
    }

    function setState(text, tone) {
        if (!state) {
            return;
        }

        state.textContent = text;
        state.hidden = false;
        state.classList.remove('is-live', 'is-off');

        if (tone) {
            state.classList.add(tone);
        }
    }

    /** The rail's one line about the live view: this page's connection, and the console's own link. */
    function sayLive() {
        if (following) {
            setState('Live updates on', 'is-live');

            return;
        }

        setState('Live updates idle — the console has lost the inventory service', 'is-off');
    }

    /**
     * Whose movement was it? On a shared terminal "something changed" and "that was me" are
     * different pieces of news, and only the second one is a receipt.
     */
    function attribution() {
        if (!lastChange) {
            return '';
        }

        if (lastChange.mine) {
            return ' · your movement of ' + units() + ' is confirmed';
        }

        return lastChange.origin ? ' · another terminal moved ' + units() : '';
    }

    /**
     * What the page band says. It is written once, when the movement it describes has been read
     * back, and then *kept*: the later passes exist so the read-model screens catch up, not so the
     * receipt can be erased a second later by a plainer sentence.
     */
    function pageStamp() {
        if (!lastChange) {
            return 'Updated ' + utcTime() + ' UTC';
        }

        if (!lastChange.receipt) {
            lastChange.receipt = 'Updated ' + utcTime() + ' UTC' + attribution();
        }

        return lastChange.receipt;
    }

    function markChanged(region) {
        if (!lastChange) {
            return;
        }

        var rows = region.querySelectorAll('tr[data-product]');

        for (var index = 0; index < rows.length; index++) {
            var row = rows[index];
            var product = Number(row.dataset.product);
            var warehouse = Number(row.dataset.warehouse);

            if (product !== lastChange.productId) {
                continue;
            }

            if (warehouse !== lastChange.warehouseId && warehouse !== lastChange.destinationWarehouseId) {
                continue;
            }

            row.classList.remove('is-changed', 'is-mine');
            void row.offsetWidth;    // restart the fade in a browser that would coalesce it
            row.classList.add(lastChange.mine ? 'is-mine' : 'is-changed');
        }
    }

    function refresh(kind) {
        var element = region(kind);

        if (!element) {
            return;
        }

        if (ended) {
            // The console answered "not signed in" or "not your screen". Asking again would only ask
            // again: the page has said what happened and stopped moving.
            say(ended, true);

            return;
        }

        // Nothing a reader cannot see is worth a request: it is remembered and done when the
        // tab comes back.
        if (document.hidden) {
            deferred[kind] = true;

            return;
        }

        if (busy[kind]) {
            deferred[kind] = true;

            return;
        }

        busy[kind] = true;
        element.setAttribute('aria-busy', 'true');

        fetch(sourceOf(element), { headers: { Accept: 'text/html' }, credentials: 'same-origin' })
            .then(function (response) {
                // A signed-out fetch is refused with a status rather than a redirect (see
                // ConsoleSession), so a fragment is never replaced by a sign-in page.
                if (response.status === 401 || response.status === 403) {
                    ended = response.status === 401
                        ? 'Your session ended at ' + utcTime() + ' UTC — sign in again'
                        : 'Your role no longer covers this screen — ask a manager';

                    throw new Error('refused');
                }

                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }

                return response.text();
            })
            .then(function (html) {
                element.innerHTML = html;
                element.setAttribute('aria-busy', 'false');
                delete busy[kind];
                markChanged(element);

                // The stamp belongs to the page's own region. The rail's count is re-read on its
                // own schedule, and letting that claim the page was updated would overstate how
                // fresh the table in front of the reader is.
                if (kind === 'page') {
                    say(pageStamp());
                }

                if (deferred[kind]) {
                    delete deferred[kind];
                    refresh(kind);
                }
            })
            .catch(function () {
                // The region keeps what it last showed, and says what happened: a screen that
                // silently stops moving is worse than one that admits it is stale.
                element.setAttribute('aria-busy', 'false');
                delete busy[kind];

                if (ended) {
                    say(ended, true);

                    return;
                }

                if (kind === 'page') {
                    say('Live update failed at ' + utcTime() + ' UTC — reload this page', true);
                }
            });
    }

    function schedule(kind, delay) {
        clearTimeout(timers[kind]);
        timers[kind] = setTimeout(
            function () {
                refresh(kind);
            },
            delay === undefined ? DEBOUNCE_MS : delay);
    }

    function stockChanged(notice) {
        var origin = pick(notice, 'origin');

        lastChange = {
            productId: pick(notice, 'productId'),
            warehouseId: pick(notice, 'warehouseId'),
            destinationWarehouseId: pick(notice, 'destinationWarehouseId'),
            quantity: pick(notice, 'quantity'),
            origin: origin,
            mine: !!origin && !!station && String(origin) === station,
            receipt: null
        };

        schedule('page');
        schedule('attention');

        // Not every region answers from the same place. The stock page reads the Inventory
        // service, which is the service that just wrote the movement — it is right immediately.
        // The overview and the rail read the Reporting and Alert read models, which are built
        // from events the outbox dispatcher delivers on a five-second poll, so they are a
        // moment behind by design. Reading those regions again when the movement has had time
        // to travel is what lets them catch up without anybody reloading.
        SETTLE_MS.forEach(function (delay, index) {
            clearTimeout(timers['settle' + index]);
            timers['settle' + index] = setTimeout(function () {
                refresh('page');
                refresh('attention');
            }, delay);
        });
    }

    // An acknowledgement made somewhere else, or an alert resolved by another screen, moves the
    // rail count without any stock changing. A slow read is enough for that.
    var polled = document.querySelector('[data-live][data-live-poll]');

    if (polled) {
        var interval = Number(polled.dataset.livePoll) || 0;

        if (interval > 0) {
            setInterval(function () {
                schedule('attention', 0);
            }, interval);
        }
    }

    document.addEventListener('visibilitychange', function () {
        if (document.hidden) {
            return;
        }

        Object.keys(deferred).forEach(function (kind) {
            delete deferred[kind];
            refresh(kind);
        });
    });

    var connection = new signalR.HubConnectionBuilder()
        // The console's own hub, behind the console's own session: the cookie has to travel.
        .withUrl(hub, { withCredentials: true })
        .withAutomaticReconnect(RETRY_SECONDS)
        .build();

    connection.on('StockChanged', stockChanged);

    // Said once when this page connects: whether the console is hearing anything at all. Without it
    // a page would show "live updates on" over a screen that can never move.
    connection.on('RelayState', function (relay) {
        following = pick(relay, 'following') !== false;
        sayLive();
    });

    connection.onreconnecting(function () {
        setState('Live updates paused — reconnecting');
    });

    connection.onreconnected(function () {
        sayLive();

        // Whatever happened while the connection was down: read it rather than guess it.
        refresh('page');
        refresh('attention');
    });

    connection.onclose(function () {
        setState('Live updates stopped — reload to follow again', 'is-off');
    });

    connection.start()
        .then(sayLive)
        .catch(function () {
            setState('Live updates unavailable — reload to try again', 'is-off');
        });
}());
