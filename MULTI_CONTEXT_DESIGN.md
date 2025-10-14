# Multi-Context Support Design Document

## Overview

This document outlines the design for supporting multiple `CasbinDbContext` instances in the EFCore adapter, allowing different policy types to be stored in separate database contexts (e.g., different schemas or tables) while maintaining transactional integrity.

## Background

### Current Architecture
- Single `DbContext` per adapter instance
- Single `DbSet<TPersistPolicy>` for all policy types
- All policy types (p, p2, g, g2, etc.) stored in the same database table
- Policy operations receive `section` and `policyType` parameters but don't use them for context routing

### Motivation
Users may want to:
- Store different policy types in separate database schemas
- Use different tables for policies vs groupings
- Separate concerns for multi-tenant scenarios
- Apply different retention/archival strategies per policy type

## Requirements

### Functional Requirements
1. Support routing policy types to different `DbContext` instances
2. Maintain ACID transaction guarantees across all contexts
3. Preserve backward compatibility - existing code must work unchanged
4. Support both sync and async operations
5. Allow flexible routing logic defined by users

### Technical Requirements
1. All contexts must connect to the **same database** (same connection string)
2. Contexts may target different schemas within that database
3. Use EF Core's `UseTransaction()` to share transactions across contexts
4. Support all existing adapter operations: Load, Save, Add, Remove, Update, Filter

### Non-Requirements
- Distributed transactions across different databases/servers
- Automatic connection string management
- Schema migration coordination

## Design

### Solution: Context Provider Pattern

#### Core Interface

```csharp
public interface ICasbinDbContextProvider<TKey> where TKey : IEquatable<TKey>
{
    /// <summary>
    /// Gets the DbContext for a specific policy type (e.g., "p", "p2", "g", "g2")
    /// </summary>
    /// <param name="policyType">The policy type identifier</param>
    /// <returns>DbContext instance that should handle this policy type</returns>
    DbContext GetContextForPolicyType(string policyType);

    /// <summary>
    /// Gets all unique DbContext instances used by this provider.
    /// Used for operations that need to coordinate across all contexts (e.g., SavePolicy, LoadPolicy)
    /// </summary>
    /// <returns>Enumerable of all distinct contexts</returns>
    IEnumerable<DbContext> GetAllContexts();
}
```

#### Default Implementation

```csharp
/// <summary>
/// Default provider that uses a single context for all policy types (current behavior)
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

#### Example Custom Implementation

```csharp
/// <summary>
/// Example: Route 'p' type policies to one schema, 'g' type policies to another
/// </summary>
public class PolicyTypeContextProvider : ICasbinDbContextProvider<int>
{
    private readonly CasbinDbContext<int> _policyContext;
    private readonly CasbinDbContext<int> _groupingContext;

    public PolicyTypeContextProvider(
        CasbinDbContext<int> policyContext,
        CasbinDbContext<int> groupingContext)
    {
        _policyContext = policyContext;
        _groupingContext = groupingContext;
    }

    public DbContext GetContextForPolicyType(string policyType)
    {
        // Route p/p2/p3 to policy context, g/g2/g3 to grouping context
        return policyType.StartsWith("p") ? _policyContext : _groupingContext;
    }

    public IEnumerable<DbContext> GetAllContexts()
    {
        return new DbContext[] { _policyContext, _groupingContext };
    }
}
```

### Constructor Changes

Add new constructor overload to `EFCoreAdapter<TKey, TPersistPolicy, TDbContext>`:

```csharp
public partial class EFCoreAdapter<TKey, TPersistPolicy, TDbContext>
{
    private readonly ICasbinDbContextProvider<TKey> _contextProvider;
    private readonly Dictionary<DbContext, DbSet<TPersistPolicy>> _persistPoliciesByContext;

    /// <summary>
    /// NEW: Creates adapter with custom context provider for multi-context scenarios
    /// </summary>
    public EFCoreAdapter(ICasbinDbContextProvider<TKey> contextProvider)
    {
        _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
        _persistPoliciesByContext = new Dictionary<DbContext, DbSet<TPersistPolicy>>();
        DbContext = null; // Kept for backward compatibility
    }

    /// <summary>
    /// EXISTING: Creates adapter with single context (unchanged behavior)
    /// </summary>
    public EFCoreAdapter(TDbContext context)
    {
        DbContext = context ?? throw new ArgumentNullException(nameof(context));
        _contextProvider = new SingleContextProvider<TKey>(context);
        _persistPoliciesByContext = new Dictionary<DbContext, DbSet<TPersistPolicy>>();
    }

    // Legacy property - kept for backward compatibility
    protected TDbContext DbContext { get; }
}
```

## Transaction Management

### EF Core Shared Transaction Pattern

EF Core provides `Database.UseTransaction()` to share a single database transaction across multiple `DbContext` instances:

```csharp
using var transaction = primaryContext.Database.BeginTransaction();
try
{
    // Save to primary context
    primaryContext.SaveChanges();

    // Enlist other contexts in the same transaction
    secondaryContext.Database.UseTransaction(transaction.GetDbTransaction());
    secondaryContext.SaveChanges();

    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

### Database Support

| Database | Same Schema | Different Schemas | Different Databases (Same Server) | Different Servers |
|----------|-------------|-------------------|-----------------------------------|-------------------|
| SQL Server | ✅ Local Tx | ✅ Local Tx | ✅ Local Tx (managed internally) | ❌ Requires DTC |
| PostgreSQL | ✅ Local Tx | ✅ Local Tx | ❌ Requires distributed tx | ❌ Requires distributed tx |
| MySQL | ✅ Local Tx | ✅ Local Tx (schema=database) | ❌ Requires distributed tx | ❌ Requires distributed tx |
| SQLite | ✅ Local Tx | N/A (no schemas) | ❌ Not supported | ❌ Not supported |

**Key Constraint:** All contexts must connect to the **same database** using the **same connection string**.

### Transaction Handling by Operation

#### 1. SavePolicy (Multi-Context)

Most complex operation - must coordinate across all contexts:

```csharp
public virtual void SavePolicy(IPolicyStore store)
{
    var persistPolicies = new List<TPersistPolicy>();
    persistPolicies.ReadPolicyFromCasbinModel(store);

    if (persistPolicies.Count is 0) return;

    // Group policies by their target context
    var policiesByContext = persistPolicies
        .GroupBy(p => _contextProvider.GetContextForPolicyType(p.Type))
        .ToList();

    var contexts = _contextProvider.GetAllContexts().Distinct().ToList();
    var primaryContext = contexts.First();

    // Begin transaction on primary context
    using var transaction = primaryContext.Database.BeginTransaction();

    try
    {
        // Clear existing policies from all contexts
        foreach (var context in contexts)
        {
            if (context != primaryContext)
            {
                context.Database.UseTransaction(transaction.GetDbTransaction());
            }

            var dbSet = GetCasbinRuleDbSet(context, null);
            var existingRules = dbSet.ToList();
            dbSet.RemoveRange(existingRules);
            context.SaveChanges();
        }

        // Add new policies to respective contexts
        foreach (var group in policiesByContext)
        {
            var context = group.Key;
            var dbSet = GetCasbinRuleDbSet(context, null);
            var saveRules = OnSavePolicy(store, group);
            dbSet.AddRange(saveRules);
            context.SaveChanges();
        }

        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

#### 2. AddPolicy (Single Context)

Simple case - single policy type maps to single context:

```csharp
public virtual void AddPolicy(string section, string policyType, IPolicyValues values)
{
    if (values.Count is 0) return;

    var context = _contextProvider.GetContextForPolicyType(policyType);
    var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);

    var filter = new PolicyFilter(policyType, 0, values);
    if (filter.Apply(dbSet).Any()) return;

    InternalAddPolicy(context, section, policyType, values);
    context.SaveChanges();
}
```

#### 3. UpdatePolicy (Single Context with Transaction)

Old and new policy must be same type (same context):

```csharp
public void UpdatePolicy(string section, string policyType,
    IPolicyValues oldValues, IPolicyValues newValues)
{
    if (newValues.Count is 0) return;

    var context = _contextProvider.GetContextForPolicyType(policyType);
    using var transaction = context.Database.BeginTransaction();

    try
    {
        InternalUpdatePolicy(context, section, policyType, oldValues, newValues);
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

#### 4. LoadPolicy (Multi-Context, Read-Only)

No transaction needed for read-only operations:

```csharp
public virtual void LoadPolicy(IPolicyStore store)
{
    var allPolicies = new List<TPersistPolicy>();

    // Load from each unique context
    foreach (var context in _contextProvider.GetAllContexts().Distinct())
    {
        var dbSet = GetCasbinRuleDbSet(context, null);
        var policies = dbSet.AsNoTracking().ToList();
        allPolicies.AddRange(policies);
    }

    var filteredPolicies = OnLoadPolicy(store, allPolicies.AsQueryable());
    store.LoadPolicyFromPersistPolicy(filteredPolicies.ToList());
    IsFiltered = false;
}
```

#### 5. LoadFilteredPolicy (Multi-Context, Read-Only)

```csharp
public void LoadFilteredPolicy(IPolicyStore store, IPolicyFilter filter)
{
    var allPolicies = new List<TPersistPolicy>();

    foreach (var context in _contextProvider.GetAllContexts().Distinct())
    {
        var dbSet = GetCasbinRuleDbSet(context, null);
        var policies = dbSet.AsNoTracking();
        var filtered = filter.Apply(policies);
        allPolicies.AddRange(filtered.ToList());
    }

    var finalPolicies = OnLoadPolicy(store, allPolicies.AsQueryable());
    store.LoadPolicyFromPersistPolicy(finalPolicies.ToList());
    IsFiltered = true;
}
```

## Internal Method Changes

### Modified Virtual Method Signatures

```csharp
// Old signature - kept for backward compatibility, marked obsolete
[Obsolete("Use GetCasbinRuleDbSet(DbContext, string) instead")]
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

### New Helper Methods

```csharp
/// <summary>
/// Gets or caches the DbSet for a specific context and policy type
/// </summary>
private DbSet<TPersistPolicy> GetCasbinRuleDbSetForPolicyType(DbContext context, string policyType)
{
    if (!_persistPoliciesByContext.TryGetValue(context, out var dbSet))
    {
        dbSet = GetCasbinRuleDbSet(context, policyType);
        _persistPoliciesByContext[context] = dbSet;
    }
    return dbSet;
}
```

## Usage Examples

### Example 1: Separate Schemas for Policies and Groupings

```csharp
// Setup: Create contexts pointing to different schemas
var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlServer("Server=localhost;Database=CasbinDB;...")
    .Options;
var policyContext = new CasbinDbContext<int>(policyOptions, schemaName: "policies");

var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlServer("Server=localhost;Database=CasbinDB;...")  // Same database!
    .Options;
var groupingContext = new CasbinDbContext<int>(groupingOptions, schemaName: "groupings");

// Create custom provider
var provider = new PolicyTypeContextProvider(policyContext, groupingContext);

// Use adapter with multi-context support
var adapter = new EFCoreAdapter<int>(provider);
var enforcer = new Enforcer("model.conf", adapter);

// All operations are atomic across both schemas
enforcer.AddPolicy("alice", "data1", "read");      // Goes to policies schema
enforcer.AddGroupingPolicy("alice", "admin");      // Goes to groupings schema
enforcer.SavePolicy();                              // Atomic across both schemas
```

### Example 2: Backward Compatible (Single Context)

```csharp
// Existing code continues to work unchanged
var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlite("Data Source=casbin.db")
    .Options;
var context = new CasbinDbContext<int>(options);

// Single context constructor - uses SingleContextProvider internally
var adapter = new EFCoreAdapter<int>(context);
var enforcer = new Enforcer("model.conf", adapter);

// Everything works exactly as before
enforcer.AddPolicy("alice", "data1", "read");
```

### Example 3: Custom Routing Logic

```csharp
public class TenantAwareContextProvider : ICasbinDbContextProvider<int>
{
    private readonly Dictionary<string, CasbinDbContext<int>> _contextsByTenant;

    public DbContext GetContextForPolicyType(string policyType)
    {
        // Extract tenant from policy type (e.g., "p_tenant1", "g_tenant2")
        var parts = policyType.Split('_');
        var tenant = parts.Length > 1 ? parts[1] : "default";

        return _contextsByTenant[tenant];
    }

    public IEnumerable<DbContext> GetAllContexts() => _contextsByTenant.Values;
}
```

## Schema Support in CasbinDbContext

The existing `CasbinDbContext<TKey>` already supports schema configuration:

```csharp
// Constructor accepts optional schemaName parameter
public CasbinDbContext(DbContextOptions<CasbinDbContext<TKey>> options,
    string schemaName = null,
    string tableName = DefaultTableName) : base(options)
{
    _casbinModelConfig = new DefaultPersistPolicyEntityTypeConfiguration<TKey>(tableName);
    _schemaName = schemaName;
}

// Applied in OnModelCreating
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    if (string.IsNullOrWhiteSpace(_schemaName) is false)
    {
        modelBuilder.HasDefaultSchema(_schemaName);
    }

    if (_casbinModelConfig is not null)
    {
        modelBuilder.ApplyConfiguration(_casbinModelConfig);
    }
}
```

This means users can already create contexts targeting different schemas without any code changes.

## Implementation Checklist

1. ✅ **Design Phase** (Current)
   - [x] Define interfaces and contracts
   - [x] Document transaction handling strategy
   - [x] Validate database support across providers
   - [x] Create usage examples

2. **Implementation Phase**
   - [ ] Create `ICasbinDbContextProvider<TKey>` interface
   - [ ] Create `SingleContextProvider<TKey>` default implementation
   - [ ] Add `_contextProvider` field to `EFCoreAdapter`
   - [ ] Add new constructor accepting context provider
   - [ ] Add `_persistPoliciesByContext` dictionary for caching
   - [ ] Modify `GetCasbinRuleDbSet()` signature to include `policyType`
   - [ ] Update `LoadPolicy()` to work with multiple contexts
   - [ ] Update `SavePolicy()` to work with multiple contexts and shared transactions
   - [ ] Update `AddPolicy()` to route to correct context
   - [ ] Update `RemovePolicy()` to route to correct context
   - [ ] Update `UpdatePolicy()` to use correct context with transaction
   - [ ] Update `AddPolicies()`, `RemovePolicies()`, `UpdatePolicies()` batch operations
   - [ ] Update `LoadFilteredPolicy()` to work with multiple contexts
   - [ ] Update all async variants of above methods
   - [ ] Update all internal helper methods in `EFCoreAdapter.Internal.cs`

3. **Testing Phase**
   - [ ] Test single context (backward compatibility)
   - [ ] Test multi-context with same schema
   - [ ] Test multi-context with different schemas (SQL Server)
   - [ ] Test multi-context with different schemas (PostgreSQL)
   - [ ] Test multi-context transaction rollback scenarios
   - [ ] Test all CRUD operations with multi-context
   - [ ] Test filtered policy loading with multi-context
   - [ ] Test batch operations with multi-context
   - [ ] Test error handling and transaction failures
   - [ ] Test with SQLite (single schema only)

4. **Documentation Phase**
   - [ ] Update CLAUDE.md with multi-context usage
   - [ ] Add XML documentation comments to new interfaces
   - [ ] Create migration guide for existing users
   - [ ] Add examples to README.md
   - [ ] Document limitations and constraints

## Breaking Changes

**None** - All existing constructors and public APIs remain unchanged. The feature is purely additive.

## Benefits

1. ✅ **ACID Guarantees** - All operations are atomic across multiple contexts
2. ✅ **No Distributed Transactions** - Uses single database transaction via `UseTransaction()`
3. ✅ **Connection Efficiency** - Reuses same connection across contexts
4. ✅ **Backward Compatible** - Existing code works unchanged
5. ✅ **Flexible Routing** - Users define custom logic for policy type routing
6. ✅ **Schema Separation** - Supports different schemas in same database
7. ✅ **Multi-Tenancy Support** - Enables tenant-specific context routing

## Limitations

1. ❌ **Same Database Only** - All contexts must connect to the same database
2. ❌ **No Cross-Server** - Cannot span multiple database servers
3. ❌ **Relational Databases Only** - Requires `DbTransaction` support
4. ⚠️ **Performance** - Multiple contexts may have slight overhead for context switching
5. ⚠️ **Schema Management** - Users responsible for creating/migrating multiple schemas

## Questions for Implementation

1. Should we validate that all contexts use the same connection string at runtime?
2. Should we provide a built-in `SchemaBasedContextProvider` for common use cases?
3. Should error messages include guidance when contexts point to different databases?
4. Should we add metrics/logging for multi-context operations?
5. Should `policyType` be passed to existing virtual methods like `OnAddPolicy` for further customization?

---

**Document Version:** 1.0
**Last Updated:** 2025-10-14
**Status:** Design Complete - Awaiting Approval for Implementation
