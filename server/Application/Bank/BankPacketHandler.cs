using System.Text.Json;
using SGO.Server.Application.Packets;
using SGO.Server.Domain.Sessions;
using SGO.Server.Protocol;

namespace SGO.Server.Application.Bank;

public sealed class BankPacketHandler : IClientPacketHandler
{
    private readonly BankService _bank;

    public BankPacketHandler(BankService bank)
    {
        _bank = bank;
    }

    public string PacketType => "BankTransfer";

    public IReadOnlyList<object> Handle(
        JsonElement request,
        ClientSession session,
        long accountId)
    {
        if (!session.CharacterId.HasValue)
            return [];

        return _bank.Transfer(
            accountId,
            session.CharacterId.Value,
            request.Long("UID"),
            request.Int("TabType"),
            request.Int("Amount", 1),
            request.TryGetProperty("ToBank", out var toBank) &&
            toBank.ValueKind == JsonValueKind.True);
    }
}
