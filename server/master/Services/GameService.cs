using BotMaster;
using Grpc.Core;
using MasterService;

namespace BotMaster.Services;

/// <summary>
/// The client-facing service (MasterService.Game). The client calls Authenticate
/// with email/password/SteamID and gets a JWT back, which it forwards to a game
/// server in LoginMsg. Everything else is account management + server discovery.
/// </summary>
public class GameSvc : MasterService.Game.GameBase
{
    private readonly Store _store;
    private readonly JwtTokens _jwt;
    private readonly ILogger<GameSvc> _log;

    public GameSvc(Store store, JwtTokens jwt, ILogger<GameSvc> log)
    {
        _store = store;
        _jwt = jwt;
        _log = log;
    }

    public override Task<AuthenticateReply> Authenticate(Credentials request, ServerCallContext context)
    {
        var (id, banned) = _store.Authenticate(request.Email, request.Password, request.ServiceId);
        if (id == 0)
            return Task.FromResult(new AuthenticateReply { AuthToken = "", Msg = "Incorrect email or password." });
        if (banned)
            return Task.FromResult(new AuthenticateReply { AuthToken = "", Msg = "This account has been banned." });
        _log.LogInformation("authenticated {Email} -> account {Id}", request.Email, id);
        return Task.FromResult(new AuthenticateReply { AuthToken = _jwt.Sign(id), Msg = "" });
    }

    public override Task<ErrorReply> RegisterAccount(RegisterAccountRequest request, ServerCallContext context)
    {
        // Invite-gated: the EmailToken.Token field carries the invite code (friends
        // get codes from the admin; the in-game registration UI enters it there).
        var email = request.EmailToken.Email.Trim();
        var (ok, err) = _store.TryConsumeInvite(request.EmailToken.Token, email);
        if (!ok) return Task.FromResult(new ErrorReply { Success = false, Msg = err });
        var createErr = _store.CreateAccount(email, request.Password, request.ServiceId);
        if (createErr != "") return Task.FromResult(new ErrorReply { Success = false, Msg = createErr });
        _log.LogInformation("registered {Email} via invite", email);
        return Task.FromResult(new ErrorReply { Success = true, Msg = "Account registered." });
    }

    public override Task<ErrorReply> SendEmailVerification(SendEmailVerificationRequest request, ServerCallContext context)
    {
        var token = _store.CreateEmailToken(request.Email, request.Name);
        _log.LogInformation("[no mail sent] verification token for {Email}: {Token}", request.Email, token);
        return Task.FromResult(new ErrorReply { Success = true, Msg = "" });
    }

    public override Task<ErrorReply> VerifyEmailToken(EmailToken request, ServerCallContext context)
    {
        return Task.FromResult(new ErrorReply
        {
            Success = _store.ConsumeEmailToken(request.Token, out _),
            Msg = ""
        });
    }

    public override Task<ErrorReply> ChangeAccountPassword(ChangeAccountPasswordRequest request, ServerCallContext context)
    {
        var ok = request.Verification.VerificationCase switch
        {
            Verification.VerificationOneofCase.Credentials => _store.ChangePassword(
                request.Verification.Credentials.Email, request.Verification.Credentials.Password, request.NewPassword),
            Verification.VerificationOneofCase.EmailToken => _store.ConsumeEmailToken(request.Verification.EmailToken.Token, out var email)
                                                              && _store.ChangePasswordDirect(email, request.NewPassword),
            _ => false
        };
        return Task.FromResult(new ErrorReply { Success = ok, Msg = ok ? "" : "Verification failed." });
    }

    public override Task<ErrorReply> ChangeAccountEmail(ChangeAccountEmailRequest request, ServerCallContext context)
    {
        var ok = request.Verification.VerificationCase switch
        {
            Verification.VerificationOneofCase.Credentials => _store.VerifyPassword(request.Verification.Credentials.Email, request.Verification.Credentials.Password) &&
                                                              _store.ChangeEmail(request.OldEmail, request.Verification.Credentials.Email),
            Verification.VerificationOneofCase.EmailToken => _store.ConsumeEmailToken(request.Verification.EmailToken.Token, out var email) &&
                                                              _store.ChangeEmail(request.OldEmail, email),
            _ => false
        };
        return Task.FromResult(new ErrorReply { Success = ok, Msg = ok ? "" : "Verification failed." });
    }

    public override Task<ErrorReply> RedeemAccountStatus(AccountUnlockRequest request, ServerCallContext context)
        => Task.FromResult(new ErrorReply { Success = true, Msg = "" });

    public override Task<ErrorReply> SendFeedbackEmail(FeedbackEmailRequest request, ServerCallContext context)
    {
        _log.LogInformation("feedback {Type}: {Headline} - {Report}", request.Type, request.Headline, request.Report);
        return Task.FromResult(new ErrorReply { Success = true, Msg = "" });
    }

    public override Task<ErrorReply> SendFeedbackEmailLog(FeedbackEmailRequestLog request, ServerCallContext context)
    {
        _log.LogInformation("feedback+log {Type}: {Headline} ({Log} bytes)", request.Type, request.Headline, request.Log.Length);
        return Task.FromResult(new ErrorReply { Success = true, Msg = "" });
    }

    public override Task<ErrorReply> SendFeedbackEmailImage(FeedbackEmailRequestImage request, ServerCallContext context)
    {
        _log.LogInformation("feedback+image {Type}: {Headline} ({Image} bytes)", request.Type, request.Headline, request.Image.Length);
        return Task.FromResult(new ErrorReply { Success = true, Msg = "" });
    }

    public override Task<ErrorReply> SendFeedbackEmailImageLog(FeedbackEmailRequestImageLog request, ServerCallContext context)
    {
        _log.LogInformation("feedback+image+log {Type}: {Headline} ({Image}/{Log} bytes)", request.Type, request.Headline, request.Image.Length, request.Log.Length);
        return Task.FromResult(new ErrorReply { Success = true, Msg = "" });
    }

    public override Task<RegionListReply> GetRegionList(RegionListRequest request, ServerCallContext context)
        => Task.FromResult(new RegionListReply { Regions = { _store.Region } });

    public override Task<RoomListReply> GetRoomList(RoomListRequest request, ServerCallContext context)
        => Task.FromResult(ToRoomList());

    public override Task<RoomListReply> GetRoomListByRegion(RoomListRequestRegion request, ServerCallContext context)
        => Task.FromResult(ToRoomList());

    public override Task<TextReply> GetRandomServer(TextRequest request, ServerCallContext context)
    {
        var rooms = _store.GetRooms();
        var room = rooms.Count > 0 ? rooms[Random.Shared.Next(rooms.Count)] : null;
        var host = _store.PublicHost ?? room?.Host ?? "";
        _log.LogInformation("random server requested -> {Host} ({Rooms} room(s))", host, rooms.Count);
        return Task.FromResult(new TextReply { Text = host });
    }

    public override Task<TextReply> GetRandomRegionServer(RegionServerRequest request, ServerCallContext context)
    {
        var rooms = _store.GetRooms();
        var room = rooms.Count > 0 ? rooms[Random.Shared.Next(rooms.Count)] : null;
        var host = _store.PublicHost ?? room?.Host ?? "";
        _log.LogInformation("random region server ({Region}) -> {Host} ({Rooms} room(s))", request.Region, host, rooms.Count);
        return Task.FromResult(new TextReply { Text = host });
    }

    public override Task<TextReply> GetNews(TextRequest request, ServerCallContext context)
        => Task.FromResult(new TextReply { Text = "Welcome to the private Book of Travels server." });

    public override Task<TextReply> GetPatchNotes(TextRequest request, ServerCallContext context)
        => Task.FromResult(new TextReply { Text = "Private server." });

    private RoomListReply ToRoomList()
    {
        var reply = new RoomListReply();
        foreach (var r in _store.GetRooms())
        {
            reply.Rooms.Add(new RoomDescription
            {
                Name = "Book of Travels",
                Host = _store.PublicHost ?? r.Host,
                Port = r.Port,
                PlayerCount = r.PlayerCount,
                MaxPlayerCount = 16,
                IsActive = true,
                CurrentVersion = "1.0",
                TargetVersion = "1.0"
            });
        }
        return reply;
    }
}
