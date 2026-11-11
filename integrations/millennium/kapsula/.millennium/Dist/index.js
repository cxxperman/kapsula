// Kapsula plugin for Millennium.
// Runs inside the Steam client. Whenever Steam decides to show one of its pop-up notifications
// (friend message, invite, friend online, achievement, download finished…), the plugin sends its
// title, text and icon to Kapsula on this PC over ws://127.0.0.1:42071. Nothing goes to the internet,
// and Steam's own notification is left exactly as it was.
// Written by hand in the same wrapper format Millennium's bundler produces, so it needs no build step.
const MILLENNIUM_IS_CLIENT_MODULE = !0, pluginName = "kapsula";
(window.PLUGIN_LIST ||= {})[pluginName] ||= {}, window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS ||= {};
let PluginEntryPointMain = function () {
  return function (exports, millenniumApi, react) {
    "use strict";
    const PORT = 42071, VERSION = "0.3.0";
    let ws = null, stopped = false, timer = null;
    const queue = [], cleanup = [];

    function send(obj) {
      let s;
      try { s = JSON.stringify(obj); } catch (e) { return; }
      if (ws && ws.readyState === 1) ws.send(s);
      else if (queue.length < 50) queue.push(s);
    }

    function connect() {
      if (stopped) return;
      timer = null;
      try {
        const sock = new WebSocket("ws://127.0.0.1:" + PORT + "/?client=millennium");
        sock.onopen = function () {
          ws = sock;
          sock.send(JSON.stringify({ type: "hello", plugin: VERSION, hooked: hooked }));
          queue.splice(0).forEach(function (s) { sock.send(s); });
        };
        sock.onmessage = function (e) { try { command(JSON.parse(e.data)); } catch (err) { } };
        sock.onclose = function () { if (ws === sock) ws = null; if (!stopped && !timer) timer = setTimeout(connect, 5000); };
        sock.onerror = function () { };
      } catch (e) {
        if (!timer) timer = setTimeout(connect, 5000);
      }
    }

    // The only command Kapsula can give: show Steam's own built-in sample notifications (used to check the link).
    function command(msg) {
      if (!msg || msg.cmd !== "test" || !Array.isArray(msg.names)) return;
      const store = window.NotificationStore;
      msg.names.slice(0, 12).forEach(function (name, i) {
        if (typeof name !== "string" || !/^Test[A-Za-z]{1,40}$/.test(name) || !store || typeof store[name] !== "function") return;
        setTimeout(function () { try { store[name](); } catch (e) { send({ type: "error", name: name, message: String(e) }); } }, 400 + i * 2500);
      });
    }

    // Notification data comes either as plain fields or as getter functions, depending on its kind.
    function field(d, name) {
      try {
        if (!d) return "";
        let v = d[name];
        if (typeof v === "function") v = v.call(d);
        if (typeof v === "string") return v.length > 400 ? v.slice(0, 400) + "…" : v;
        if (typeof v === "number") return String(v);
      } catch (e) { }
      return "";
    }

    function describeToast(config, n) {
      const d = n && n.data;
      const out = {
        type: "toast",
        kind: config && typeof config.strTest === "string" ? config.strTest : "",
        eType: n && typeof n.eType === "number" ? n.eType : -1,
        title: field(d, "title"), body: field(d, "body"), icon: field(d, "icon"),
        steamid: field(d, "steamid"), url: field(d, "response_steamurl"),
        extra: ""
      };
      try { if (d && d.item && typeof d.item.body_data === "string") out.extra = d.item.body_data.slice(0, 400); } catch (e) { }
      return out;
    }

    let hooked = false;
    function hook() {
      const store = window.NotificationStore;
      if (!store || typeof store.BShowToast !== "function") return false;
      const orig = store.BShowToast;
      const wrapped = function (config, notification) {
        const show = orig.apply(this, arguments);
        // Forward only what Steam itself is about to show, so the island follows the user's Steam settings.
        try { if (show) send(describeToast(config, notification)); } catch (e) { }
        return show;
      };
      store.BShowToast = wrapped;
      cleanup.push(function () { try { if (store.BShowToast === wrapped) delete store.BShowToast; } catch (e) { } });
      return true;
    }

    function stop() {
      stopped = true;
      if (timer) clearTimeout(timer);
      cleanup.splice(0).forEach(function (f) { try { f(); } catch (e) { } });
      try { if (ws) ws.close(); } catch (e) { }
    }

    exports.default = async function () {
      hooked = hook();
      connect();
      return { icon: null, onDismount: stop };
    };
    Object.defineProperty(exports, "__esModule", { value: !0 });
    return exports;
  }({}, window.MILLENNIUM_API, window.SP_REACT);
};
(async () => {
  const e = PluginEntryPointMain();
  Object.assign(window.PLUGIN_LIST[pluginName], { ...e, __millennium_internal_plugin_name_do_not_use_or_change__: pluginName });
  const t = await e.default();
  t && void 0 !== t.title && void 0 !== t.icon && void 0 !== t.content && (window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS[pluginName] = t),
    MILLENNIUM_BACKEND_IPC.postMessage(1, { pluginName: pluginName });
})();
