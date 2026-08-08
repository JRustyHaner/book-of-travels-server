using BotMaster;
using Grpc.Core;
using MasterService;

namespace BotMaster.Services;

/// <summary>
/// MasterService.Master — the legacy/orchestration service. The shipped client
/// never calls it (verified in IL), but it's part of the wire contract so we
/// implement it faithfully for tooling compatibility.
/// </summary>
public class MasterSvc : MasterService.Master.MasterBase
{
    private readonly Store _store;
    private readonly JwtTokens _jwt;

    public MasterSvc(Store store, JwtTokens jwt)
    {
        _store = store;
        _jwt = jwt;
    }

    public override Task<PongReply> Ping(PingRequest request, ServerCallContext context)
        => Task.FromResult(new PongReply
        {
            UpdateTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            NextPingMs = 5000
        });

    public override Task<ConfigReply> GetConfig(ConfigRequest request, ServerCallContext context)
        => Task.FromResult(new ConfigReply
        {
            ServerId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Tags = 0,
            DbUrl = "",
            SecurityKey = Google.Protobuf.ByteString.CopyFrom(_jwt.Key)
        });

    public override Task<RoomReply> SetRooms(RoomRequest request, ServerCallContext context)
    {
        var ip = context.GetHttpContext().Connection.RemoteIpAddress?.ToString() ?? "?";
        foreach (var room in request.Rooms)
            _store.RegisterRoom(ip, room.Port, room.PlayerCount);
        return Task.FromResult(new RoomReply());
    }
}
