# Multi-Context Support Design Document

## Overview

This document provides technical architecture and implementation details for multi-context support in the EFCore adapter. For user-facing setup instructions, see [MULTI_CONTEXT_USAGE_GUIDE.md](MULTI_CONTEXT_USAGE_GUIDE.md).

**Purpose:** Enable multiple `CasbinDbContext` instances to store different policy types in separate database locations (schemas, tables, or databases) while maintaining transactional integrity where possible.

## Background

### Current Architecture
- Single `DbContext` per adapter instance
- Single `DbSet<TPersistPolicy>` for all policy types
- All policy types stored in the same table

### Motivation
- Store different policy types in separate schemas/tables
- Apply different retention strategies per policy type
- Enable multi-tenant scenarios with separate contexts
- Separate concerns for organizational requirements

### Requirements

**Functional:**
1. Route policy types to different `DbContext` instances
2. Maintain ACID guarantees when contexts share connections
3. Preserve backward compatibility
4. Support sync and async operations
5. Allow flexible user-defined routing logic

**Technical:**
1. Use EF Core's `UseTransaction()` for shared transactions
2. Support all existing operations (Load, Save, Add, Remove, Update, Filter)
3. Detect connection compatibility at runtime
4. Gracefully degrade to individual transactions when sharing is not possible

**Non-Requirements:**
- Distributed transactions across different databases/servers
- Automatic connection string management
- Schema migration coordination

## Architecture

### Context Provider Pattern

#### ICasbinDbContextProvider Interface

```csharp
public interface ICasbinDbContextProvider<TKey> where TKey : IEquatable<TKey>
{
    /// <summary>
    /// Gets the DbContext for a specific policy type (e.g., "p", "p2", "g", "g2")
    /// </summary>
    DbContext GetContextForPolicyType(string policyType);

    /// <summary>
    /// Gets all unique DbContext instances used by this provider.
    /// Used for operations that coordinate across all contexts (SavePolicy, LoadPolicy)
    /// </summary>
    IEnumerable<DbContext> GetAllContexts();
}
```

**Contract:**
- `GetContextForPolicyType()` must return a valid DbContext for any policy type
- `GetAllContexts()` must return all distinct contexts (used for SavePolicy, LoadPolicy)
- Same policy type should always route to the same context instance

#### Default Implementation

```csharp
/// <summary>
/// Default provider using single context for all policy types (backward compatibility)
/// </summary>
public class SingleContextProvider<TKey> : ICasbinDbContextProvider<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly DbContext _context;

    public SingleContextProvider(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public DbContext GetContextForPolicyType(string policyType) => _context;
    public IEnumerable<DbContext> GetAllContexts() => new[] { _context };
}
```

### Constructor Design

```csharp
public partial class EFCoreAdapter<TKey, TPersistPolicy, TDbContext>
{
    private readonly ICasbinDbContextProvider<TKey> _contextProvider;
    private readonly Dictionary<(DbContext, string), DbSet<TPersistPolicy>> _persistPoliciesByContext;

    /// <summary>
    /// NEW: Multi-context constructor with custom provider
    /// </summary>
    public EFCoreAdapter(ICasbinDbContextProvider<TKey> contextProvider)
    {
        _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
        _persistPoliciesByContext = new Dictionary<(DbContext, string), DbSet<TPersistPolicy>>();
        DbContext = null; // Kept for backward compatibility
    }

    /// <summary>
    /// EXISTING: Single-context constructor (unchanged behavior)
    /// </summary>
    public EFCoreAdapter(TDbContext context)
    {
        DbContext = context ?? throw new ArgumentNullException(nameof(context));
        _contextProvider = new SingleContextProvider<TKey>(context);
        _persistPoliciesByContext = new Dictionary<(DbContext, string), DbSet<TPersistPolicy>>();
    }

    protected TDbContext DbContext { get; } // Legacy property for compatibility
}
```

**Backward Compatibility:**
- Existing single-context constructor wraps context in `SingleContextProvider`
- All existing code continues to work unchanged
- `DbContext` property maintained for external code that may access it

### Transaction Coordination

#### Detection Logic

The adapter automatically detects if contexts can share transactions:

```csharp
private bool CanShareTransaction(List<DbContext> contexts)
{
    if (contexts.Count <= 1) return true;

    try
    {
        var firstConnection = contexts[0].Database.GetDbConnection();
        var firstConnectionString = firstConnection?.ConnectionString;

        if (string.IsNullOrEmpty(firstConnectionString))
            return false;

        return contexts.All(c =>
        {
            var connection = c.Database.GetDbConnection();
            return connection?.ConnectionString == firstConnectionString;
        });
    }
    catch (Exception)
    {
        // If we can't determine connection compatibility for any reason,
        // assume separate connections for safety
        return false;
    }
}
```

**Detection Strategy:**
- Compare connection strings across all contexts
- If all match → use shared transaction (atomic)
- If any differ → use individual transactions (not atomic)
- No errors thrown → graceful degradation

#### Shared Transaction Pattern

When connection strings match:

```csharp
// Pseudocode
var primaryContext = contexts.First();
using var transaction = primaryContext.Database.BeginTransaction();

foreach (var context in contexts)
{
    if (context != primaryContext)
        context.Database.UseTransaction(transaction.GetDbTransaction());

    // Perform operations on context
    context.SaveChanges();
}

transaction.Commit(); // Atomic across all contexts
```

#### Individual Transaction Pattern

When connection strings differ (e.g., separate SQLite files):

```csharp
// Pseudocode - WARNING: Not atomic across contexts
foreach (var context in contexts)
{
    using var transaction = context.Database.BeginTransaction();
    try
    {
        // Perform operations on context
        context.SaveChanges();
        transaction.Commit(); // Commits this context only
    }
    catch
    {
        transaction.Rollback();
        throw; // Failure in one context doesn't rollback others
    }
}
```

### Database Support

| Database | Same Schema | Different Schemas | Different Tables | Separate Files | Atomic Tx |
|----------|-------------|-------------------|------------------|----------------|-----------|
| **SQL Server** | ✅ | ✅ | ✅ | ✅ (same server) | ✅ |
| **PostgreSQL** | ✅ | ✅ | ✅ | ❌ | ✅ (same DB) |
| **MySQL** | ✅ | ✅ | ✅ | ❌ | ✅ (same DB) |
| **SQLite** | ✅ | N/A | ✅ (same file) | ⚠️ (no atomicity) | ✅ (same file only) |

**Key Constraints:**
- All contexts must connect to the same database for shared transactions
- SQLite cannot share transactions across different files
- Distributed transactions (cross-database) are not supported

## Implementation Details

### Internal Method Changes

#### Modified Virtual Method

```csharp
// Old signature - kept for backward compatibility, marked obsolete
[Obsolete("Use GetCasbinRuleDbSet(DbContext, string) instead. This method will be removed in a future major version.", false)]
protected virtual DbSet<TPersistPolicy> GetCasbinRuleDbSet(TDbContext dbContext)
{
    return GetCasbinRuleDbSet(dbContext, null);
}

// New signature - allows policy-type-aware customization
protected virtual DbSet<TPersistPolicy> GetCasbinRuleDbSet(DbContext dbContext, string policyType)
{
    return dbContext.Set<TPersistPolicy>();
}
```

**Rationale:**
- Old method is `protected virtual` - external code may override it
- Cannot remove without breaking change
- New signature enables policy-type-specific customization
- Old signature delegates to new one for compatibility

#### DbSet Caching

```csharp
private readonly Dictionary<(DbContext context, string policyType), DbSet<TPersistPolicy>> _persistPoliciesByContext;

private DbSet<TPersistPolicy> GetCasbinRuleDbSetForPolicyType(DbContext context, string policyType)
{
    var key = (context, policyType);
    if (!_persistPoliciesByContext.TryGetValue(key, out var dbSet))
    {
        dbSet = GetCasbinRuleDbSet(context, policyType);
        _persistPoliciesByContext[key] = dbSet;
    }
    return dbSet;
}
```

**Memory Characteristics:**
- Dictionary caches DbSet instances to avoid repeated `dbContext.Set<T>()` reflection calls
- Typical memory: 224 bytes (2 contexts × 2 policy types = 4 entries)
- Worst-case: ~3.5 KB (8 contexts × 8 policy types = 64 entries)
- Bounded growth: (# contexts × # policy types), stable after warm-up
- See MULTI_CONTEXT_USAGE_GUIDE.md for detailed memory analysis

### Operation Handling

#### SavePolicy (Multi-Context with Adaptive Transactions)

Most complex operation - coordinates across all contexts:

```csharp
// Pseudocode
public virtual void SavePolicy(IPolicyStore store)
{
    var persistPolicies = store.ReadPolicyFromCasbinModel();
    var policiesByContext = persistPolicies.GroupBy(p => GetContextForPolicyType(p.Type));
    var contexts = GetAllContexts().Distinct().ToList();

    if (contexts.Count == 1 || CanShareTransaction(contexts))
    {
        // Use shared transaction (atomic)
        SavePolicyWithSharedTransaction(contexts, policiesByContext);
    }
    else
    {
        // Use individual transactions (not atomic)
        SavePolicyWithIndividualTransactions(contexts, policiesByContext);
    }
}
```

#### LoadPolicy (Multi-Context, Read-Only)

No transaction needed:

```csharp
// Pseudocode
public virtual void LoadPolicy(IPolicyStore store)
{
    var allPolicies = new List<TPersistPolicy>();

    foreach (var context in GetAllContexts().Distinct())
    {
        var dbSet = GetCasbinRuleDbSet(context, null);
        var policies = dbSet.AsNoTracking().ToList();
        allPolicies.AddRange(policies);
    }

    store.LoadPolicyFromPersistPolicy(allPolicies);
    IsFiltered = false;
}
```

#### AddPolicy/RemovePolicy (Single Context)

Simple routing to appropriate context:

```csharp
// Pseudocode
public virtual void AddPolicy(string section, string policyType, IPolicyValues values)
{
    var context = GetContextForPolicyType(policyType);
    var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);

    // Add policy to dbSet
    context.SaveChanges();
}
```

#### UpdatePolicy (Single Context with Transaction)

```csharp
// Pseudocode
public void UpdatePolicy(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
{
    var context = GetContextForPolicyType(policyType);
    using var transaction = context.Database.BeginTransaction();

    try
    {
        RemovePolicy(context, section, policyType, oldValues);
        AddPolicy(context, section, policyType, newValues);
        context.SaveChanges();
        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

## Implementation Decisions

### 1. Runtime Detection vs. Validation

**Decision:** Implement runtime detection without throwing errors

**Rationale:**
- Allows flexible configurations (testing with separate files)
- Graceful degradation to individual transactions
- No breaking changes for existing code
- Users responsible for understanding trade-offs

**Alternative Considered:** Throw exception if connection strings don't match
**Rejected Because:** Too restrictive for testing scenarios

### 2. Schema-Based Provider

**Decision:** Not implement in core library

**Rationale:**
- Users can easily implement custom providers
- Keeps adapter focused and flexible
- Example implementations provided in documentation

**Alternative Considered:** Include `SchemaBasedContextProvider` in library
**Rejected Because:** Too opinionated, users have varying needs

### 3. Virtual Method Enhancement

**Decision:** Add `policyType` parameter to `GetCasbinRuleDbSet()`

**Rationale:**
- Enables advanced customization scenarios
- Maintains backward compatibility via `[Obsolete]` attribute
- External code overriding old method continues to work

### 4. Database Initialization

**Issue:** `EnsureCreated()` unreliable across EF Core versions

**Solution:**
- Explicit model initialization: `_ = dbContext.Model;`
- Fallback: delete and recreate if table doesn't exist
- Applied in test fixtures and extension methods

### 5. SQLite Transaction Limitation

**Discovery:** `UseTransaction()` fails for separate SQLite files with "transaction not associated with connection"

**Root Cause:** Each SQLite file has its own connection

**Solution:** Adaptive transaction handling based on `CanShareTransaction()`

**Impact:** Tests use separate files for isolation but accept non-atomic behavior

## Performance & Limitations

### Performance Overhead

Multiple contexts incur:
- Additional connection management overhead
- Context switching costs
- Multiple `SaveChanges()` calls per operation
- Negligible memory overhead (~224 bytes to 3.5 KB for caching)

### Limitations

**Transaction-Related:**
1. SQLite separate files cannot share transactions
2. Same connection string required for atomicity
3. No cross-database or cross-server support
4. No distributed transaction coordination (DTC)

**General:**
1. Users responsible for schema management and migrations
2. Error handling complexity with individual transactions
3. Partial failures possible when transaction sharing unavailable

### When to Use Multi-Context

**Good Use Cases:**
- Separate policy and grouping data for compliance
- Apply different retention policies per type
- Multi-tenant routing with tenant-specific contexts
- Organizational separation of concerns

**Not Recommended For:**
- Performance optimization (adds overhead, not reduces it)
- Cross-database scenarios requiring atomicity
- Simple authorization models (single context sufficient)

## Status

**Implementation:** ✅ Complete
**Testing:** ✅ All 120 tests passing (30 tests × 4 frameworks)
**Documentation:** ✅ Complete
**Breaking Changes:** None - fully backward compatible

## See Also

- [MULTI_CONTEXT_USAGE_GUIDE.md](MULTI_CONTEXT_USAGE_GUIDE.md) - Step-by-step user guide
- [ICasbinDbContextProvider Interface](Casbin.Persist.Adapter.EFCore/ICasbinDbContextProvider.cs) - Interface source code
- [EFCoreAdapter Implementation](Casbin.Persist.Adapter.EFCore/EFCoreAdapter.cs) - Adapter source code
