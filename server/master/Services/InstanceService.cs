using BotMaster;
using Grpc.Core;
using MasterService;

namespace BotMaster.Services;

/// <summary>
/// The game-server-facing service (MasterService.Instance). Headless game
/// instances ping this every few seconds; the master records the room
/// (peer IP + room_port) and hands back the JWT signing key the instance
/// needs to validate player login tokens.
/// The advertised host can be overridden with BOT_PUBLIC_HOST (see Store.PublicHost).
/// </summary>
public class InstanceSvc : MasterService.Instance.InstanceBase
{
    private readonly Store _store;
    private readonly JwtTokens _jwt;
    private readonly ILogger<InstanceSvc> _log;

    public InstanceSvc(Store store, JwtTokens jwt, ILogger<InstanceSvc> log)
    {
        _store = store;
        _jwt = jwt;
        _log = log;
    }

    public override Task<InstancePongReply> Ping(InstancePingRequest request, ServerCallContext context)
    {
        var ip = context.GetHttpContext().Connection.RemoteIpAddress?.ToString() ?? "?";
        var room = _store.RegisterRoom(ip, request.RoomPort, request.PlayerCount);
        _log.LogInformation("instance ping: {Ip}:{Port} players={Players} -> {Rooms} room(s) registered",
            ip, request.RoomPort, request.PlayerCount, _store.GetRooms().Count);
        return Task.FromResult(new InstancePongReply
        {
            NextPingMs = 5000,
            Status = InstanceStatus.Continue,
            MaxPlayerCount = 16,
            SecurityKey = Google.Protobuf.ByteString.CopyFrom(_jwt.Key),
            TimeZoneOffset = 0
        });
    }
}
