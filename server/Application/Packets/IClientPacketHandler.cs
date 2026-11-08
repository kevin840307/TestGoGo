using System.Text.Json;
using SGO.Server.Domain.Sessions;

namespace SGO.Server.Application.Packets;

public interface IClientPacketHandler
{
    string PacketType { get; }

    IReadOnlyList<object> Handle(
        JsonElement request,
        ClientSession session,
        long accountId);
}
