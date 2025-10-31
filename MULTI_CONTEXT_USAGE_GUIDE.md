# Multi-Context Support Usage Guide

## Overview

Multi-context support allows you to store different Casbin policy types in separate database locations while maintaining a unified authorization model.

**Use cases:**
- Store policy rules (p, p2) and role assignments (g, g2) in separate schemas
- Apply different retention policies per policy type
- Separate concerns in multi-tenant systems

**How it works:**
- Each `CasbinDbContext` targets a different schema, table, or database
- A context provider routes policy types to the appropriate context
- The adapter automatically coordinates operations across all contexts

## Quick Start

### Step 1: Create Database Contexts

Create separate `CasbinDbContext` instances for different storage locations.

**⚠️ IMPORTANT for transaction guarantees:** To ensure atomic operations across contexts:
1. All contexts must use the **same connection string** (same database)
2. The database must support `UseTransaction()` to share a physical connection object
3. **Same connection string alone is not enough** - the database must be able to coordinate transactions

**Example: SQL Server with different schemas**

```csharp
using Microsoft.EntityFrameworkCore;
using Casbin.Persist.Adapter.EFCore;

// Define connection string once - REQUIRED for atomic transactions
string connectionString = "Server=localhost;Database=CasbinDB;Trusted_Connection=True;";

// Policy context - "policies" schema
var policyContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer(connectionString)  // Same connection string = atomic transactions
        .Options,
    schemaName: "policies");
policyContext.Database.EnsureCreated();

// Grouping context - "groupings" schema
var groupingContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer(connectionString)  // Same connection string = atomic transactions
        .Options,
    schemaName: "groupings");
groupingContext.Database.EnsureCreated();
```

**Other configuration options:**

| Option | Use Case | Example |
|--------|----------|---------|
| **Different schemas** | SQL Server, PostgreSQL | `schemaName: "policies"` vs `schemaName: "groupings"` |
| **Different tables** | Any database | `tableName: "casbin_policy"` vs `tableName: "casbin_grouping"` |
| **Separate databases** | Testing only | `UseSqlite("policy.db")` vs `UseSqlite("grouping.db")` ⚠️ Not atomic |

### Step 2: Implement Context Provider

Create a provider that routes policy types to contexts:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Casbin.Persist.Adapter.EFCore;

public class PolicyTypeContextProvider : ICasbinDbContextProvider<int>
{
    private readonly CasbinDbContext<int> _policyContext;
    private readonly CasbinDbContext<int> _groupingContext;

    public PolicyTypeContextProvider(
        CasbinDbContext<int> policyContext,
        CasbinDbContext<int> groupingContext)
    {
        _policyContext = policyContext ?? throw new ArgumentNullException(nameof(policyContext));
        _groupingContext = groupingContext ?? throw new ArgumentNullException(nameof(groupingContext));
    }

    public DbContext GetContextForPolicyType(string policyType)
    {
        if (string.IsNullOrEmpty(policyType))
            return _policyContext;

        // Route: p/p2/p3 → policyContext, g/g2/g3 → groupingContext
        return policyType.StartsWith("p", StringComparison.OrdinalIgnoreCase)
            ? _policyContext
            : _groupingContext;
    }

    public IEnumerable<DbContext> GetAllContexts()
    {
        return new DbContext[] { _policyContext, _groupingContext };
    }
}
```

**Policy type routing:**

| Policy Type | Context | Description |
|-------------|---------|-------------|
| `p`, `p2`, `p3`, ... | policyContext | Permission rules |
| `g`, `g2`, `g3`, ... | groupingContext | Role/group assignments |

### Step 3-4: Create Adapter and Enforcer

```csharp
// Create provider
var provider = new PolicyTypeContextProvider(policyContext, groupingContext);

// Create adapter with multi-context support
var adapter = new EFCoreAdapter<int>(provider);

// Create enforcer (multi-context behavior is transparent)
var enforcer = new Enforcer("path/to/model.conf", adapter);
enforcer.LoadPolicy();
```

### Step 5: Use Normally

```csharp
// Add policies (automatically routed to correct contexts)
enforcer.AddPolicy("alice", "data1", "read");        // → policyContext
enforcer.AddGroupingPolicy("alice", "admin");        // → groupingContext

// Save (coordinated across both contexts)
enforcer.SavePolicy();

// Check permissions (combines data from both contexts)
bool allowed = enforcer.Enforce("alice", "data1", "read");
```

### Complete Example

```csharp
using Microsoft.EntityFrameworkCore;
using NetCasbin;
using Casbin.Persist.Adapter.EFCore;

public class Program
{
    public static void Main()
    {
        // 1. Create contexts with same connection string
        string connectionString = "Server=localhost;Database=CasbinDB;Trusted_Connection=True;";

        var policyContext = new CasbinDbContext<int>(
            new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlServer(connectionString).Options,
            schemaName: "policies");
        policyContext.Database.EnsureCreated();

        var groupingContext = new CasbinDbContext<int>(
            new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlServer(connectionString).Options,
            schemaName: "groupings");
        groupingContext.Database.EnsureCreated();

        // 2. Create provider (use implementation from Step 2)
        var provider = new PolicyTypeContextProvider(policyContext, groupingContext);

        // 3. Create adapter and enforcer
        var adapter = new EFCoreAdapter<int>(provider);
        var enforcer = new Enforcer("rbac_model.conf", adapter);

        // 4. Use enforcer
        enforcer.AddPolicy("alice", "data1", "read");
        enforcer.AddGroupingPolicy("alice", "admin");
        enforcer.SavePolicy();

        bool allowed = enforcer.Enforce("alice", "data1", "read");
        Console.WriteLine($"Alice can read data1: {allowed}");
    }
}
```

## Configuration Reference

### Async Operations

All operations have async variants:

```csharp
await enforcer.AddPolicyAsync("alice", "data1", "read");
await enforcer.AddGroupingPolicyAsync("alice", "admin");
await enforcer.SavePolicyAsync();
await enforcer.LoadPolicyAsync();
```

### Filtered Loading

Load subsets of policies across all contexts:

```csharp
enforcer.LoadFilteredPolicy(new Filter
{
    P = new[] { "alice", "", "" },  // Only Alice's policies
    G = new[] { "alice", "" }        // Only Alice's groupings
});
```

### Dependency Injection

For ASP.NET Core applications:

```csharp
// Use same connection string for all contexts
string connectionString = Configuration.GetConnectionString("Casbin");

services.AddSingleton(sp =>
{
    var policyCtx = new CasbinDbContext<int>(
        new DbContextOptionsBuilder<CasbinDbContext<int>>()
            .UseSqlServer(connectionString).Options,
        schemaName: "policies");

    var groupingCtx = new CasbinDbContext<int>(
        new DbContextOptionsBuilder<CasbinDbContext<int>>()
            .UseSqlServer(connectionString).Options,
        schemaName: "groupings");

    return new PolicyTypeContextProvider(policyCtx, groupingCtx);
});

services.AddSingleton<IAdapter>(sp =>
{
    var provider = sp.GetRequiredService<ICasbinDbContextProvider<int>>();
    return new EFCoreAdapter<int>(provider);
});

services.AddSingleton<IEnforcer>(sp =>
{
    var adapter = sp.GetRequiredService<IAdapter>();
    return new Enforcer("rbac_model.conf", adapter);
});
```

## Transaction Behavior

### Connection String Requirements

**For atomic transactions across contexts, all contexts MUST share the same connection string.**

#### ✅ Correct: Shared Connection String

```csharp
// Define once, use everywhere
string connectionString = "Server=localhost;Database=CasbinDB;Trusted_Connection=True;";

var policyContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer(connectionString)  // ← Same variable
        .Options,
    schemaName: "policies");

var groupingContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer(connectionString)  // ← Same variable = atomic
        .Options,
    schemaName: "groupings");
```

**How it works:**
1. You provide contexts with matching connection strings
2. Adapter detects compatibility via `CanShareTransaction()`
3. Adapter uses `UseTransaction()` internally to coordinate
4. Database ensures atomic commit/rollback across both contexts

#### ❌ Incorrect: Hard-Coded Strings

```csharp
// WRONG: Even if strings look identical, they might differ slightly
var policyContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer("Server=localhost;Database=CasbinDB;...")  // ← Typed separately
        .Options);

var groupingContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer("Server=localhost;Database=CasbinDB;...")  // ← Not same string instance
        .Options);
```

### Context Factory Pattern (Recommended)

```csharp
public class CasbinContextFactory
{
    private readonly string _connectionString;

    public CasbinContextFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Casbin");
    }

    public CasbinDbContext<int> CreateContext(string schemaName)
    {
        var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
            .UseSqlServer(_connectionString)  // Guaranteed same string
            .Options;
        return new CasbinDbContext<int>(options, schemaName: schemaName);
    }
}

// Usage
var factory = new CasbinContextFactory(configuration);
var policyContext = factory.CreateContext("policies");
var groupingContext = factory.CreateContext("groupings");
// Both contexts guaranteed to share connection string
```

### Database Compatibility

| Database | Atomic Transactions | Connection Requirement | Notes |
|----------|-------------------|----------------------|-------|
| **SQL Server** | ✅ Yes | Same connection string | Works with different schemas/tables |
| **PostgreSQL** | ✅ Yes | Same connection string | Works with different schemas/tables |
| **MySQL** | ✅ Yes | Same connection string | Works with different schemas/tables |
| **SQLite (same file)** | ✅ Yes | Same file path | Different table names only |
| **SQLite (different files)** | ❌ No | N/A | Cannot share transactions |

### Responsibility Matrix

| Task | Your Responsibility | Adapter Responsibility |
|------|-------------------|----------------------|
| Provide same connection string | ✅ YES | ❌ NO |
| Use context factory pattern | ✅ YES (recommended) | ❌ NO |
| Understand database limitations | ✅ YES | ❌ NO |
| Call `UseTransaction()` | ❌ NO | ✅ YES (internal) |
| Detect connection compatibility | ❌ NO | ✅ YES |
| Coordinate commit/rollback | ❌ NO | ✅ YES |

### When Separate Connections Are Acceptable

**Non-atomic behavior (individual transactions per context) may be acceptable for:**
- Testing and development
- Read-heavy workloads with eventual consistency
- Non-critical data

**Not acceptable for:**
- Production ACID requirements (financial, authorization)
- Compliance/audit scenarios
- Multi-tenant SaaS with strict data integrity

## Troubleshooting

### "No such table" errors

**Cause:** Database tables not created.

**Solution:**
```csharp
policyContext.Database.EnsureCreated();
groupingContext.Database.EnsureCreated();
```

### Partial data committed on failure

**Cause:** Using separate database connections (e.g., different SQLite files).

**Solution:** Use same database with different schemas/tables:
```csharp
// Instead of separate files
.UseSqlite("Data Source=policy.db")
.UseSqlite("Data Source=grouping.db")

// Use same file with different tables
.UseSqlite("Data Source=casbin.db")  // Both use same file
// Configure different table names
```

### Transaction warnings in logs

**Cause:** Adapter detected different connection strings and fell back to individual transactions.

**Solution:** Ensure all contexts use the same connection string variable (see [Context Factory Pattern](#context-factory-pattern-recommended)).

## See Also

- [MULTI_CONTEXT_DESIGN.md](MULTI_CONTEXT_DESIGN.md) - Technical architecture and implementation details
- [Casbin.NET Documentation](https://casbin.org/docs/overview) - Casbin concepts and model syntax
- [ICasbinDbContextProvider Interface](Casbin.Persist.Adapter.EFCore/ICasbinDbContextProvider.cs) - Interface definition
