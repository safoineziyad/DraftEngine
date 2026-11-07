using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Core.Services;

/// <summary>
/// Edits contracts with version snapshots before destructive changes, and supports
/// duplicate, soft-archive/restore, and permanent delete from the archive.
/// </summary>
public class ContractEditService : IContractEditService
{
    private readonly IDatabaseContextFactory _databaseFactory;

    public ContractEditService(IDatabaseContextFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public async Task<Contract> DuplicateContractAsync(int sourceContractId, string newTitle)
    {
        using var database = _databaseFactory.CreateContext();
        var source = await database.Set<Contract>().Include(c => c.ContractClauses).Include(c => c.ContractFields)
            .FirstOrDefaultAsync(c => c.Id == sourceContractId);
        if (source is null) return null;

        var copy = new Contract
        {
            UserId = source.UserId,
            Title = newTitle,
            ContractType = source.ContractType,
            ClientName = source.ClientName,
            ClientEmail = source.ClientEmail,
            PartiesJson = source.PartiesJson,
            EffectiveDate = source.EffectiveDate,
            GoverningLaw = source.GoverningLaw,
            TermMonths = source.TermMonths,
            Status = ContractStatus.Draft,
            AssembledText = string.Empty,
            FinalFilePath = string.Empty
        };
        database.Set<Contract>().Add(copy);
        await database.SaveChangesAsync();

        foreach (var cc in source.ContractClauses)
            database.Set<ContractClause>().Add(new ContractClause
            {
                ContractId = copy.Id, ClauseId = cc.ClauseId, Selected = cc.Selected,
                CustomBodyOverride = cc.CustomBodyOverride
            });
        foreach (var f in source.ContractFields)
            database.Set<ContractField>().Add(new ContractField
            {
                ContractId = copy.Id, Key = f.Key, Value = f.Value
            });
        await database.SaveChangesAsync();
        return copy;
    }

    public async Task<List<Contract>> GetArchivedContractsAsync(int userId)
    {
        using var database = _databaseFactory.CreateContext();
        return await database.Set<Contract>()
            .AsNoTracking()
            .Where(contract => contract.UserId == userId && contract.IsArchived)
            .OrderByDescending(contract => contract.CreatedAt)
            .ToListAsync();
    }

    public async Task ArchiveContractAsync(int contractId)
    {
        using var database = _databaseFactory.CreateContext();
        var c = await database.Set<Contract>().FirstOrDefaultAsync(x => x.Id == contractId);
        if (c is null || c.IsArchived) return;
        c.IsArchived = true;
        c.ArchivedAt = DateTime.UtcNow;
        await database.SaveChangesAsync();
    }

    public async Task<bool> RestoreContractAsync(int contractId)
    {
        using var database = _databaseFactory.CreateContext();
        var c = await database.Set<Contract>().FirstOrDefaultAsync(x => x.Id == contractId);
        if (c is null || !c.IsArchived) return false;
        c.IsArchived = false;
        c.ArchivedAt = null;
        await database.SaveChangesAsync();
        return true;
    }

    public async Task PermanentDeleteContractAsync(int contractId)
    {
        using var database = _databaseFactory.CreateContext();
        var c = await database.Set<Contract>()
            .Include(contract => contract.ContractClauses)
            .Include(contract => contract.ContractFields)
            .Include(contract => contract.ChatMessages)
            .Include(contract => contract.GeneratedDocuments)
            .Include(contract => contract.Versions)
            .FirstOrDefaultAsync(contract => contract.Id == contractId);
        if (c is null) return;

        database.Set<ContractClause>().RemoveRange(c.ContractClauses);
        database.Set<ContractField>().RemoveRange(c.ContractFields);
        database.Set<ChatMessage>().RemoveRange(c.ChatMessages);
        database.Set<GeneratedDocument>().RemoveRange(c.GeneratedDocuments);
        database.Set<ContractVersion>().RemoveRange(c.Versions);
        database.Set<Contract>().Remove(c);
        await database.SaveChangesAsync();
    }

    public async Task<Contract> UpdateContractAsync(int contractId, Action<Contract> mutate)
    {
        using var database = _databaseFactory.CreateContext();
        var c = await database.Set<Contract>().FirstOrDefaultAsync(x => x.Id == contractId);
        if (c is null) return null;
        mutate(c);
        c.UpdatedAt = DateTime.UtcNow;
        await database.SaveChangesAsync();
        return c;
    }
}
