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

When using the adapter with Dependency Injection (e.g., in ASP.NET Core), it's recommended to use `IDbContextFactory<TContext>` instead of directly injecting the `DbContext`. This prevents issues with the `DbContext` being disposed while the enforcer is still using it.

**For .NET 5.0 and later:**

```csharp
using Casbin.Adapter.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Define a custom DbContext (if using default CasbinDbContext)
public class MyCasbinDbContext : CasbinDbContext<int>
{
    public MyCasbinDbContext(DbContextOptions<MyCasbinDbContext> options) 
        : base(options)
    {
    }
}

// In your Program.cs or Startup.cs
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Register the DbContext factory (recommended for DI scenarios)
        builder.Services.AddDbContextFactory<MyCasbinDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
        
        // Configure Casbin
        builder.Services.AddCasbinAuthorization(options =>
        {
            options.DefaultModelPath = "model.conf";
            
            // Use the factory-based adapter constructor
            options.DefaultEnforcerFactory = (serviceProvider, model) =>
            {
                var contextFactory = serviceProvider.GetRequiredService<IDbContextFactory<MyCasbinDbContext>>();
                var adapter = new EFCoreAdapter<int, EFCorePersistPolicy<int>, MyCasbinDbContext>(contextFactory);
                return new Enforcer(model, adapter);
            };
        });
        
        var app = builder.Build();
        
        // Ensure database is created
        using (var scope = app.Services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MyCasbinDbContext>>();
            using var context = factory.CreateDbContext();
            context.Database.EnsureCreated();
        }
        
        app.Run();
    }
}
```

**Usage in a controller or page model:**

```csharp
public class MyController : Controller
{
    private readonly IEnforcer _enforcer;
    
    public MyController(IEnforcerProvider enforcerProvider)
    {
        _enforcer = enforcerProvider.GetEnforcer();
    }
    
    public IActionResult Index()
    {
        // The enforcer can safely perform operations across multiple requests
        // Each operation creates and disposes its own DbContext
        _enforcer.LoadPolicy();
        
        if (_enforcer.Enforce(User.Identity.Name, "resource", "read"))
        {
            // Allow access
        }
        
        return View();
    }
}
```

### Why use IDbContextFactory?

When you inject a `DbContext` directly into a service with a longer lifetime than the context (like a singleton enforcer), you'll encounter `ObjectDisposedException` when the context is disposed but the enforcer tries to use it. 

Using `IDbContextFactory<TContext>` solves this by:
- Creating a new `DbContext` for each operation
- Automatically disposing the context after the operation completes
- Allowing the enforcer to outlive individual request scopes

## Getting Help

- [Casbin.NET](https://github.com/casbin/Casbin.NET)

## License

This project is under Apache 2.0 License. See the [LICENSE](LICENSE) file for the full license text.