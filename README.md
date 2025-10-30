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

## Getting Help

- [Casbin.NET](https://github.com/casbin/Casbin.NET)

## License

This project is under Apache 2.0 License. See the [LICENSE](LICENSE) file for the full license text.