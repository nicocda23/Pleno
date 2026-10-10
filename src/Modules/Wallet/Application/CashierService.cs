using System.Collections.Concurrent;
using Casino.BuildingBlocks;
using Casino.Modules.Wallet.Domain;
using Marten;

namespace Casino.Modules.Wallet.Application;

/// <summary>El lugar de una persona en la jerarquia de cargas.</summary>
public enum HierarchyLevel
{
    /// <summary>Jugador comun. Su padre es el cajero que le carga.</summary>
    Player = 0,

    /// <summary>Cajero. Su padre es el jefe de cajeros que le carga.</summary>
    Cashier = 1,

    /// <summary>Jefe de cajeros. Es la raiz: no tiene padre; sus fichas se las da el backoffice.</summary>
    HeadCashier = 2,
}

/// <summary>Un nodo del arbol de cargas: de quien depende cada usuario. Un solo padre por usuario (un arbol, no un grafo).</summary>
public sealed class HierarchyNode
{
    /// <summary>El usuario (tambien es el id del documento).</summary>
    public Guid Id { get; set; }

    public HierarchyLevel Level { get; set; }

    public Guid? ParentUserId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid UpdatedBy { get; set; }

    /// <summary>
    /// El nombre de usuario (el "preferred_username" del token) para que quien tiene a esta persona a cargo pueda distinguirla. Solo se guarda de quienes estan en el arbol
    /// y se renueva cuando la persona usa la API; nunca se muestra fuera de su jurisdiccion.
    /// </summary>
    public string? DisplayName { get; set; }
}

/// <summary>Un jugador (o cajero) de la jurisdiccion de alguien, con su saldo si ya tiene cuenta.</summary>
public sealed record JurisdictionMember(Guid UserId, HierarchyLevel Level, string? DisplayName, long? Available, long? Reserved);

/// <summary>
/// Cajeros y jefes de cajeros: arman el arbol de jurisdicciones (lo decide el backoffice) y cargan fichas hacia abajo (jefe → cajero → jugador) desde su
/// PROPIO saldo, por transferencia: las fichas nunca se crean, solo se mueven. La Wallet garantiza la contabilidad; aca vive la regla de quien puede cargarle a quien.
/// </summary>
public sealed class CashierService(IDocumentStore store, WalletService wallet, BackofficeAudit audit, TimeProvider clock)
{
    private const int MaxDisplayNameLength = 60;

    // Ultimo nombre guardado de cada usuario (en esta instancia): evita ir a la base en cada pedido cuando el nombre no cambio.
    private readonly ConcurrentDictionary<Guid, string> _knownNames = new();

    /// <summary>
    /// Guarda el nombre de usuario de quien esta en el arbol (si no esta, no guarda nada: no se acumulan nombres de quien no los necesita). Nunca falla el pedido que lo dispara.
    /// </summary>
    public async Task TouchDisplayNameAsync(Guid userId, string? rawName, CancellationToken ct = default)
    {
        var name = CleanName(rawName);
        if (name is null || (_knownNames.TryGetValue(userId, out var known) && known == name))
        {
            return;
        }

        try
        {
            await using var session = store.LightweightSession();
            var node = await session.LoadAsync<HierarchyNode>(userId, ct);
            if (node is null)
            {
                return; // no esta en el arbol todavia: se guardara cuando lo ubiquen y vuelva a usar la API
            }

            if (node.DisplayName != name)
            {
                node.DisplayName = name;
                session.Store(node);
                await session.SaveChangesAsync(ct);
            }

            _knownNames[userId] = name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mejora de visualizacion: si falla, se reintenta en el proximo pedido.
        }
    }

    private static string? CleanName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var name = new string([.. rawName.Trim().Where(c => !char.IsControl(c))]);
        return name.Length == 0 ? null : name[..Math.Min(name.Length, MaxDisplayNameLength)];
    }

    /// <summary>El nivel que da un conjunto de roles del token (el mas alto manda). Null si no es cajero ni jefe.</summary>
    public static HierarchyLevel? LevelOfRoles(IEnumerable<string> roles)
    {
        var list = roles as IReadOnlyCollection<string> ?? [.. roles];
        if (list.Contains(Roles.HeadCashier))
        {
            return HierarchyLevel.HeadCashier;
        }

        return list.Contains(Roles.Cashier) ? HierarchyLevel.Cashier : null;
    }

    public static string NameOf(HierarchyLevel level) => level switch
    {
        HierarchyLevel.HeadCashier => Roles.HeadCashier,
        HierarchyLevel.Cashier => Roles.Cashier,
        _ => Roles.Player,
    };

    public static bool TryParseLevel(string? name, out HierarchyLevel level)
    {
        switch (name)
        {
            case Roles.HeadCashier:
                level = HierarchyLevel.HeadCashier;
                return true;
            case Roles.Cashier:
                level = HierarchyLevel.Cashier;
                return true;
            case Roles.Player:
                level = HierarchyLevel.Player;
                return true;
            default:
                level = default;
                return false;
        }
    }

    // ---- Backoffice: armar el arbol ----

    /// <summary>
    /// Pone a <paramref name="userId"/> en el arbol con ese nivel y ese padre. Reglas: el jefe no tiene padre; el padre de un cajero es un jefe y el de un jugador es un cajero;
    /// no se cambia el nivel de quien ya tiene gente a cargo (se mueve primero a su gente).
    /// </summary>
    public async Task<HierarchyNode> AssignAsync(Guid actorUserId, Guid userId, HierarchyLevel level, Guid? parentUserId, CancellationToken ct = default)
    {
        if (parentUserId == userId)
        {
            throw new WalletDomainException(WalletError.InvalidHierarchy, "Nadie puede depender de si mismo.");
        }

        await using var session = store.LightweightSession();
        if (level == HierarchyLevel.HeadCashier)
        {
            if (parentUserId is not null)
            {
                throw new WalletDomainException(WalletError.InvalidHierarchy, "El jefe de cajeros no tiene padre: sus fichas se las da el backoffice.");
            }
        }
        else if (parentUserId is { } parentId)
        {
            var parent = await session.LoadAsync<HierarchyNode>(parentId, ct);
            if (parent is null || parent.Level != level + 1)
            {
                throw new WalletDomainException(
                    WalletError.InvalidHierarchy,
                    level == HierarchyLevel.Cashier ? "El padre de un cajero tiene que ser un jefe de cajeros." : "El padre de un jugador tiene que ser un cajero.");
            }
        }

        var existing = await session.LoadAsync<HierarchyNode>(userId, ct);
        if (existing is not null && existing.Level != level && await session.Query<HierarchyNode>().AnyAsync(n => n.ParentUserId == userId, ct))
        {
            throw new WalletDomainException(WalletError.InvalidHierarchy, "Tiene gente a su cargo: movela antes de cambiarle el nivel.");
        }

        var node = existing ?? new HierarchyNode { Id = userId };
        node.Level = level;
        node.ParentUserId = parentUserId;
        node.UpdatedAt = clock.GetUtcNow();
        node.UpdatedBy = actorUserId;
        session.Store(node);
        await session.SaveChangesAsync(ct);
        return node;
    }

    public async Task<IReadOnlyList<HierarchyNode>> ListAllAsync(CancellationToken ct = default)
    {
        await using var session = store.QuerySession();
        return await session.Query<HierarchyNode>().OrderBy(n => n.Level).ToListAsync(ct);
    }

    public async Task<HierarchyNode?> GetNodeAsync(Guid userId, CancellationToken ct = default)
    {
        await using var session = store.QuerySession();
        return await session.LoadAsync<HierarchyNode>(userId, ct);
    }

    // ---- Cajero / jefe ----

    /// <summary>Los nombres de la gente a cargo directo de <paramref name="parentUserId"/> (solo de ellos, nunca de otros).</summary>
    public async Task<IReadOnlyDictionary<Guid, string?>> GetMemberNamesAsync(Guid parentUserId, CancellationToken ct = default)
    {
        await using var session = store.QuerySession();
        var nodes = await session.Query<HierarchyNode>().Where(n => n.ParentUserId == parentUserId).ToListAsync(ct);
        return nodes.ToDictionary(n => n.Id, n => n.DisplayName);
    }

    /// <summary>La gente a cargo directo de <paramref name="parentUserId"/>, con su saldo.</summary>
    public async Task<IReadOnlyList<JurisdictionMember>> ListMembersAsync(Guid parentUserId, int limit, CancellationToken ct = default)
    {
        await using var session = store.QuerySession();
        var nodes = await session.Query<HierarchyNode>().Where(n => n.ParentUserId == parentUserId).OrderBy(n => n.UpdatedAt).Take(limit).ToListAsync(ct);
        var members = new List<JurisdictionMember>(nodes.Count);
        foreach (var node in nodes)
        {
            try
            {
                var account = await wallet.GetAsync(PlayerIds.WalletAccountFor(node.Id), ct);
                members.Add(new JurisdictionMember(node.Id, node.Level, node.DisplayName, account.Available, account.Reserved));
            }
            catch (WalletDomainException ex) when (ex.Error == WalletError.AccountNotFound)
            {
                members.Add(new JurisdictionMember(node.Id, node.Level, node.DisplayName, null, null)); // todavia no abrio su cuenta
            }
        }

        return members;
    }

    /// <summary>
    /// Carga fichas desde el saldo de quien llama hacia alguien de su jurisdiccion DIRECTA: el jefe solo a sus cajeros y el cajero solo a sus jugadores.
    /// El nivel de quien llama sale de su token (rol) y tiene que coincidir con el que le asigno el backoffice en el arbol.
    /// </summary>
    public async Task<OperationOutcome> LoadChipsAsync(Guid actorUserId, IEnumerable<string> actorRoles, Guid targetUserId, long amount, string idempotencyKey, CancellationToken ct = default)
    {
        var actorLevel = LevelOfRoles(actorRoles)
            ?? throw new WalletDomainException(WalletError.NotInJurisdiction, "Tu usuario no es cajero ni jefe de cajeros.");

        var actor = await GetNodeAsync(actorUserId, ct);
        if (actor is null || actor.Level != actorLevel)
        {
            throw new WalletDomainException(WalletError.NotInJurisdiction, "Todavia no te asignaron un lugar en la jerarquia de cajeros.");
        }

        var target = await GetNodeAsync(targetUserId, ct);
        if (target is null || target.ParentUserId != actorUserId || target.Level != actorLevel - 1)
        {
            throw new WalletDomainException(
                WalletError.NotInJurisdiction,
                actorLevel == HierarchyLevel.HeadCashier ? "Solo podes cargarle a los cajeros de tu jurisdiccion." : "Solo podes cargarle a los jugadores de tu jurisdiccion.");
        }

        var outcome = await wallet.TransferAsync(actorUserId, targetUserId, idempotencyKey, amount, ct);
        // Tambien si fue un duplicado: asi un reintento completa la anotacion si fallo la primera vez.
        await audit.RecordTransferAsync(actorUserId, targetUserId, PlayerIds.WalletAccountFor(targetUserId), amount, idempotencyKey, outcome.TransactionId, ct);
        return outcome;
    }
}
