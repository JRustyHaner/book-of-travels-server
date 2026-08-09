using System;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Unity.Mono;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Microsoft.IdentityModel.Tokens;
using UnityEngine;

namespace BotMasterPlugin;

/// <summary>
/// BotMaster plugin — points the Book of Travels client at a private master
/// server (role=client), or turns a headless copy into a dedicated game
/// server instance (role=instance).
///
/// Why this exists (from RE of the shipped build):
///  - remote mode is neutered: Database.GetConnection returns null,
///    _tokenValidationParameters is never initialized, master host defaults to
///    localhost, and nothing resolves the game-server address from the master.
///    This plugin repairs all of that.
///  - the instance registers with the master via the MDY_INSTANCE_SERVER ping loop.
/// </summary>
[BepInPlugin("dev.botmaster.plugin", "BotMaster Plugin", "1.0.0")]
// BepInEx 6 compares against Paths.ProcessName = Path.GetFileNameWithoutExtension(exePath).
// "BookOfTravels.x86_64" -> extension ".x86_64" -> "BookOfTravels". Neither the full name
// nor the Linux-truncated comm ("BookOfTravels.x") ever matches, so this must be "BookOfTravels".
[BepInProcess("BookOfTravels")]
public class BotMasterPlugin : BaseUnityPlugin
{
    internal static ManualLogSource Log = null!;
    internal static BotConfig Cfg = null!;

    private void Awake()
    {
        Log = Logger;
        Cfg = new BotConfig(Config);
        Logger.LogInfo($"BotMaster plugin loaded. role={Cfg.Role}, master={Cfg.MasterHost}:{Cfg.MasterPort}");

        // Headless instance: it never renders, so load scene textures at the
        // smallest mip (1/256 memory) to cut the world's RAM footprint.
        // Clients are unaffected (each renders with its own full-res textures).
        QualitySettings.globalTextureMipmapLimit = 4;
        Logger.LogInfo($"globalTextureMipmapLimit set to 4 (headless memory saving)");

        try
        {
            Harmony.CreateAndPatchAll(typeof(Patches));
            Logger.LogInfo("Harmony patches applied (NetworkManagerMMO.Start, UILogin.Login, Database.GetConnection).");
        }
        catch (Exception e)
        {
            Logger.LogError($"Harmony patch failed: {e}");
        }
    }

    private void Update()
    {
        try
        {
            var nm = NetworkManagerMMO.Instance;
            if (nm == null) return;
            if (Cfg.Role == "instance") InstanceDriver.Tick(nm);
        }
        catch (Exception e)
        {
            Log.LogWarning($"tick: {e.Message}");
        }
    }
}

/// <summary>Config from cfg/BotMasterPlugin.cfg.</summary>
public class BotConfig
{
    public string Role { get; }       // "client" | "instance"
    public string MasterHost { get; }
    public int MasterPort { get; }

    public BotConfig(ConfigFile file)
    {
        // BepInEx stores/returns string values with surrounding quotes (e.g. `role = "client"`),
        // so trim them — otherwise "client" != "client" and masterHost becomes `"127.0.0.1"`
        // which getaddrinfo can't resolve ("DNS resolution failed").
        // (Kept as a comment for history; the trim itself moved into BotConfig.)
        // BepInEx stores/returns string values quoted AND re-saves them with escaped quotes
        // (`role = \"client\"`), so strip both quote and backslash characters from the ends.
        Role = file.Bind("general", "role", "client", "client or instance").Value.Trim('"', '\\');
        MasterHost = file.Bind("general", "masterHost", "127.0.0.1", "private master server host").Value.Trim('"', '\\');
        MasterPort = file.Bind("general", "masterPort", 1234, "private master server gRPC port").Value;
        // Optional "email,password" — auto-fills the login form (the game's keyboard input
        // is unreliable in windowed/XWayland, and remote mode starts with empty fields).
        AutoLogin = file.Bind("general", "autoLogin", "", "optional email,password to pre-fill the login form").Value.Trim('"', '\\');
    }

    public string AutoLogin { get; }
}

/// <summary>All Harmony patches.</summary>
[HarmonyPatch]
public static class Patches
{
    // ---- NetworkManagerMMO.Start: configure remote mode BEFORE the master channel is created ----
    [HarmonyPatch(typeof(NetworkManagerMMO), "Start")]
    [HarmonyPrefix]
    private static void OnStartPrefix(NetworkManagerMMO __instance)
    {
        var tools = PersistentTools.Instance;
        if (tools != null) tools.UseRemoteServer = true;

        var t = typeof(NetworkManagerMMO);
        SetField(t, __instance, "_masterServerHostName", BotMasterPlugin.Cfg.MasterHost);
        SetField(t, __instance, "masterServerHostNameLive", BotMasterPlugin.Cfg.MasterHost);
        SetField(t, __instance, "masterServerHostNameTest", BotMasterPlugin.Cfg.MasterHost);
        SetField(t, __instance, "_masterServerPort", BotMasterPlugin.Cfg.MasterPort);
        SetField(t, __instance, "_useSSL", false);

        // Grpc.Core's ares resolver fails on bare IP literals ("DNS resolution failed"),
        // which breaks the LobbyUI master calls and then aborts the process. Force the
        // native resolver; also set the game's DNSResolver setting (1 = native) so the
        // original Start() body doesn't overwrite the env var back to "ares".
        Environment.SetEnvironmentVariable("GRPC_DNS_RESOLVER", "native");
        try
        {
            var settings = LocalSaveManager.Instance?.SettingsData;
            if (settings != null && settings.ContainsKey(Setting.DNSResolver))
                settings[Setting.DNSResolver] = new SettingData(1, "");
        }
        catch (Exception e)
        {
            BotMasterPlugin.Log.LogWarning($"DNSResolver override failed: {e.Message}");
        }

        // Shipped build never creates this; PingPong only sets IssuerSigningKey on it.
        var p = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "MasterServer",
            ValidateAudience = true,
            ValidAudience = "ServerInstance",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(60),
            IssuerSigningKey = new SymmetricSecurityKey(new byte[48]) // replaced by Instance.Ping pong
        };
        SetField(t, __instance, "_tokenValidationParameters", p);

        BotMasterPlugin.Log.LogInfo($"Configured remote mode: master={BotMasterPlugin.Cfg.MasterHost}:{BotMasterPlugin.Cfg.MasterPort}, useSSL=false");
    }

    [HarmonyPatch(typeof(UILogin), "Start")]
    [HarmonyPostfix]
    private static void OnUILoginStartPostfix(UILogin __instance)
    {
        try
        {
            var parts = BotMasterPlugin.Cfg.AutoLogin;
            if (string.IsNullOrEmpty(parts)) return;
            var idx = parts.IndexOf(',');
            if (idx < 0) idx = parts.IndexOf(';');
            var email = idx < 0 ? parts.Trim() : parts.Substring(0, idx).Trim();
            var pass = idx < 0 ? "" : parts.Substring(idx + 1).Trim();
            if (__instance.accountInput != null) __instance.accountInput.text = email;
            if (__instance.passwordInput != null) __instance.passwordInput.text = pass;
            if (__instance.manager != null)
            {
                __instance.manager.loginAccount = email;
                __instance.manager.loginPassword = pass;
            }
            BotMasterPlugin.Log.LogInfo($"autoLogin: filled credentials for {email}");
        }
        catch (Exception e)
        {
            BotMasterPlugin.Log.LogWarning($"autoLogin failed: {e.Message}");
        }
    }

    [HarmonyPatch(typeof(NetworkManagerMMO), "Start")]
    [HarmonyPostfix]
    private static void OnStartPostfix(NetworkManagerMMO __instance)
    {
        try
        {
            var t = typeof(NetworkManagerMMO);
            var f = t.GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var ch = f?.GetValue(__instance) as Grpc.Core.Channel;
            var host = t.GetField("_masterServerHostName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(__instance);
            var port = t.GetField("_masterServerPort", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(__instance);
            BotMasterPlugin.Log.LogInfo($"POST-START: _masterServerHostName={host} _masterServerPort={port} channelTarget={(ch != null ? ch.Target : "null")} channelState={(ch != null ? ch.State.ToString() : "n/a")}");
        }
        catch (Exception e)
        {
            BotMasterPlugin.Log.LogWarning($"OnStartPostfix: {e.Message}");
        }
    }

    // ---- UILogin.Login: resolve the game-server address from the master first ----
    [HarmonyPatch(typeof(UILogin), "Login")]
    [HarmonyPrefix]
    private static void OnLoginPrefix()
    {
        var nm = NetworkManagerMMO.Instance;
        if (nm == null) return;
        try
        {
            // Run on a threadpool thread: GetRandomServer() is async and its continuation
            // is posted to Unity's main-thread sync context — calling .GetResult() directly
            // on the main thread deadlocks (the continuation can never run). Task.Run keeps
            // the blocking-call semantics the prefix needs without the deadlock.
            var addr = Task.Run(() => nm.GetRandomServer()).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(addr))
            {
                nm.networkAddress = addr;
                BotMasterPlugin.Log.LogInfo($"Resolved game server from master: {addr}");
            }
        }
        catch (Exception e)
        {
            BotMasterPlugin.Log.LogWarning($"GetRandomServer failed: {e.Message}");
        }
    }

    // ---- Database.GetConnection: repair the stubbed remote-DB path ----
    [HarmonyPatch(typeof(Database), "GetConnection")]
    [HarmonyPrefix]
    private static bool OnGetConnection(string serverUrl, ref MySql.Data.MySqlClient.MySqlConnection __result)
    {
        __result = new MySql.Data.MySqlClient.MySqlConnection(serverUrl);
        BotMasterPlugin.Log.LogInfo("Database.GetConnection patched (remote MariaDB restored).");
        return false;
    }

    // ---- Instance registration: the shipped StartInstanceConnection parses the
    // "host:port" wrong (Substring includes the colon, so int.TryParse fails and it
    // pings port 7689 — the game's own telepathy port — never the master). Reimplement
    // the ping loop against the configured master so the room registers. ----
    [HarmonyPatch(typeof(NetworkManagerMMO), "StartInstanceConnection")]
    [HarmonyPrefix]
    private static bool OnStartInstanceConnection(NetworkManagerMMO __instance, string hostString)
    {
        try
        {
            string host = BotMasterPlugin.Cfg.MasterHost;
            int port = BotMasterPlugin.Cfg.MasterPort;
            int colon = hostString.IndexOf(':');
            if (colon > 0) host = hostString.Substring(0, colon);
            if (colon >= 0 && int.TryParse(hostString.Substring(colon + 1), out int parsed)) port = parsed;
            var channel = new Grpc.Core.Channel(host, port, Grpc.Core.ChannelCredentials.Insecure);
            var client = new MasterService.Instance.InstanceClient(channel);
            SetField(typeof(NetworkManagerMMO), __instance, "instanceClient", client);
            __instance.StartCoroutine(InstancePingLoop(__instance, client));
            BotMasterPlugin.Log.LogInfo($"Instance ping loop started -> {host}:{port}");
            return false;
        }
        catch (Exception e)
        {
            BotMasterPlugin.Log.LogWarning($"StartInstanceConnection override failed: {e.Message}");
            return true; // fall back to the (broken) original
        }
    }

    private static System.Collections.IEnumerator InstancePingLoop(NetworkManagerMMO nm, MasterService.Instance.InstanceClient client)
    {
        int delayMs = 5000;
        while (nm != null)
        {
            try
            {
                var reply = client.Ping(new MasterService.InstancePingRequest
                {
                    RoomPort = nm.networkPort,
                    PlayerCount = nm.numPlayers
                }, deadline: DateTime.UtcNow.AddSeconds(5));
                delayMs = reply.NextPingMs;
                // Hand the JWT signing key to OnServerLogin (the shipped PingPong did this;
                // without it the instance validates client tokens against the placeholder key).
                var tvp = GetField(typeof(NetworkManagerMMO), nm, "_tokenValidationParameters") as TokenValidationParameters;
                if (tvp != null && reply.SecurityKey.Length > 0)
                {
                    tvp.IssuerSigningKey = new SymmetricSecurityKey(reply.SecurityKey.ToByteArray());
                    BotMasterPlugin.Log.LogInfo($"JWT signing key installed ({reply.SecurityKey.Length} bytes)");
                }
                if (NetworkSyncManager.Instance != null)
                {
                    NetworkSyncManager.Instance.TimeOffset = reply.TimeZoneOffset;
                }
                BotMasterPlugin.Log.LogInfo($"Instance.Ping OK: status={reply.Status} max={reply.MaxPlayerCount}");
                if (reply.Status == MasterService.InstanceStatus.Shutdown)
                {
                    Application.Quit();
                    yield break;
                }
            }
            catch (Exception e)
            {
                BotMasterPlugin.Log.LogWarning($"Instance.Ping failed: {e.Message}");
                delayMs = 5000;
            }
            yield return new WaitForSeconds(delayMs / 1000f);
        }
    }

    private static void SetField(Type t, object obj, string name, object value)
    {
        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) { BotMasterPlugin.Log.LogWarning($"field not found: {name}"); return; }
        f.SetValue(obj, value);
    }

    private static object? GetField(Type t, object obj, string name)
    {
        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) { BotMasterPlugin.Log.LogWarning($"field not found: {name}"); return null; }
        return f.GetValue(obj);
    }
}

/// <summary>Dedicated instance: start the Mirror server once the world is ready.</summary>
public static class InstanceDriver
{
    private static bool _started;

    public static void Tick(NetworkManagerMMO nm)
    {
        if (_started) return;
        if (Time.timeSinceLevelLoad < 1f) return; // let Start() and scene load settle

        BotMasterPlugin.Log.LogInfo("InstanceDriver: starting server...");
        nm.SetNetworkPort();
        nm.StartServer();
        _started = true;
        BotMasterPlugin.Log.LogInfo("InstanceDriver: StartServer() called.");
    }
}
