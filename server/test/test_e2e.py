#!/usr/bin/env python3
"""End-to-end test of the emulated master server against the exact wire contract
the Book of Travels client uses.

Flow (mirrors the game):
  client -> Game.Authenticate(email, password, steamID)     -> JWT
  instance -> Instance.Ping(room_port, player_count)        -> security_key (JWT key)
  verify JWT signature against security_key (what OnServerLogin does)
  client -> Game.GetRandomServer / GetRoomList              -> the registered room
"""
import sys, json, base64, hmac, hashlib, time
sys.path.insert(0, "/tmp/proto_py")
import grpc
import Game_pb2, Game_pb2_grpc
import Instance_pb2, Instance_pb2_grpc
import Master_pb2, Master_pb2_grpc
import Admin_pb2, Admin_pb2_grpc

HOST = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1:1234"
ch = grpc.insecure_channel(HOST)
fail = 0

def check(name, cond, extra=""):
    global fail
    print(f"  [{'PASS' if cond else 'FAIL'}] {name} {extra}")
    if not cond: fail += 1

print("== Game.Authenticate ==")
game = Game_pb2_grpc.GameStub(ch)
reply = game.Authenticate(Game_pb2.Credentials(email="player1@example.com", password="secret", serviceId=76561198000000001))
check("auth_token non-empty", bool(reply.auth_token))
check("msg empty on success", reply.msg == "", repr(reply.msg))
jwt = reply.auth_token
print(f"    jwt: {jwt[:60]}...")

print("== Instance.Ping (game server registering) ==")
inst = Instance_pb2_grpc.InstanceStub(ch)
pong = inst.Ping(Instance_pb2.InstancePingRequest(room_port=50050, player_count=2))
check("next_ping_ms > 0", pong.next_ping_ms > 0)
check("status CONTINUE", pong.status == Instance_pb2.INSTANCE_STATUS_CONTINUE)
check("max_player_count > 0", pong.max_player_count > 0)
check("security_key present", len(pong.security_key) >= 32, f"({len(pong.security_key)} bytes)")
key = pong.security_key

print("== JWT validation (what OnServerLogin does) ==")
# decode payload
hdr, payload, sig = jwt.split(".")
payload_json = json.loads(base64.urlsafe_b64decode(payload + "=="))
check("iss == MasterServer", payload_json.get("iss") == "MasterServer", repr(payload_json.get("iss")))
check("aud == ServerInstance", payload_json.get("aud") == "ServerInstance", repr(payload_json.get("aud")))
check("uid claim is int", isinstance(payload_json.get("uid"), int), repr(payload_json.get("uid")))
exp = payload_json.get("exp", 0)
check("exp in future", exp > time.time())
# HS256 verify with the key from Instance.Ping
sig_check = base64.urlsafe_b64encode(
    hmac.new(key, f"{hdr}.{payload}".encode(), hashlib.sha256).digest()
).rstrip(b"=").decode()
check("signature verifies with instance security_key", sig_check == sig)

print("== Game.GetRandomServer / GetRoomList ==")
rs = game.GetRandomServer(Game_pb2.TextRequest())
check("random server == instance ip", rs.text == "127.0.0.1", repr(rs.text))
rooms = game.GetRoomList(Game_pb2.RoomListRequest())
check("room list has 1 room", len(rooms.rooms) == 1)
if rooms.rooms:
    r = rooms.rooms[0]
    check("room port == 50050", r.port == 50050)
    check("room active", r.is_active)

print("== Game.GetRegionList / GetNews ==")
regions = game.GetRegionList(Game_pb2.RegionListRequest())
check("region list non-empty", len(regions.regions) > 0, repr(list(regions.regions)))
news = game.GetNews(Game_pb2.TextRequest())
check("news non-empty", bool(news.text))

print("== Master.GetConfig (instance bootstrap) ==")
master = Master_pb2_grpc.MasterStub(ch)
cfg = master.GetConfig(Master_pb2.ConfigRequest(host="127.0.0.1", instance_id="i-1", server_version="1.0"))
check("config has same security_key", cfg.security_key == key)

print("== Admin.GetAllServers (fleet view) ==")
admin = Admin_pb2_grpc.AdminStub(ch)
servers = admin.GetAllServers(Admin_pb2.ListServersRequest(region="eu-central-1"))
check("servers list has 1 entry", len(servers.servers) == 1)

print("== account persistence: wrong password rejected, re-login OK ==")
bad = game.Authenticate(Game_pb2.Credentials(email="player1@example.com", password="wrong", serviceId=0))
check("wrong password rejected", bad.auth_token == "" and bad.msg != "", repr(bad.msg))
again = game.Authenticate(Game_pb2.Credentials(email="player1@example.com", password="secret", serviceId=76561198000000001))
check("re-login with correct password works", bool(again.auth_token))

print()
print("ALL PASS" if fail == 0 else f"{fail} FAILURES")
sys.exit(1 if fail else 0)
