# EF Core Adapter

[![Actions Status](https://github.com/casbin-net/EFCore-Adapter/workflows/Build/badge.svg)](https://github.com/casbin-net/EFCore-Adapter/actions)
[![Coverage Status](https://coveralls.io/repos/github/casbin-net/EFCore-Adapter/badge.svg?branch=master)](https://coveralls.io/github/casbin-net/EFCore-Adapter?branch=master)
[![NuGet](https://buildstats.info/nuget/Casbin.NET.Adapter.EFCore)](https://www.nuget.org/packages/Casbin.NET.Adapter.EFCore)

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

## Multi-Context Support

The adapter supports storing different policy types in separate database contexts, allowing you to:
- Store policies (p, p2, etc.) and groupings (g, g2, etc.) in different schemas
- Use different tables for different policy types
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

> **Transaction Integrity:**
>
> For atomic operations across contexts, all contexts must share the same `DbConnection` object instance. The adapter detects connection compatibility via reference equality and automatically uses `UseTransaction()` to coordinate shared transactions. Create one `DbConnection` object and pass it to all contexts using `.UseSqlServer(connection)` (not `.UseSqlServer(connectionString)`). Ensuring shared connection objects is your responsibility - use a context factory pattern to guarantee consistency.
>
> - ✅ **Atomic:** SQL Server, PostgreSQL, MySQL, SQLite (same file) - when sharing same `DbConnection` object
> - ❌ **Not Atomic:** Separate `DbConnection` objects, different databases, different connection strings
>
> See detailed requirements in the [Transaction Integrity Requirements](MULTI_CONTEXT_USAGE_GUIDE.md#shared-connection-requirements) section.

### Documentation

- **[Multi-Context Usage Guide](MULTI_CONTEXT_USAGE_GUIDE.md)** - Complete step-by-step guide with examples
- **[Multi-Context Design](MULTI_CONTEXT_DESIGN.md)** - Detailed design documentation and limitations

## Getting Help

- [Casbin.NET](https://github.com/casbin/Casbin.NET)

## License

This project is under Apache 2.0 License. See the [LICENSE](LICENSE) file for the full license text.