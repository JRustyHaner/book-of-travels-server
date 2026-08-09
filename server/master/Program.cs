using BotMaster;
using BotMaster.Services;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Information);

// The game client connects with Grpc.Core over plaintext HTTP/2 ("insecure channel").
// No TLS: the client's GetMasterServerCredentials() falls back to insecure when
// _useSSL is false, which our client mod will enforce.
var port = int.Parse(Environment.GetEnvironmentVariable("BOT_MASTER_PORT") ?? "1234");
var host = Environment.GetEnvironmentVariable("BOT_MASTER_HOST") ?? "0.0.0.0";
var httpPort = int.Parse(Environment.GetEnvironmentVariable("BOT_HTTP_PORT") ?? "8080"); // REST: /register, /admin/invite
var adminToken = Environment.GetEnvironmentVariable("BRAID_ADMIN_TOKEN")
                 ?? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

builder.WebHost.ConfigureKestrel(o =>
{
    o.Listen(System.Net.IPAddress.Parse(host), port, lo => lo.Protocols =
        Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
    // REST for the site (browser can't do gRPC): /register, /admin/invite
    o.Listen(System.Net.IPAddress.Parse(host), httpPort, lo => lo.Protocols =
        Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
    // The game's StartInstanceConnection() parses MDY_INSTANCE_SERVER's port by
    // int.TryParse of the substring AFTER the last ':' — which includes the colon,
    // so it ALWAYS fails and falls back to the hardcoded 7689. Listen there too so
    // the instance's Instance.Ping actually reaches us (overridable for tests /
    // when another process owns 7689).
    var extraPort = int.Parse(Environment.GetEnvironmentVariable("BOT_EXTRA_PORT") ?? "7689");
    o.Listen(System.Net.IPAddress.Parse(host), extraPort, lo => lo.Protocols =
        Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

// Per-IP fixed-window limiter for auth endpoints (public internet).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
            }));
});

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
var store = new Store(Path.Combine(dataDir, "master.db"));
store.PublicHost = Environment.GetEnvironmentVariable("BOT_PUBLIC_HOST"); // advertise a DNS name instead of the instance's (container) IP
var jwt = new JwtTokens(Path.Combine(dataDir, "jwt.key"));

builder.Services.AddSingleton(store);
builder.Services.AddSingleton(jwt);
builder.Services.AddGrpc(o =>
{
    o.MaxReceiveMessageSize = 64 * 1024 * 1024; // feedback emails carry images/logs
});

var app = builder.Build();
app.UseRateLimiter();
app.MapGrpcService<GameSvc>().RequireRateLimiting("auth");
app.MapGrpcService<InstanceSvc>();
app.MapGrpcService<MasterSvc>();
app.MapGrpcService<AdminSvc>();

// --- REST: invite-gated registration (used by the braid site) ---
app.MapPost("/register", async (HttpContext ctx) =>
{
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
    var root = doc.RootElement;
    var email = root.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
    var password = root.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";
    var invite = root.TryGetProperty("invite", out var i) ? i.GetString() ?? "" : "";
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(invite))
        return Results.BadRequest(new { error = "email, password and invite are required." });
    if (password.Length < 8)
        return Results.BadRequest(new { error = "Password must be at least 8 characters." });
    var (ok, err) = store.TryConsumeInvite(invite, email);
    if (!ok) return Results.BadRequest(new { error = err });
    var createErr = store.CreateAccount(email, password, 0);
    if (createErr != "") return Results.BadRequest(new { error = createErr });
    _ = LogInfo(ctx, $"registered {email} via invite");
    return Results.Ok(new { ok = true, msg = "Account created. Log in in-game with that email and password." });
}).RequireRateLimiting("auth");

// --- REST: login (existing account -> JWT) for the site's frontend ---
app.MapPost("/login", async (HttpContext ctx) =>
{
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
    var root = doc.RootElement;
    var email = root.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
    var password = root.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";
    var (id, banned) = store.Authenticate(email, password, 0);
    if (id == 0 || banned)
        return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);
    _ = LogInfo(ctx, $"frontend login {email} -> account {id}");
    return Results.Ok(new { token = jwt.Sign(id) });
}).RequireRateLimiting("auth");

// --- REST: any authenticated user may mint an invite code ---
app.MapPost("/invite", async (HttpContext ctx) =>
{
    var auth = ctx.Request.Headers.Authorization.ToString();
    var token = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth["Bearer ".Length..] : "";
    var uid = jwt.Validate(token);
    if (uid == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);
    string? email = null; bool reusable = false;
    if (ctx.Request.ContentLength is > 0)
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
        var root = doc.RootElement;
        if (root.TryGetProperty("email", out var e)) email = e.GetString();
        if (root.TryGetProperty("reusable", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.True) reusable = true;
    }
    var code = store.CreateInvite(email, reusable);
    _ = LogInfo(ctx, $"account {uid} created invite {code}");
    return Results.Ok(new { code });
}).RequireRateLimiting("auth");

// --- REST: admin invite-code generation (Bearer BRAID_ADMIN_TOKEN) ---
app.MapPost("/admin/invite", async (HttpContext ctx) =>
{
    var auth = ctx.Request.Headers.Authorization.ToString();
    if (!auth.Equals($"Bearer {adminToken}", StringComparison.Ordinal))
        return Results.Json(new { error = "unauthorized" }, statusCode: 401);
    string? email = null; bool reusable = false;
    if (ctx.Request.ContentLength is > 0)
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
        var root = doc.RootElement;
        if (root.TryGetProperty("email", out var e)) email = e.GetString();
        if (root.TryGetProperty("reusable", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.True) reusable = true;
    }
    var code = store.CreateInvite(email, reusable);
    _ = LogInfo(ctx, $"created invite {code} (email={(email ?? "any")}, reusable={reusable})");
    return Results.Ok(new { code });
}).RequireRateLimiting("auth");

static bool LogInfo(HttpContext ctx, string msg)
{
    ctx.RequestServices.GetRequiredService<ILogger<Program>>().LogInformation(msg);
    return true;
}

Console.WriteLine($"[BotMaster] listening on {host}:{port} (gRPC, h2c) + {host}:{httpPort} (REST)");
Console.WriteLine($"[BotMaster] jwt key: {Convert.ToBase64String(jwt.Key)[..12]}... ({jwt.Key.Length} bytes)");
Console.WriteLine($"[BotMaster] admin token: {adminToken}");
app.Run();
