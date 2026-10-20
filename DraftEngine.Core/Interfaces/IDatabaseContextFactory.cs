using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Creates short-lived EF Core contexts, one per operation. Services stay stateless and
/// open a fresh context for each call (the pattern EF Core recommends for desktop apps),
/// so no context is shared across operations or threads and nothing is pinned for the
/// application's lifetime. Callers own the returned context and must dispose it,
/// typically with <c>using</c>.
/// </summary>
public interface IDatabaseContextFactory
{
    /// <summary>Creates a new, unshared EF Core context.</summary>
    DbContext CreateContext();
}
