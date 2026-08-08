using Version = MasterService.Version;
using BotMaster;
using Grpc.Core;
using MasterService;

namespace BotMaster.Services;

/// <summary>
/// MasterService.Admin — the fleet-orchestration API (builds, auto-scaling,
/// regions, master clients). The game client never calls it; we implement the
/// read paths against the live room registry and return success for the
/// management RPCs so external tooling can talk to it.
/// </summary>
public class AdminSvc : MasterService.Admin.AdminBase
{
    private readonly Store _store;
    private readonly ILogger<AdminSvc> _log;

    public AdminSvc(Store store, ILogger<AdminSvc> log)
    {
        _store = store;
        _log = log;
    }

    public override Task<ServerReply> GetAllServers(ListServersRequest request, ServerCallContext context)
    {
        var reply = new ServerReply();
        foreach (var room in _store.GetRooms())
        {
            reply.Servers.Add(new ServerDescription
            {
                ServerId = room.ServerId,
                PublicIp = room.Host,
                TotalPlayers = room.PlayerCount,
                IsActive = true,
                State = ServerState.Running,
                Name = "private",
                CurrentVersion = "1.0",
                TargetVersion = "1.0",
                Tags = 0
            });
        }
        return Task.FromResult(reply);
    }

    public override Task<RegionList> GetAvailableRegions(Empty request, ServerCallContext context)
        => Task.FromResult(RegionList());

    public override Task<RegionList> GetActiveRegions(Empty request, ServerCallContext context)
        => Task.FromResult(RegionList());

    public override Task<ErrorReply> AddActiveRegion(Region request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> UpdateActiveRegion(Region request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> DeleteActiveRegion(Region request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> CreateMasterClient(CreateMasterRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> RemoveMasterClient(StopMasterRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> UpdateMasterClient(UpdateMasterRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> StartMasterClient(StartMasterRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> StopMasterClient(StopMasterRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<GetAutoScalingReply> GetAutoScalingSettings(Empty request, ServerCallContext context)
        => Task.FromResult(new GetAutoScalingReply { Settings = new ScalingDescription { Enabled = false } });

    public override Task<ErrorReply> UpdateAutoScaling(UpdateAutoScalingRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<GetVersionsReply> GetGameVersions(Empty request, ServerCallContext context)
        => Task.FromResult(new GetVersionsReply { Versions = { new Version { Name = "1.0", Tag = "1.0" } } });

    public override Task<GetVersionsReply> GetServerVersions(Empty request, ServerCallContext context)
        => Task.FromResult(new GetVersionsReply { Versions = { new Version { Name = "1.0", Tag = "1.0" } } });

    public override Task<ErrorReply> SetDefaultGameVersion(Version request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> SetDefaultServerVersion(Version request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<Version> GetDefaultGameVersion(Empty request, ServerCallContext context)
        => Task.FromResult(new Version { Name = "1.0", Tag = "1.0" });

    public override Task<Version> GetDefaultServerVersion(Empty request, ServerCallContext context)
        => Task.FromResult(new Version { Name = "1.0", Tag = "1.0" });

    public override Task<ErrorReply> ApplyGameBuildAll(ApplyBuildAllRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> ApplyGameBuildSelected(ApplyBuildSelectedRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> ApplyServerBuildAll(ApplyBuildAllRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> ApplyServerBuildSelected(ApplyBuildSelectedRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    private static RegionList RegionList()
    {
        var l = new RegionList();
        l.Regions.Add(new Region { Name = "eu-central-1", DisplayName = "EU Central", Code = "eu-central-1" });
        return l;
    }
}
