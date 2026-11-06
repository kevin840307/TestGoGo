using System.Text.Json;

namespace SGO.Server.Application.Abstractions;

public interface IGameStore
{
    (bool Success, string Message) Register(string username, string password, string email);
    long? Login(string username, string password);

    List<Dictionary<string, object>> Characters(long accountId);
    Dictionary<string, object>? GetCharacter(long accountId, long dbid);

    (bool Success, string Message) Create(long accountId, JsonElement data);
    bool Delete(long accountId, long dbid, string password);
    bool ChangeCharacterPassword(long accountId, long dbid, string newPassword);

    bool UpdatePosition(long accountId, long dbid, int x, int y);
    bool UpdateLocation(long accountId, long dbid, int mapId, int x, int y);
    bool SetRebornLocation(long accountId, long dbid, int mapId, int x, int y);
    bool UpdateState(long accountId, long dbid, string state);

    Dictionary<string, object>? Revive(long accountId, long dbid);

    Dictionary<string, object>? ApplyDamage(
        long accountId,
        long dbid,
        int damage);

    Dictionary<string, object>? AddRewards(
        long accountId,
        long dbid,
        int exp,
        int money);
}
