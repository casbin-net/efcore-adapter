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
- Enable multi-tenant scenarios with separate contexts
- Separate concerns for organizational requirements

### Requirements

**Functional:**
1. Route policy types to different `DbContext` instances
2. Maintain ACID guarantees when contexts share connections
3. Preserve backward compatibility

**Technical:**
1. Use EF Core's `UseTransaction()` for shared transactions
2. Detect connection compatibility at runtime
3. Gracefully degrade to individual transactions when sharing is not possible

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

    /// <summary>
    /// Gets the shared DbConnection if all contexts use the same physical connection.
    /// Returns null if contexts use separate connections.
    /// </summary>
    /// <remarks>
    /// When non-null, the adapter starts transactions at the connection level
    /// (connection.BeginTransaction()) rather than context level, which is required
    /// for proper savepoint handling in PostgreSQL and other databases that require
    /// explicit transaction blocks before creating savepoints.
    ///
    /// Return null for scenarios where contexts use separate physical connections
    /// (e.g., separate SQLite database files), in which case the adapter will use
    /// separate transactions for each context.
    /// </remarks>
    /// <returns>The shared DbConnection, or null if contexts use separate connections</returns>
    DbConnection? GetSharedConnection();
}
```

**Contract:**
- `GetContextForPolicyType()` must return a valid DbContext for any policy type
- `GetAllContexts()` must return all distinct contexts (used for SavePolicy, LoadPolicy)
- Same policy type should always route to the same context instance
- `GetSharedConnection()` must return the shared DbConnection when all contexts use the same physical connection, or null when contexts use separate connections

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

    /// <summary>
    /// Returns null since single-context scenarios don't have a shared connection
    /// (only one context, so the concept of "shared" doesn't apply).
    /// </summary>
    public DbConnection? GetSharedConnection() => null;
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

#### Provider-Declared Connection Strategy

The adapter uses the provider's `GetSharedConnection()` method to determine transaction strategy:

```csharp
var sharedConnection = _contextProvider?.GetSharedConnection();

if (sharedConnection != null)
{
    // Use connection-level transaction (atomic)
    SavePolicyWithSharedTransaction_ConnectionLevel(sharedConnection, contexts, policiesByContext);
}
else
{
    // Use context-level transactions (not atomic across contexts)
    SavePolicyWithIndividualTransactions(contexts, policiesByContext);
}
```

**Strategy:**
- Provider explicitly declares connection topology via `GetSharedConnection()`
- If provider returns a DbConnection → all contexts share that connection → use connection-level transaction
- If provider returns null → contexts use separate connections → use individual transactions
- No runtime detection → provider knows best about connection strategy

#### Connection-Level Transaction Pattern (PostgreSQL Savepoint Support)

When provider returns a shared DbConnection:

```csharp
// Actual implementation (simplified)
var sharedConnection = _contextProvider?.GetSharedConnection();

if (sharedConnection.State != ConnectionState.Open)
{
    sharedConnection.Open();
}

using var transaction = sharedConnection.BeginTransaction();  // ← Connection-level

try
{
    // Enlist all contexts in the connection-level transaction
    foreach (var context in contexts)
    {
        context.Database.UseTransaction(transaction);
    }

    // Clear and add policies for each context
    foreach (var contextGroup in policiesByContext)
    {
        var dbSet = GetCasbinRuleDbSetForPolicyType(contextGroup.Key, null);

        // Clear existing policies
        var existingPolicies = dbSet.ToList();
        dbSet.RemoveRange(existingPolicies);
        contextGroup.Key.SaveChanges();

        // Add new policies
        dbSet.AddRange(contextGroup);
        contextGroup.Key.SaveChanges();
    }

    transaction.Commit(); // Atomic across all contexts
}
catch
{
    transaction.Rollback();
    throw;
}
```

**Key Points:**
- Transaction started at **connection level** (`connection.BeginTransaction()`) not context level
- Required for PostgreSQL savepoint handling - PostgreSQL requires explicit `BEGIN` before creating savepoints
- When EF Core uses `UseTransaction()` with multiple contexts on same connection, it creates savepoints internally
- PostgreSQL savepoints require an active transaction block at the connection level
- All contexts enlisted in the same connection-level transaction using `context.Database.UseTransaction()`

#### Individual Transaction Pattern (Fallback)

When provider returns null (separate connections):

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
- All contexts must use the **same DbConnection object instance** for shared transactions
- Users must explicitly create and pass a shared connection object to all contexts
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

### AutoSave Mode and Transaction Atomicity

The Casbin Enforcer's `EnableAutoSave` setting fundamentally affects transaction atomicity in multi-context scenarios.

**AutoSave ON (Default Behavior):**

When AutoSave is enabled, the Casbin Enforcer immediately calls the adapter's Add/Remove/Update methods for each operation. The adapter then calls `DbContext.SaveChangesAsync()`, which creates an implicit transaction for that single operation.

**Code Flow:**
1. User calls `enforcer.AddPolicyAsync("alice", "data1", "read")`
2. Enforcer immediately calls `adapter.AddPolicyAsync(...)`
3. Adapter calls `context.SaveChangesAsync()` → commits to database
4. Returns to user

**Implications:**
- Each operation is atomic in isolation
- **No transaction coordination across multiple operations**
- If a sequence of operations fails partway through, earlier operations remain committed
- The adapter's `SavePolicyAsync()` transaction coordination is bypassed entirely

**AutoSave OFF (Batch Mode):**

When AutoSave is disabled, operations stay in the Enforcer's in-memory policy store. Only when `SavePolicyAsync()` is called does the adapter receive all policies at once, enabling atomic transaction coordination.

**Code Flow:**
1. User calls `enforcer.AddPolicyAsync("alice", "data1", "read")` → stored in memory
2. User calls `enforcer.AddGroupingPolicyAsync("alice", "admin")` → stored in memory
3. User calls `enforcer.SavePolicyAsync()`
4. Adapter receives ALL policies and uses shared transaction
5. All contexts commit atomically or all roll back

**Design Implication:**

The adapter **cannot** provide cross-context atomicity when AutoSave is ON because it never receives multiple policies in a single method call. Transaction coordination requires all policies to be processed together in `SavePolicyAsync()`.

**Rollback Test Evidence:**

The integration tests `SavePolicy_WhenTableDroppedInOneContext_ShouldRollbackAllContexts` and `SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts` originally failed because they used `AddPolicyAsync()` with AutoSave ON (default). This caused policies to commit immediately, preventing rollback verification.

**Fix:** Adding `enforcer.EnableAutoSave(false)` at lines 302 and 370 in `TransactionIntegrityTests.cs` fixed the tests by ensuring policies stayed in memory until `SavePolicyAsync()` was called, allowing proper atomic rollback testing.

**Code Evidence:**
```csharp
// TransactionIntegrityTests.cs:302, 370
try
{
    // Disable AutoSave so policies stay in-memory until SavePolicyAsync() is called
    enforcer.EnableAutoSave(false);

    // Add policies to all contexts (in memory only, AutoSave is OFF)
    await enforcer.AddPolicyAsync("alice", "data1", "read");
    await enforcer.AddGroupingPolicyAsync("alice", "admin");
    await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser");

    // Simulate failure (e.g., drop table in one context)
    await _fixture.DropTableAsync(TransactionIntegrityTestFixture.RolesSchema);

    // Try to save - should fail and rollback ALL contexts atomically
    await adapter.SavePolicyAsync(enforcer.GetModel());

    // Verify all contexts rolled back to 0 policies (atomicity verified)
}
```

**Recommendation:**

For multi-context scenarios requiring atomicity:
1. Use `enforcer.EnableAutoSave(false)`
2. Ensure all contexts share the same `DbConnection` object
3. Call `SavePolicyAsync()` to batch commit atomically

**Reference:** See [MULTI_CONTEXT_USAGE_GUIDE.md](MULTI_CONTEXT_USAGE_GUIDE.md#enableautosave-and-transaction-atomicity) for detailed user guidance and examples.

### When to Use Multi-Context

**Good Use Cases:**
- Separate policy and grouping data for compliance
- Multi-tenant routing with tenant-specific contexts
- Organizational separation of concerns

**Not Recommended For:**
- Cross-database scenarios requiring atomicity
- Simple authorization models (single context sufficient)

## Verification

### Integration Tests

Transaction integrity guarantees are verified by comprehensive integration tests in:
- **[TransactionIntegrityTests.cs](../Casbin.Persist.Adapter.EFCore.UnitTest/Integration/TransactionIntegrityTests.cs)** - Proves atomic commit/rollback across multiple contexts

**Test Coverage:**

| Test | Purpose | What It Proves |
|------|---------|----------------|
| `SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically` | Happy path atomic write | Policies written to 3 schemas in single transaction |
| `MultiContextSetup_WithSharedConnection_ShouldShareSamePhysicalConnection` | Connection sharing | Reference equality check confirms shared DbConnection object |
| `SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts` | Rollback on severe failure | Missing table in one context rolls back all contexts |
| `MultipleSaveOperations_WithSharedConnection_ShouldMaintainDataConsistency` | Consistency over time | Multiple incremental saves maintain integrity |
| `SavePolicy_WithSeparateConnections_ShouldNotBeAtomic` | **Negative test** | Proves separate connections do NOT provide atomicity |
| `SavePolicy_ShouldReflectDatabaseStateNotCasbinMemory` | Database verification | Tests verify actual database state, not just Casbin memory |

**Running Integration Tests:**

```bash
# Run all integration tests locally
dotnet test --filter "Category=Integration"

# Run specific test
dotnet test --filter "FullyQualifiedName~SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically"
```

**Note:** Integration tests are excluded from CI/CD (marked with `[Trait("Category", "Integration")]`) as they:
- Require local PostgreSQL database
- Take longer to execute than unit tests
- Are specific to multi-context functionality validation

### Test Architecture

**Setup:**
- Uses local PostgreSQL database for testing (database `casbin_integration_test` must exist)
- Creates 3 separate schemas: `casbin_policies`, `casbin_groupings`, `casbin_roles`
- Routes policy types: p → policies, g → groupings, g2 → roles
- Simulates real multi-context scenarios

**Prerequisites to run integration tests:**
- PostgreSQL running on localhost:5432
- Database `casbin_integration_test` must exist (schemas created automatically)
- Connection credentials: postgres/postgres4all! (or update ConnectionString in fixture)

**Failure Simulation:**
- Duplicate key violations (via direct SQL INSERT)
- Missing tables (via DROP TABLE)
- Separate connection objects (to prove non-atomicity)

**Verification:**
- Raw SQL queries to count policies in each schema
- Reference equality checks on DbConnection objects
- Database state verification (not just Casbin in-memory state)

## Status

**Implementation:** ✅ Complete
**Testing:** ✅ All 120 unit tests passing (30 tests × 4 frameworks) + 7 integration tests
**Documentation:** ✅ Complete
**Breaking Changes:** None - fully backward compatible

## See Also

- [MULTI_CONTEXT_USAGE_GUIDE.md](MULTI_CONTEXT_USAGE_GUIDE.md) - Step-by-step user guide
- [ICasbinDbContextProvider Interface](Casbin.Persist.Adapter.EFCore/ICasbinDbContextProvider.cs) - Interface source code
- [EFCoreAdapter Implementation](Casbin.Persist.Adapter.EFCore/EFCoreAdapter.cs) - Adapter source code
- [TransactionIntegrityTests.cs](../Casbin.Persist.Adapter.EFCore.UnitTest/Integration/TransactionIntegrityTests.cs) - Integration test suite
