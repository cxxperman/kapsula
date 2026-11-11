// Kapsula plugin for Millennium (probe version).
// Runs inside the Steam client and forwards its notification events to Kapsula on this PC
// over ws://127.0.0.1:42071. Nothing is sent to the internet. Written by hand in the same
// wrapper format Millennium's bundler produces, so it needs no build step.
const MILLENNIUM_IS_CLIENT_MODULE = !0, pluginName = "kapsula";
(window.PLUGIN_LIST ||= {})[pluginName] ||= {}, window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS ||= {};
let PluginEntryPointMain = function () {
  return function (exports, millenniumApi, react) {
    "use strict";
    const PORT = 42071;
    let ws = null, stopped = false, timer = null;
    const queue = [], cleanup = [];

    function send(obj) {
      let s;
      try { s = JSON.stringify(obj); } catch (e) { return; }
      if (ws && ws.readyState === 1) ws.send(s);
      else if (queue.length < 200) queue.push(s);
    }

    function connect() {
      if (stopped) return;
      timer = null;
      try {
        const sock = new WebSocket("ws://127.0.0.1:" + PORT + "/?client=millennium");
        sock.onopen = function () {
          ws = sock;
          sock.send(JSON.stringify({ type: "hello", plugin: "0.1.0" }));
          queue.splice(0).forEach(function (s) { sock.send(s); });
        };
        sock.onclose = function () { if (ws === sock) ws = null; if (!stopped && !timer) timer = setTimeout(connect, 5000); };
        sock.onerror = function () { };
      } catch (e) {
        if (!timer) timer = setTimeout(connect, 5000);
      }
    }

    // Turns whatever Steam hands us into plain JSON: short strings, limited depth, bytes as base64.
    function safe(v, depth) {
      depth = depth || 0;
      if (v === null || v === undefined) return null;
      const t = typeof v;
      if (t === "string") return v.length > 300 ? v.slice(0, 300) + "…" : v;
      if (t === "number" || t === "boolean") return v;
      if (t === "bigint") return v.toString();
      if (t === "function") return "[function]";
      if (v instanceof ArrayBuffer) v = new Uint8Array(v);
      if (ArrayBuffer.isView(v)) {
        const bytes = new Uint8Array(v.buffer, v.byteOffset, Math.min(v.byteLength, 4096));
        let bin = "";
        for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
        return { bytes: v.byteLength, base64: btoa(bin) };
      }
      if (depth >= 4) return "[deep]";
      if (Array.isArray(v)) return v.slice(0, 40).map(function (x) { return safe(x, depth + 1); });
      const out = {};
      let n = 0;
      for (const k in v) {
        if (n++ >= 40) break;
        try { out[k] = safe(v[k], depth + 1); } catch (e) { out[k] = "[error]"; }
      }
      return out;
    }

    function keys(o) { try { return o ? Object.keys(o) : null; } catch (e) { return null; } }

    function listen(name, register) {
      try {
        const handle = register(function () {
          send({ type: "event", name: name, args: safe(Array.prototype.slice.call(arguments)) });
        });
        if (handle && typeof handle.unregister === "function") cleanup.push(function () { handle.unregister(); });
        send({ type: "registered", name: name });
      } catch (e) {
        send({ type: "error", name: name, message: String(e) });
      }
    }

    function stop() {
      stopped = true;
      if (timer) clearTimeout(timer);
      cleanup.splice(0).forEach(function (f) { try { f(); } catch (e) { } });
      try { if (ws) ws.close(); } catch (e) { }
    }

    exports.default = async function () {
      connect();
      const sc = window.SteamClient;
      send({
        type: "probe",
        where: { title: document.title, href: String(location.href).split("?")[0] },
        steamClient: keys(sc),
        notifications: sc ? keys(sc.Notifications) : null,
        friends: sc ? keys(sc.Friends) : null,
        gameSessions: sc ? keys(sc.GameSessions) : null,
        messaging: sc ? keys(sc.Messaging) : null,
        webChat: sc ? keys(sc.WebChat) : null,
        globals: Object.keys(window).filter(function (k) { return /store|friend|notif|chat|toast/i.test(k); }).slice(0, 120)
      });
      if (sc) {
        if (sc.Notifications && sc.Notifications.RegisterForNotifications)
          listen("Notifications", function (cb) { return sc.Notifications.RegisterForNotifications(cb); });
        if (sc.GameSessions) {
          if (sc.GameSessions.RegisterForAchievementNotification)
            listen("Achievement", function (cb) { return sc.GameSessions.RegisterForAchievementNotification(cb); });
          if (sc.GameSessions.RegisterForAppLifetimeNotifications)
            listen("AppLifetime", function (cb) { return sc.GameSessions.RegisterForAppLifetimeNotifications(cb); });
          if (sc.GameSessions.RegisterForScreenshotNotification)
            listen("Screenshot", function (cb) { return sc.GameSessions.RegisterForScreenshotNotification(cb); });
        }
      }
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
