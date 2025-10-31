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

Create separate `CasbinDbContext` instances that **share the same physical DbConnection object**.

**⚠️ CRITICAL - Shared Connection Requirement:**

For atomic transactions across contexts, you MUST pass the **same DbConnection object instance** to all contexts. EF Core's `UseTransaction()` requires reference equality of connection objects, not just matching connection strings.

**✅ CORRECT: Share physical DbConnection object**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;  // or Npgsql.NpgsqlConnection, etc.
using Casbin.Persist.Adapter.EFCore;

// Create ONE shared connection object
string connectionString = "Server=localhost;Database=CasbinDB;Trusted_Connection=True;";
var sharedConnection = new SqlConnection(connectionString);

// Pass SAME connection instance to both contexts
var policyContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer(sharedConnection)  // ← Shared connection object
        .Options,
    schemaName: "policies");
policyContext.Database.EnsureCreated();

var groupingContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer(sharedConnection)  // ← Same connection object
        .Options,
    schemaName: "groupings");
groupingContext.Database.EnsureCreated();
```

**❌ WRONG: This will NOT provide atomic transactions**

```csharp
// Each .UseSqlServer(connectionString) creates a DIFFERENT DbConnection object
var policyContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer(connectionString)  // ← Creates DbConnection #1
        .Options);

var groupingContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer(connectionString)  // ← Creates DbConnection #2 (different object!)
        .Options);

// These contexts have different connection objects, so they CANNOT share transactions
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
using Microsoft.Data.SqlClient;
using NetCasbin;
using Casbin.Persist.Adapter.EFCore;

public class Program
{
    public static void Main()
    {
        // 1. Create shared connection object
        string connectionString = "Server=localhost;Database=CasbinDB;Trusted_Connection=True;";
        var sharedConnection = new SqlConnection(connectionString);

        // 2. Create contexts with shared connection
        var policyContext = new CasbinDbContext<int>(
            new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlServer(sharedConnection).Options,  // ← Shared connection
            schemaName: "policies");
        policyContext.Database.EnsureCreated();

        var groupingContext = new CasbinDbContext<int>(
            new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlServer(sharedConnection).Options,  // ← Same connection
            schemaName: "groupings");
        groupingContext.Database.EnsureCreated();

        // 3. Create provider (use implementation from Step 2)
        var provider = new PolicyTypeContextProvider(policyContext, groupingContext);

        // 4. Create adapter and enforcer
        var adapter = new EFCoreAdapter<int>(provider);
        var enforcer = new Enforcer("rbac_model.conf", adapter);

        // 5. Use enforcer (atomic transactions across both contexts)
        enforcer.AddPolicy("alice", "data1", "read");
        enforcer.AddGroupingPolicy("alice", "admin");
        enforcer.SavePolicy();

        bool allowed = enforcer.Enforce("alice", "data1", "read");
        Console.WriteLine($"Alice can read data1: {allowed}");

        // 6. Cleanup
        sharedConnection.Dispose();
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

For ASP.NET Core applications with shared connection:

```csharp
// Register shared connection as singleton
services.AddSingleton<DbConnection>(sp =>
{
    var connectionString = Configuration.GetConnectionString("Casbin");
    return new SqlConnection(connectionString);
});

// Register context provider with shared connection
services.AddSingleton<ICasbinDbContextProvider<int>>(sp =>
{
    var sharedConnection = sp.GetRequiredService<DbConnection>();

    var policyCtx = new CasbinDbContext<int>(
        new DbContextOptionsBuilder<CasbinDbContext<int>>()
            .UseSqlServer(sharedConnection).Options,  // Shared connection
        schemaName: "policies");

    var groupingCtx = new CasbinDbContext<int>(
        new DbContextOptionsBuilder<CasbinDbContext<int>>()
            .UseSqlServer(sharedConnection).Options,  // Same connection
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

### Connection Lifetime Management

**Important:** When using shared connections, you are responsible for connection lifetime:

**In simple applications:**
```csharp
// Create connection
var connection = new SqlConnection(connectionString);

// Use for contexts/adapter/enforcer
// ... (create contexts, adapter, enforcer)

// Dispose when done
connection.Dispose();
```

**With using statement:**
```csharp
using (var connection = new SqlConnection(connectionString))
{
    // Create contexts with shared connection
    var policyCtx = new CasbinDbContext<int>(...);
    var groupingCtx = new CasbinDbContext<int>(...);

    // Create and use enforcer
    var provider = new PolicyTypeContextProvider(policyCtx, groupingCtx);
    var adapter = new EFCoreAdapter<int>(provider);
    var enforcer = new Enforcer("model.conf", adapter);

    enforcer.LoadPolicy();
    // ... use enforcer

} // Connection disposed automatically
```

**In DI scenarios:**

The DbConnection is registered as a singleton and will be disposed when the application shuts down. No manual disposal needed in request handlers.

## Transaction Behavior

### Shared Connection Requirements

**For atomic transactions across contexts, all contexts MUST share the same DbConnection object instance.**

**How atomic transactions work:**
1. You create ONE DbConnection object and pass it to all contexts
2. Adapter detects shared connection via `CanShareTransaction()` (reference equality check)
3. Adapter uses `UseTransaction()` to enlist all contexts in one transaction
4. Database ensures atomic commit/rollback across both contexts

**✅ CORRECT Example:**

Already shown in Step 1 - create shared DbConnection and pass to all contexts.

### Context Factory Pattern (Recommended)

```csharp
public class CasbinContextFactory : IDisposable
{
    private readonly DbConnection _sharedConnection;

    public CasbinContextFactory(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Casbin");
        _sharedConnection = new SqlConnection(connectionString);  // Create shared connection once
    }

    public CasbinDbContext<int> CreateContext(string schemaName)
    {
        var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
            .UseSqlServer(_sharedConnection)  // ← Share same connection object
            .Options;
        return new CasbinDbContext<int>(options, schemaName: schemaName);
    }

    public void Dispose()
    {
        _sharedConnection?.Dispose();
    }
}

// Usage
using var factory = new CasbinContextFactory(configuration);
var policyContext = factory.CreateContext("policies");
var groupingContext = factory.CreateContext("groupings");
// Both contexts share the same physical connection object
```

### Database Compatibility

| Database | Atomic Transactions | Connection Requirement | Notes |
|----------|-------------------|----------------------|-------|
| **SQL Server** | ✅ Yes | Same DbConnection object | Works with different schemas/tables |
| **PostgreSQL** | ✅ Yes | Same DbConnection object | Works with different schemas/tables |
| **MySQL** | ✅ Yes | Same DbConnection object | Works with different schemas/tables |
| **SQLite** | ✅ Yes | Same DbConnection object | Works with different tables in same file |

**Note:** "Same database" requires **same DbConnection object instance**, not just matching connection strings.

### Responsibility Matrix

| Task | Your Responsibility | Adapter Responsibility |
|------|-------------------|----------------------|
| Create shared DbConnection object | ✅ YES | ❌ NO |
| Pass same connection to all contexts | ✅ YES | ❌ NO |
| Manage connection lifetime | ✅ YES | ❌ NO |
| Use context factory pattern | ✅ YES (recommended) | ❌ NO |
| Call `UseTransaction()` | ❌ NO | ✅ YES (internal) |
| Detect shared connection (reference equality) | ❌ NO | ✅ YES |
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
