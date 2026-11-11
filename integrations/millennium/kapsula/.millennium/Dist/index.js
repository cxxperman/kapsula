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
    const PORT = 42071, VERSION = "0.4.0";
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
      debug = true;   // check mode: also report field names of what Steam passes, until the plugin is reloaded
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

    // Names of an object's fields and methods (no values) — sent only in check mode, to learn Steam's data shapes.
    function shape(o) {
      if (!o) return null;
      const own = [], methods = [];
      try { for (const k of Object.getOwnPropertyNames(o)) if (own.length < 60) own.push(k + ":" + (o[k] === null ? "null" : typeof o[k])); } catch (e) { }
      try {
        let p = Object.getPrototypeOf(o), level = 0;
        while (p && p !== Object.prototype && level++ < 3)
          for (const k of Object.getOwnPropertyNames(p)) if (k !== "constructor" && methods.length < 120 && methods.indexOf(k) < 0) methods.push(k);
      } catch (e) { }
      return { own: own, methods: methods };
    }

    // Simple values of a notification: results of its argument-less getters (title, steamid, appid…).
    function simpleFields(d) {
      const out = {};
      let n = 0;
      try {
        for (const k in d) {
          if (n >= 25) break;
          let v = d[k];
          if (typeof v === "function") {
            if (v.length !== 0 || /^(set_|clone|to[A-Z]|get[A-Z]|sync|serialize|tag$)/.test(k)) continue;
            try { v = v.call(d); } catch (e) { continue; }
          }
          if (typeof v === "string") { out[k] = v.slice(0, 200); n++; }
          else if (typeof v === "number" || typeof v === "boolean") { out[k] = v; n++; }
        }
      } catch (e) { }
      return out;
    }

    // Looks a friend up in Steam's own friends list by SteamID: name, avatar and the game they are in.
    function friend(steamid) {
      const out = { name: "", game: "", avatar: "" };
      try {
        if (!steamid) return out;
        const account = Number(BigInt(steamid) & 0xFFFFFFFFn);
        const store = window.g_FriendsUIApp && window.g_FriendsUIApp.FriendStore;
        const p = store && typeof store.GetPlayer === "function" ? store.GetPlayer(account) : null;
        if (!p) { if (debug) out.storeShape = shape(store); return out; }
        const per = p.persona;
        out.name = String(p.display_name || (per && per.m_strPlayerName) || "");
        if (per) {
          let game = "";
          try { if (typeof per.GetCurrentGameName === "function") game = per.GetCurrentGameName() || ""; } catch (e) { }
          out.game = String(game || per.m_strGameExtraInfo || "");
          out.avatar = String(per.avatar_url_medium || "");
        }
        if (debug) { out.playerShape = shape(p); out.personaShape = shape(per); }
      } catch (e) { out.error = String(e); }
      return out;
    }

    let debug = false;

    function describeToast(config, n) {
      const d = n && n.data;
      const out = {
        type: "toast",
        kind: config && typeof config.strTest === "string" ? config.strTest : "",
        eType: n && typeof n.eType === "number" ? n.eType : -1,
        title: field(d, "title"), body: field(d, "body"), icon: field(d, "icon"),
        steamid: field(d, "steamid"), url: field(d, "response_steamurl"),
        game: "", extra: ""
      };
      try { if (d && d.item && typeof d.item.body_data === "string") out.extra = d.item.body_data.slice(0, 400); } catch (e) { }
      // "Friend online / in game" notifications carry only a SteamID — fill in the rest from the friends list.
      if (out.steamid && (!out.title || !out.icon)) {
        const f = friend(out.steamid);
        if (!out.title) out.title = f.name;
        if (!out.icon) out.icon = f.avatar;
        out.game = f.game;
        if (debug) out.friend = f;
      }
      if (debug) { out.fields = simpleFields(d); out.dataShape = shape(d); }
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
        try {
          if (show) send(describeToast(config, notification));
          else if (debug) { const t = describeToast(config, notification); t.type = "suppressed"; send(t); }
        } catch (e) { }
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
