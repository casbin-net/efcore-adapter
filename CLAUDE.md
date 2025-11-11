# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is **Casbin.NET.Adapter.EFCore** - an Entity Framework Core adapter for Casbin.NET authorization library. It enables Casbin to persist and load authorization policies from any EF Core supported database (SQL Server, SQLite, PostgreSQL, MySQL, etc.).

## Building and Testing

### Build
```bash
# Restore dependencies
dotnet restore

# Build the solution (Release mode)
dotnet build -c Release --no-restore

# Build specific framework
dotnet build -f net9.0
```

### Run Tests
```bash
# Run all tests
dotnet test -c Release --no-build --no-restore --verbosity normal

# Run specific test
dotnet test --filter "FullyQualifiedName~AdapterTest.TestAdapterAutoSave"

# Run tests for specific framework
dotnet test -f net9.0
```

## Multi-Framework Support

This project targets multiple .NET versions (.NET 3.1, 5.0, 6.0, 7.0, 8.0, 9.0) with different EF Core versions:
- Main project: `Casbin.Persist.Adapter.EFCore.csproj` uses conditional `<ItemGroup>` elements to reference appropriate EF Core versions
- Test project: `Casbin.Persist.Adapter.EFCore.UnitTest.csproj` follows the same pattern

When modifying dependencies, ensure changes are made to ALL framework-specific ItemGroup sections.

## Core Architecture

### Key Classes

**EFCoreAdapter<TKey, TPersistPolicy, TDbContext>** (Casbin.Persist.Adapter.EFCore\EFCoreAdapter.cs)
- Main adapter implementing `IAdapter` and `IFilteredAdapter` interfaces
- Provides CRUD operations for policies: LoadPolicy, SavePolicy, AddPolicy, RemovePolicy, UpdatePolicy
- Has both sync and async versions of all methods
- Partial class split across EFCoreAdapter.cs and EFCoreAdapter.Internal.cs
- Generic design supports custom key types (int, Guid, string, etc.) and custom policy entities

**CasbinDbContext<TKey>** (Casbin.Persist.Adapter.EFCore\CasbinDbContext.cs)
- EF Core DbContext for Casbin policies
- Configurable table name (default: "casbin_rule") and schema name
- Supports custom entity type configurations via `IEntityTypeConfiguration<EFCorePersistPolicy<TKey>>`

**EFCorePersistPolicy<TKey>** (Casbin.Persist.Adapter.EFCore\Entities\EFCorePersistPolicy.cs)
- Entity representing a single policy rule in the database
- Inherits from `PersistPolicy` (from Casbin.NET) and implements `IEFCorePersistPolicy<TKey>`
- Contains: Id (generic TKey), Type (p/g), Section, and Value1-Value5 fields

### Policy Operations Flow

1. **Load**: Database → `LoadPolicy()` → Extension method `LoadPolicyFromPersistPolicy()` → Casbin IPolicyStore
2. **Save**: Casbin IPolicyStore → Extension method `ReadPolicyFromCasbinModel()` → `SavePolicy()` → Database (removes all existing, then adds all)
3. **Add/Remove**: Direct database operations with immediate `SaveChanges()` in AutoSave mode
4. **Update**: Uses transaction - internally calls Remove + Add

### Extension Methods

**PolicyStoreExtension.cs** (Casbin.Persist.Adapter.EFCore\Extensions\PolicyStoreExtension.cs)
- `LoadPolicyFromPersistPolicy`: Converts database entities to Casbin policy store
- `ReadPolicyFromCasbinModel`: Converts Casbin policy store to database entities

### Filtered Adapter Support

The adapter implements `IFilteredAdapter` for loading subsets of policies:
- `LoadFilteredPolicy()` / `LoadFilteredPolicyAsync()` - loads only policies matching the filter
- Sets `IsFiltered = true` to indicate partial policy load
- See Filter class usage in EFCoreAdapterTest.cs:142-165 for examples

## Testing Strategy

### Unit Tests (SQLite)
Tests use xUnit with fixtures:
- **ModelProvideFixture**: Provides Casbin model configurations
- **DbContextProviderFixture**: Provides test database contexts (SQLite in-memory)
- **EFCoreAdapterTest.cs**: Main test suite covering all adapter operations
- **DependencyInjectionTest.cs**: Tests for DI scenarios
- **PolicyEdgeCasesTest.cs**: Edge cases and special scenarios
- **MultiContextTest.cs**: Multi-context functional tests using separate SQLite database files

**Important**: SQLite unit tests use **separate database files** for each context (policy.db, grouping.db).
This means each context has its own connection, making atomic cross-context transactions impossible.
These tests verify functional correctness but **cannot test transaction rollback across contexts**.

### Integration Tests (PostgreSQL)
- **Integration/TransactionIntegrityTests.cs**: Atomic transaction and rollback verification
- **Integration/SchemaDistributionTests.cs**: Policy distribution across schemas
- **Integration/AutoSaveTests.cs**: AutoSave behavior with single and multiple contexts

**Important**: PostgreSQL integration tests use **one shared connection** with multiple schemas.
This enables testing of atomic transactions and rollback across multiple contexts.
These tests are marked `[Trait("Category", "Integration")]` and excluded from CI/CD.

Test data files in `Casbin.Persist.Adapter.EFCore.UnitTest\examples\`:
- `rbac_model.conf`: RBAC model definition
- `rbac_policy.csv`: Test policy data

## Important Implementation Details

### AutoSave vs Manual Save
- Most operations call `DbContext.SaveChanges()` immediately (AutoSave behavior)
- When implementing custom adapters, override virtual methods (OnLoadPolicy, OnSavePolicy, OnAddPolicy, etc.) to customize behavior

### Virtual Methods for Extensibility
The adapter provides several virtual methods for customization:
- `OnLoadPolicy()`: Modify query before loading
- `OnSavePolicy()`: Transform policies before saving
- `OnAddPolicy()` / `OnAddPolicies()`: Intercept policy additions
- `OnRemoveFilteredPolicy()`: Intercept policy removals
- `GetCasbinRuleDbSet()`: Customize DbSet retrieval

### Policy Filter Implementation
PolicyFilter class (in Internal.cs) applies EF Core query filters based on:
- Policy type (p, p2, g, g2, etc.)
- Field index (0-5)
- Field values (supports partial matching with empty strings)

## Package Publishing

The package is published as **Casbin.NET.Adapter.EFCore** on NuGet. See `.github\workflows\release.yml` for release automation.
