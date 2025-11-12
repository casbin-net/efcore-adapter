# EF Core Adapter

[![Build Status](https://github.com/casbin-net/efcore-adapter/workflows/Build/badge.svg)](https://github.com/casbin-net/efcore-adapter/actions)
[![Coverage Status](https://coveralls.io/repos/github/casbin-net/EFCore-Adapter/badge.svg?branch=master)](https://coveralls.io/github/casbin-net/EFCore-Adapter?branch=master)
[![Nuget](https://img.shields.io/nuget/v/Casbin.NET.Adapter.EFCore.svg)](https://www.nuget.org/packages/Casbin.NET.Adapter.EFCore/)
[![Release](https://img.shields.io/github/release/casbin-net/efcore-adapter.svg)](https://github.com/casbin-net/efcore-adapter/releases/latest)
[![Nuget](https://img.shields.io/nuget/dt/Casbin.NET.Adapter.EFCore.svg)](https://www.nuget.org/packages/Casbin.NET.Adapter.EFCore/)
[![Discord](https://img.shields.io/discord/1022748306096537660?logo=discord&label=discord&color=5865F2)](https://discord.gg/S5UjpzGZjN)

EF Core Adapter is the [EF Core](https://docs.microsoft.com/en-gb/ef/) adapter for [Casbin](https://github.com/casbin/casbin). With this library, Casbin can load policy from EF Core supported database or save policy to it.

The current version supported all databases which EF Core supported, there is a part list:

- SQL Server 2012 onwards
- SQLite 3.7 onwards
- Azure Cosmos DB SQL API
- PostgreSQL
- MySQL, MariaDB
- Oracle DB
- Db2, Informix
- And more...

You can see all the list at [Database Providers](https://docs.microsoft.com/en-gb/ef/core/providers).

## Installation
```
dotnet add package Casbin.NET.Adapter.EFCore
```

## Supported Frameworks

The adapter supports the following .NET target frameworks:
- .NET 9.0
- .NET 8.0
- .NET 7.0
- .NET 6.0
- .NET 5.0
- .NET Core 3.1

## Simple Example

```csharp
using Casbin.Adapter.EFCore;
using Microsoft.EntityFrameworkCore;
using NetCasbin;

namespace ConsoleAppExample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // You should build a DbContextOptions for CasbinDbContext<TKey>.
            // The example use the SQLite database named "casbin_example.sqlite3".
            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite("Data Source=casbin_example.sqlite3")
                .Options;
            var context = new CasbinDbContext<int>(options);

            // If it doesn't exist, you can use this to create it automatically.
            context.Database.EnsureCreated();

            // Initialize a EF Core adapter and use it in a Casbin enforcer:
            var efCoreAdapter = new EFCoreAdapter<int>(context);
            var e = new Enforcer("examples/rbac_model.conf", efCoreAdapter);

            // Load the policy from DB.
            e.LoadPolicy();

            // Check the permission.
            e.Enforce("alice", "data1", "read");
            
            // Modify the policy.
            // e.AddPolicy(...)
            // e.RemovePolicy(...)
	
            // Save the policy back to DB.
            e.SavePolicy();
        }
    }
}
```

## Using with Dependency Injection

When using the adapter with dependency injection (e.g., in ASP.NET Core), you should use the `IServiceProvider` constructor or the extension method to avoid issues with disposed DbContext instances.

### Recommended Approach (Using Extension Method)

```csharp
using Casbin.Persist.Adapter.EFCore;
using Casbin.Persist.Adapter.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Register services
services.AddDbContext<CasbinDbContext<int>>(options =>
    options.UseSqlServer(connectionString));

// Register the adapter using the extension method
services.AddEFCoreAdapter<int>();

// The adapter will resolve the DbContext from the service provider on each operation,
// preventing issues with disposed contexts when used with long-lived services.
```

### Alternative Approach (Using IServiceProvider Constructor)

```csharp
// In your startup configuration
services.AddDbContext<CasbinDbContext<int>>(options =>
    options.UseSqlServer(connectionString));

services.AddCasbinAuthorization(options =>
{
    options.DefaultModelPath = "model.conf";
    
    // Use the IServiceProvider constructor
    options.DefaultEnforcerFactory = (sp, model) =>
        new Enforcer(model, new EFCoreAdapter<int>(sp));
});
```

This approach resolves the DbContext from the service provider on each database operation, ensuring that:
- The adapter works correctly with scoped DbContext instances
- No `ObjectDisposedException` is thrown when the adapter outlives the scope that created it
- The adapter can be used in long-lived services like singletons

## Multi-Context Support

The adapter supports storing different policy types in separate database contexts, allowing you to:
- Store policies (p, p2, etc.) and groupings (g, g2, etc.) in different schemas and/or tables
- Each Context can control both schema AND table independently
- Separate data for multi-tenant or compliance scenarios

### Quick Example

```csharp
// Create ONE shared connection object
var sharedConnection = new SqlConnection(connectionString);

// Create contexts with shared connection
var policyContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer(sharedConnection).Options,  // Shared connection
    schemaName: "policies");

var groupingContext = new CasbinDbContext<int>(
    new DbContextOptionsBuilder<CasbinDbContext<int>>()
        .UseSqlServer(sharedConnection).Options,  // Same connection
    schemaName: "groupings");

// Create a provider that routes policy types to contexts
var provider = new PolicyTypeContextProvider(policyContext, groupingContext);

// Use the provider with the adapter
var adapter = new EFCoreAdapter<int>(provider);
var enforcer = new Enforcer("rbac_model.conf", adapter);

// All operations work transparently across contexts
enforcer.AddPolicy("alice", "data1", "read");      // → policyContext
enforcer.AddGroupingPolicy("alice", "admin");      // → groupingContext
enforcer.SavePolicy();                              // Atomic across both
```

> **⚠️ Transaction Integrity Requirements**
>
> For atomic multi-context operations:
> 1. **Share DbConnection:** All contexts must use the **same `DbConnection` object** (reference equality)
> 2. **Disable AutoSave:** Use `enforcer.EnableAutoSave(false)` and call `SavePolicyAsync()` to batch commit
> 3. **Supported databases:** PostgreSQL, MySQL, SQL Server, SQLite (same file)
>
> **Why disable AutoSave?** With `EnableAutoSave(true)` (default), each policy operation commits immediately and independently. If a later operation fails, earlier operations remain committed. With `EnableAutoSave(false)`, all changes stay in memory until `SavePolicyAsync()` commits them atomically across all contexts using a shared connection-level transaction.
>
> - ✅ **Atomic:** Same `DbConnection` object + `EnableAutoSave(false)` + `SavePolicyAsync()`
> - ❌ **Not Atomic:** AutoSave ON, separate `DbConnection` objects, different databases
>
> See detailed explanation in [EnableAutoSave and Transaction Atomicity](MULTI_CONTEXT_USAGE_GUIDE.md#enableautosave-and-transaction-atomicity).

### Documentation

- **[Multi-Context Usage Guide](MULTI_CONTEXT_USAGE_GUIDE.md)** - Complete step-by-step guide with examples
- **[Multi-Context Design](MULTI_CONTEXT_DESIGN.md)** - Detailed design documentation and limitations
- **[Integration Tests Setup](Casbin.Persist.Adapter.EFCore.UnitTest/Integration/README.md)** - How to run transaction integrity tests locally

## Getting Help

- [Casbin.NET](https://github.com/casbin/Casbin.NET)

## License

This project is under Apache 2.0 License. See the [LICENSE](LICENSE) file for the full license text.