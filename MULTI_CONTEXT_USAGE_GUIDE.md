# Multi-Context Enforcer Setup Guide

This guide shows you how to build a Casbin enforcer that uses **multiple database contexts** to store different policy types separately.

## Overview

In a multi-context setup:
- **Policy rules** (p, p2, p3, etc.) go to one database context
- **Grouping rules** (g, g2, g3, etc.) go to another database context
- Each context can point to different schemas, tables, or even separate databases

## Step-by-Step Guide

### Step 1: Create Your Database Contexts

Create two separate `CasbinDbContext` instances, each configured for a different storage location.

#### Option A: Different Schemas (SQL Server, PostgreSQL)

```csharp
using Microsoft.EntityFrameworkCore;
using Casbin.Persist.Adapter.EFCore;

// Context for policy rules - stores in "policies" schema
var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlServer("Server=localhost;Database=CasbinDB;Trusted_Connection=True;")
    .Options;
var policyContext = new CasbinDbContext<int>(
    policyOptions,
    schemaName: "policies",  // Custom schema
    tableName: "casbin_rule" // Standard table name
);
policyContext.Database.EnsureCreated();

// Context for grouping rules - stores in "groupings" schema
var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlServer("Server=localhost;Database=CasbinDB;Trusted_Connection=True;")
    .Options;
var groupingContext = new CasbinDbContext<int>(
    groupingOptions,
    schemaName: "groupings", // Different schema
    tableName: "casbin_rule" // Same table name, different schema
);
groupingContext.Database.EnsureCreated();
```

#### Option B: Different Tables (Same Database)

```csharp
// Context for policy rules - stores in "casbin_policy" table
var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlite("Data Source=casbin.db")
    .Options;
var policyContext = new CasbinDbContext<int>(
    policyOptions,
    tableName: "casbin_policy" // Custom table name
);
policyContext.Database.EnsureCreated();

// Context for grouping rules - stores in "casbin_grouping" table
var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlite("Data Source=casbin.db") // Same database file
    .Options;
var groupingContext = new CasbinDbContext<int>(
    groupingOptions,
    tableName: "casbin_grouping" // Different table name
);
groupingContext.Database.EnsureCreated();
```

#### Option C: Separate Databases (Testing/Development)

```csharp
// Context for policy rules - separate database file
var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlite("Data Source=casbin_policy.db")
    .Options;
var policyContext = new CasbinDbContext<int>(policyOptions);
policyContext.Database.EnsureCreated();

// Context for grouping rules - separate database file
var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlite("Data Source=casbin_grouping.db")
    .Options;
var groupingContext = new CasbinDbContext<int>(groupingOptions);
groupingContext.Database.EnsureCreated();
```

⚠️ **Warning:** Separate databases cannot share transactions. See [Transaction Limitations](#transaction-limitations) below.

### Step 2: Implement the Context Provider

Create a class that implements `ICasbinDbContextProvider<TKey>` to route policy types to the correct context.

```csharp
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Casbin.Persist.Adapter.EFCore;

/// <summary>
/// Routes 'p' type policies to policyContext and 'g' type policies to groupingContext
/// </summary>
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

    /// <summary>
    /// Routes policy types to the appropriate context:
    /// - p, p2, p3, etc. → policyContext
    /// - g, g2, g3, etc. → groupingContext
    /// </summary>
    public DbContext GetContextForPolicyType(string policyType)
    {
        if (string.IsNullOrEmpty(policyType))
        {
            return _policyContext;
        }

        // Route based on first character
        return policyType.StartsWith("p", StringComparison.OrdinalIgnoreCase)
            ? _policyContext
            : _groupingContext;
    }

    /// <summary>
    /// Returns both contexts for operations that need all data
    /// </summary>
    public IEnumerable<DbContext> GetAllContexts()
    {
        return new DbContext[] { _policyContext, _groupingContext };
    }
}
```

### Step 3: Create the Adapter with the Provider

Pass your context provider to the adapter constructor:

```csharp
// Create the provider with both contexts
var provider = new PolicyTypeContextProvider(policyContext, groupingContext);

// Create the adapter using the multi-context provider
var adapter = new EFCoreAdapter<int>(provider);
```

### Step 4: Create the Enforcer

Create your enforcer as usual - the multi-context behavior is transparent:

```csharp
// Create enforcer with your model and the multi-context adapter
var enforcer = new Enforcer("path/to/model.conf", adapter);

// Load existing policies from both contexts
enforcer.LoadPolicy();
```

### Step 5: Use the Enforcer Normally

All operations work transparently across multiple contexts:

```csharp
// Add policy rules (automatically routed to policyContext)
enforcer.AddPolicy("alice", "data1", "read");
enforcer.AddPolicy("bob", "data2", "write");
enforcer.AddPolicy("charlie", "data3", "read");

// Add grouping rules (automatically routed to groupingContext)
enforcer.AddGroupingPolicy("alice", "admin");
enforcer.AddGroupingPolicy("bob", "user");

// Save all policies (coordinates across both contexts)
enforcer.SavePolicy();

// Check permissions (enforcer combines data from both contexts)
bool allowed = enforcer.Enforce("alice", "data1", "read"); // true

// Update/Remove work across contexts automatically
enforcer.RemovePolicy("charlie", "data3", "read");
enforcer.UpdatePolicy(
    new[] { "alice", "data1", "read" },
    new[] { "alice", "data1", "write" }
);
```

## Complete Example

Here's a complete working example:

```csharp
using Microsoft.EntityFrameworkCore;
using NetCasbin;
using Casbin.Persist.Adapter.EFCore;

public class Program
{
    public static void Main()
    {
        // Step 1: Create contexts
        var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
            .UseSqlServer("Server=localhost;Database=CasbinDB;Trusted_Connection=True;")
            .Options;
        var policyContext = new CasbinDbContext<int>(policyOptions, schemaName: "policies");
        policyContext.Database.EnsureCreated();

        var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
            .UseSqlServer("Server=localhost;Database=CasbinDB;Trusted_Connection=True;")
            .Options;
        var groupingContext = new CasbinDbContext<int>(groupingOptions, schemaName: "groupings");
        groupingContext.Database.EnsureCreated();

        // Step 2: Create provider
        var provider = new PolicyTypeContextProvider(policyContext, groupingContext);

        // Step 3: Create adapter
        var adapter = new EFCoreAdapter<int>(provider);

        // Step 4: Create enforcer
        var enforcer = new Enforcer("rbac_model.conf", adapter);

        // Step 5: Use enforcer
        enforcer.AddPolicy("alice", "data1", "read");
        enforcer.AddGroupingPolicy("alice", "admin");
        enforcer.SavePolicy();

        bool allowed = enforcer.Enforce("alice", "data1", "read");
        Console.WriteLine($"Alice can read data1: {allowed}");
    }
}

// PolicyTypeContextProvider implementation (from Step 2)
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
        return policyType?.StartsWith("p", StringComparison.OrdinalIgnoreCase) == true
            ? _policyContext
            : _groupingContext;
    }

    public IEnumerable<DbContext> GetAllContexts()
    {
        return new DbContext[] { _policyContext, _groupingContext };
    }
}
```

## Key Points

### Policy Type Routing

The provider routes policy types to contexts based on the **first character**:

| Policy Type | Context | Description |
|------------|---------|-------------|
| `p` | policyContext | Standard policy rule |
| `p2` | policyContext | Alternative policy rule |
| `p3`, `p4`, ... | policyContext | More policy variants |
| `g` | groupingContext | Standard role/group |
| `g2` | groupingContext | Alternative role/group |
| `g3`, `g4`, ... | groupingContext | More grouping variants |

**Multiple policy types per context:** Each context can handle multiple policy types (e.g., p, p2, p3 all go to the same context).

### Async Operations

All operations have async variants that work the same way:

```csharp
await enforcer.AddPolicyAsync("alice", "data1", "read");
await enforcer.AddGroupingPolicyAsync("alice", "admin");
await enforcer.SavePolicyAsync();
await enforcer.LoadPolicyAsync();
```

### Filtered Loading

Loading filtered policies works across all contexts:

```csharp
enforcer.LoadFilteredPolicy(new Filter
{
    P = new[] { "alice", "", "" },  // Only load Alice's policies
    G = new[] { "alice", "" }        // Only load Alice's groupings
});
```

## Transaction Limitations

### ✅ Same Connection = Atomic Transactions

When both contexts connect to the **same database** (same connection string):
- All operations are **fully atomic** (ACID guarantees)
- If one context fails, all contexts rollback
- Works with: SQL Server, PostgreSQL, MySQL (same database), SQLite (same file)

```csharp
// Both contexts use same database - ATOMIC
var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlServer("Server=localhost;Database=CasbinDB;...")
    .Options;
var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlServer("Server=localhost;Database=CasbinDB;...") // Same connection string
    .Options;
```

### ⚠️ Separate Connections = Individual Transactions

When contexts connect to **different databases/files**:
- Operations are **NOT atomic** across contexts
- Each context has its own transaction
- If one context fails, others may have already committed
- Acceptable for testing, not recommended for production

```csharp
// Separate SQLite files - NOT ATOMIC across contexts
var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlite("Data Source=policy.db")
    .Options;
var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
    .UseSqlite("Data Source=grouping.db") // Different database
    .Options;
```

**Recommendation:** For production, use the same database with different schemas or tables to maintain atomicity.

## Dependency Injection Setup

For ASP.NET Core or DI-based applications:

```csharp
services.AddDbContext<CasbinDbContext<int>>(options =>
    options.UseSqlServer(connectionString));

// Register contexts with different schemas
services.AddDbContext<CasbinDbContext<int>>("PolicyContext", options =>
    options.UseSqlServer(connectionString),
    contextOptions => new CasbinDbContext<int>(contextOptions, schemaName: "policies"));

services.AddDbContext<CasbinDbContext<int>>("GroupingContext", options =>
    options.UseSqlServer(connectionString),
    contextOptions => new CasbinDbContext<int>(contextOptions, schemaName: "groupings"));

// Register provider and adapter
services.AddSingleton<ICasbinDbContextProvider<int>>(sp =>
{
    var policyContext = sp.GetRequiredService<CasbinDbContext<int>>("PolicyContext");
    var groupingContext = sp.GetRequiredService<CasbinDbContext<int>>("GroupingContext");
    return new PolicyTypeContextProvider(policyContext, groupingContext);
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

## Troubleshooting

### Issue: "No such table" errors

**Cause:** Database tables not created before use.

**Solution:** Ensure `EnsureCreated()` is called on both contexts before creating the enforcer:

```csharp
policyContext.Database.EnsureCreated();
groupingContext.Database.EnsureCreated();
```

### Issue: "Transaction not associated with connection"

**Cause:** Contexts are using different database connections (e.g., separate SQLite files).

**Solution:** The adapter automatically handles this by using individual transactions per context. This is expected behavior for separate databases.

### Issue: Partial data committed on failure

**Cause:** Using separate database connections without atomic transactions.

**Solution:** Use the same database with different schemas/tables instead:

```csharp
// Instead of separate files
.UseSqlite("Data Source=policy.db")
.UseSqlite("Data Source=grouping.db")

// Use same file with different tables
.UseSqlite("Data Source=casbin.db") // Both use same file
```

## See Also

- [MULTI_CONTEXT_DESIGN.md](MULTI_CONTEXT_DESIGN.md) - Detailed design documentation
- [ICasbinDbContextProvider Interface](Casbin.Persist.Adapter.EFCore/ICasbinDbContextProvider.cs) - Interface definition
- [Casbin.NET Documentation](https://casbin.org/docs/overview) - Casbin concepts
