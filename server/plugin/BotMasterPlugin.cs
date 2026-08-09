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
using UnityEngine.SceneManagement;

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
            Patches.StreamTick();
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
        // Instance memory saving: after the world loads, keep only the randomizer's
        // ACTIVE levels (plus the manager scenes) and unload the rest.
        LazyLevels = file.Bind("general", "lazyLevels", true, "instance: keep only active-set levels loaded (Tier 2a)").Value;
        // Tier 2b: per-player level streaming — unload world levels with no
        // players, load on entry. Modes: off | on (always) | pressure (only when
        // the host is low on RAM, so NPCs simulate normally when there's headroom).
        StreamMode = file.Bind("general", "streamLevels", "pressure", "off | on | pressure (per-player level streaming)").Value.Trim('"', ' ');
        StreamPressureMB = file.Bind("general", "streamPressureMB", 2048, "pressure mode: unload empty levels when MemAvailable drops below this (MB)").Value;
    }

    public bool LazyLevels { get; }
    public string StreamMode { get; }
    public int StreamPressureMB { get; }
    public string AutoLogin { get; }

    public bool StreamingEnabled => StreamMode switch
    {
        "on" or "true" or "1" => true,
        "pressure" => true,
        _ => false
    };

    public bool StreamingPressure => StreamMode == "pressure";
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

    // ---- Tier 2a: after the world loads, keep only the randomizer's ACTIVE levels.
    // The shipped OnStartServer loads ALL bundled levels (~111); the level
    // randomizer has already chosen the active arrangement
    // (SyncDictionaryConnectionRandom / island scenes), so unload everything else.
    // Runs before any player can connect, so it's race-free. No game code changes.
    [HarmonyPatch(typeof(NetworkManagerMMO), "OnStartServer")]
    [HarmonyPostfix]
    private static void OnStartServerPostfix(NetworkManagerMMO __instance)
    {
        if (BotMasterPlugin.Cfg.Role != "instance" || !BotMasterPlugin.Cfg.LazyLevels) return;
        try
        {
            var keep = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "Essentials", "Entry", "Entry_Audio", "Lobby"
            };

            // active randomized levels (the world graph the randomizer chose)
            var nsm = NetworkSyncManager.Instance;
            if (nsm != null)
            {
                foreach (var k in nsm.SyncDictionaryConnectionRandom.Keys) keep.Add(k);
            }

            // active island scenes (private field, best-effort)
            var lr = PersistentTools.Instance?.LevelRandomizer;
            if (lr != null)
            {
                var f = typeof(LevelRandomizer).GetField("activeIslandScenes", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f?.GetValue(lr) is System.Collections.IList list)
                {
                    foreach (var o in list)
                    {
                        var sn = o?.GetType().GetField("sceneName")?.GetValue(o) as string;
                        if (!string.IsNullOrEmpty(sn)) keep.Add(sn);
                    }
                }
            }

            // safety: if we couldn't determine any active world levels, leave everything
            // loaded (same behavior as before) rather than unloading the world.
            if (keep.Count <= 4)
            {
                var names = new System.Collections.Generic.List<string>();
                for (int i = 0; i < SceneManager.sceneCount; i++) names.Add(SceneManager.GetSceneAt(i).name);
                BotMasterPlugin.Log.LogInfo($"lazy: no active level set found (dict={nsm?.SyncDictionaryConnectionRandom.Count ?? -1}); loaded scenes [{names.Count}]: {string.Join(",", names)}");
                return;
            }

            int kept = 0, unloaded = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (string.IsNullOrEmpty(scene.name) || keep.Contains(scene.name)) { kept++; continue; }
                try
                {
                    var lvlMgr = UtilityManager.Instance?.RelevantLevelManager(scene.name);
                    lvlMgr?.DespawnAll();
                    UtilityManager.Instance?.ShouldAddLoadedLevel(scene.name, false);
                    SceneManager.UnloadSceneAsync(scene);
                    unloaded++;
                    BotMasterPlugin.Log.LogInfo($"lazy: unloaded {scene.name}");
                }
                catch (Exception e)
                {
                    BotMasterPlugin.Log.LogWarning($"lazy: failed to unload {scene.name}: {e.Message}");
                }
            }
            BotMasterPlugin.Log.LogInfo($"lazy: kept {kept} scene(s), unloaded {unloaded} (active-set only)");
        }
        catch (Exception e)
        {
            BotMasterPlugin.Log.LogWarning($"lazy OnStartServer: {e.Message}");
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

    // ---- Tier 2b: per-player level streaming (instance only, experimental) ----
    // World levels are unloaded when the last player leaves and loaded on entry.
    // The game's own interior rooms (_Room_ scenes) are left alone (it streams
    // those itself). Manager scenes are always kept.
    private static readonly System.Collections.Generic.HashSet<string> _alwaysKeep =
        new(System.StringComparer.OrdinalIgnoreCase) { "Lobby", "Essentials", "Entry", "Entry_Audio" };

    private static bool IsStreamableScene(string name) =>
        !string.IsNullOrEmpty(name)
        && !_alwaysKeep.Contains(name)
        && name.IndexOf("_Room_", System.StringComparison.OrdinalIgnoreCase) < 0
        && Application.CanStreamedLevelBeLoaded(name);

    // Ensure the destination is loaded BEFORE the player relocates into it
    // (UserCode_CmdRequestChangeLevel is the server-side handler of the level
    // change command; this prefix runs before RpcFadeAndRelocateData).
    [HarmonyPatch(typeof(PlayerBase), "UserCode_CmdRequestChangeLevel__String__String__Int32__Boolean__Vector3__Boolean__RandomLevelDirection")]
    [HarmonyPrefix]
    private static void OnRequestChangeLevel(string nextLevel)
    {
        if (BotMasterPlugin.Cfg.Role != "instance" || !BotMasterPlugin.Cfg.StreamingEnabled) return;
        if (!IsStreamableScene(nextLevel)) return;
        var s = SceneManager.GetSceneByName(nextLevel);
        if (s.IsValid() && s.isLoaded) return;
        SceneManager.LoadScene(nextLevel, LoadSceneMode.Additive);
        StreamSpawn(nextLevel);
        BotMasterPlugin.Log.LogInfo($"stream: preloaded {nextLevel} for arrival");
    }

    private static int MemAvailableMB()
    {
        try
        {
            foreach (var line in System.IO.File.ReadAllLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemAvailable:", System.StringComparison.Ordinal))
                {
                    var parts = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                    return int.TryParse(parts[1], out var kb) ? kb / 1024 : int.MaxValue;
                }
            }
        }
        catch { }
        return int.MaxValue; // unknown -> treat as NOT under pressure
    }

    private static void StreamSpawn(string level)
    {
        try
        {
            var lvlMgr = UtilityManager.Instance?.RelevantLevelManager(level);
            if (lvlMgr != null && !lvlMgr.HasAlreadySpawnedObjects)
            {
                UtilityManager.Instance.SpawnAllNetworkObjects(lvlMgr);
                BotMasterPlugin.Log.LogInfo($"stream: spawned network objects for {level}");
            }
        }
        catch (Exception e)
        {
            BotMasterPlugin.Log.LogWarning($"stream: spawn {level}: {e.Message}");
        }
    }

    private static float _nextStreamTick;
    private static bool _lastPressure;

    internal static void StreamTick()
    {
        if (BotMasterPlugin.Cfg.Role != "instance" || !BotMasterPlugin.Cfg.StreamingEnabled) return;
        if (Time.time < _nextStreamTick) return;
        _nextStreamTick = Time.time + 4f;
        try
        {
            var occupied = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (PlayerBase.onlinePlayers != null)
            {
                foreach (var kv in PlayerBase.onlinePlayers)
                {
                    var lvl = kv.Value?.CurrentLevel;
                    if (!string.IsNullOrEmpty(lvl)) occupied.Add(lvl);
                }
            }
            // don't start unloading until someone is actually in the world
            if (occupied.Count == 0) return;

            // pressure mode: only shed empty levels while the host is low on RAM,
            // so NPCs keep simulating normally whenever there's headroom.
            var mayUnload = true;
            if (BotMasterPlugin.Cfg.StreamingPressure)
            {
                var availMB = MemAvailableMB();
                mayUnload = availMB < BotMasterPlugin.Cfg.StreamPressureMB;
                if (mayUnload != _lastPressure)
                {
                    _lastPressure = mayUnload;
                    BotMasterPlugin.Log.LogInfo($"stream: pressure {(mayUnload ? "LOW" : "OK")} (MemAvailable {availMB} MB, threshold {BotMasterPlugin.Cfg.StreamPressureMB} MB)");
                }
            }

            // unload world levels with no players
            if (mayUnload)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    var name = scene.name;
                    if (!IsStreamableScene(name) || occupied.Contains(name)) continue;
                    try
                    {
                        var lvlMgr = UtilityManager.Instance?.RelevantLevelManager(name);
                        lvlMgr?.DespawnAll();
                        UtilityManager.Instance?.ShouldAddLoadedLevel(name, false);
                        SceneManager.UnloadSceneAsync(scene);
                        BotMasterPlugin.Log.LogInfo($"stream: unloaded {name} (0 players)");
                    }
                    catch (Exception e)
                    {
                        BotMasterPlugin.Log.LogWarning($"stream: unload {name}: {e.Message}");
                    }
                }
            }

            // load occupied levels that aren't loaded (synchronous, like the game's own loads)
            foreach (var name in occupied)
            {
                if (!IsStreamableScene(name)) continue;
                var s = SceneManager.GetSceneByName(name);
                if (s.IsValid() && s.isLoaded) continue;
                SceneManager.LoadScene(name, LoadSceneMode.Additive);
                StreamSpawn(name);
                BotMasterPlugin.Log.LogInfo($"stream: loaded {name} (occupied)");
            }
        }
        catch (Exception e)
        {
            BotMasterPlugin.Log.LogWarning($"stream tick: {e.Message}");
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
