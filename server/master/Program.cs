using BotMaster;
using BotMaster.Services;
using Grpc.Core;
using Grpc.Net.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Information);

// The game client connects with Grpc.Core over plaintext HTTP/2 ("insecure channel").
// No TLS: the client's GetMasterServerCredentials() falls back to insecure when
// _useSSL is false, which our client mod will enforce.
var port = int.Parse(Environment.GetEnvironmentVariable("BOT_MASTER_PORT") ?? "1234");
var host = Environment.GetEnvironmentVariable("BOT_MASTER_HOST") ?? "0.0.0.0";

builder.WebHost.ConfigureKestrel(o =>
{
    o.Listen(System.Net.IPAddress.Parse(host), port, lo => lo.Protocols =
        Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
    // The game's StartInstanceConnection() parses MDY_INSTANCE_SERVER's port by
    // int.TryParse of the substring AFTER the last ':' — which includes the colon,
    // so it ALWAYS fails and falls back to the hardcoded 7689. Listen there too so
    // the instance's Instance.Ping actually reaches us.
    o.Listen(System.Net.IPAddress.Parse(host), 7689, lo => lo.Protocols =
        Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
var store = new Store(Path.Combine(dataDir, "master.db"));
var jwt = new JwtTokens(Path.Combine(dataDir, "jwt.key"));

builder.Services.AddSingleton(store);
builder.Services.AddSingleton(jwt);
builder.Services.AddGrpc(o =>
{
    o.MaxReceiveMessageSize = 64 * 1024 * 1024; // feedback emails carry images/logs
});

var app = builder.Build();
app.MapGrpcService<GameSvc>();
app.MapGrpcService<InstanceSvc>();
app.MapGrpcService<MasterSvc>();
app.MapGrpcService<AdminSvc>();

Console.WriteLine($"[BotMaster] listening on {host}:{port} (gRPC, h2c)");
Console.WriteLine($"[BotMaster] jwt key: {Convert.ToBase64String(jwt.Key)[..12]}... ({jwt.Key.Length} bytes)");
app.Run();
