using SGO.Server.Application.Abstractions;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Character;

public sealed record PetEvolutionAdvanceResult(
    Dictionary<string, object> Pet,
    int FromEvolId,
    int ToEvolId,
    bool Changed);

public sealed class PetEvolutionService
{
    private readonly IPetStore _store;
    private readonly CurrentClientDataCatalog _current;

    public PetEvolutionService(
        IPetStore store,
        CurrentClientDataCatalog current)
    {
        _store = store;
        _current = current;
    }

    public PetEvolutionAdvanceResult Advance(
        long accountId,
        long dbid,
        long petUid,
        Dictionary<string, object> pet)
    {
        var currentPet = pet;
        var fromEvolId = Convert.ToInt32(
            currentPet["EvolID"]);
        var currentEvolId = fromEvolId;

        // A large EXP grant can cross multiple linear evolution stages.
        // Branches are still constrained by each current evolution's
        // next_evol_ids, so evolution can never jump backwards or sideways.
        for (var guard = 0; guard < 32; guard++)
        {
            var petId = Convert.ToInt32(
                currentPet["PETID"]);
            var level = Convert.ToInt32(
                currentPet["Level"]);
            var con =
                Convert.ToInt32(currentPet["CON"]) +
                Convert.ToInt32(currentPet["ExtraCON"]);
            var str =
                Convert.ToInt32(currentPet["STR"]) +
                Convert.ToInt32(currentPet["ExtraSTR"]);
            var dex =
                Convert.ToInt32(currentPet["DEX"]) +
                Convert.ToInt32(currentPet["ExtraDEX"]);
            var wis =
                Convert.ToInt32(currentPet["WIS"]) +
                Convert.ToInt32(currentPet["ExtraWIS"]);

            var next = _current.ResolveNextPetEvolution(
                petId,
                currentEvolId,
                level,
                con,
                str,
                dex,
                wis);

            if (next is null ||
                next.EvolId == currentEvolId)
                break;

            var updated = _store.SetPetEvolution(
                accountId,
                dbid,
                petUid,
                next.EvolId);

            if (updated is null)
                break;

            currentPet = updated;
            currentEvolId = next.EvolId;
        }

        return new PetEvolutionAdvanceResult(
            currentPet,
            fromEvolId,
            currentEvolId,
            currentEvolId != fromEvolId);
    }
}
