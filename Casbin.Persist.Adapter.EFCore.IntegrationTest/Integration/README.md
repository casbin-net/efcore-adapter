# Integration Tests for Multi-Context Transaction Integrity

This directory contains integration tests that verify the transaction integrity guarantees of the multi-context EFCore adapter feature.

## Separate Test Project

These integration tests are in a **separate test project** (`Casbin.Persist.Adapter.EFCore.IntegrationTest`) to enable sequential framework execution.

**Why separate?**
- Integration tests run frameworks **sequentially** (one at a time) to avoid PostgreSQL database conflicts
- Unit tests continue running frameworks **in parallel** for faster execution
- .NET 9+ runs multi-targeted tests in parallel by default - this separation allows different configurations

**Project Settings:**
- `<TestTfmsInParallel>false</TestTfmsInParallel>` - Frameworks execute sequentially
- Shares single PostgreSQL database: `casbin_integration_test`
- Uses `DisableParallelization = true` on test collection for within-framework sequencing

## Purpose

These tests prove that when multiple `DbContext` instances share the same `DbConnection` object, operations across contexts are **atomic** - they either all succeed or all fail together.

## Prerequisites

### 1. PostgreSQL Installation

You need PostgreSQL running locally on your development machine.

**Install PostgreSQL:**
- **Windows**: Download from [postgresql.org](https://www.postgresql.org/download/windows/)
- **macOS**: `brew install postgresql@17` (or use [Postgres.app](https://postgresapp.com/))
- **Linux**: `sudo apt-get install postgresql` (Debian/Ubuntu) or equivalent

### 2. Database Setup

Create the test database:

```bash
# Connect to PostgreSQL (default superuser is 'postgres')
psql -U postgres

# Create the test database
CREATE DATABASE casbin_integration_test;

# Exit psql
\q
```

Alternatively, use a one-liner:

```bash
psql -U postgres -c "CREATE DATABASE casbin_integration_test;"
```

### 3. Connection Credentials

The tests use these default credentials:
- **Host**: `localhost:5432`
- **Database**: `casbin_integration_test`
- **Username**: `postgres`
- **Password**: `postgres4all!`

**If your PostgreSQL uses different credentials**, update the connection string in [TransactionIntegrityTestFixture.cs](TransactionIntegrityTestFixture.cs):

```csharp
ConnectionString = "Host=localhost;Database=casbin_integration_test;Username=YOUR_USER;Password=YOUR_PASSWORD";
```

## Running the Tests

### Run All Integration Tests

```bash
dotnet test --filter "Category=Integration"
```

### Run a Specific Test

```bash
dotnet test --filter "FullyQualifiedName~SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically"
```

### Run with Specific Framework

```bash
dotnet test --filter "Category=Integration" -f net9.0
```

## Test Architecture

### Test Fixture

The [TransactionIntegrityTestFixture](TransactionIntegrityTestFixture.cs) automatically:
1. Creates 3 PostgreSQL schemas: `casbin_policies`, `casbin_groupings`, `casbin_roles`
2. Creates tables in each schema
3. Clears all data before each test
4. Cleans up schemas after all tests complete

### Test Organization

The integration tests are organized into 3 test classes:

| Test Class | Tests | Purpose |
|------------|-------|---------|
| `TransactionIntegrityTests` | 7 | Multi-context transaction atomicity and rollback |
| `AutoSaveTests` | 10 | Casbin.NET AutoSave behavior verification |
| `SchemaDistributionTests` | 2 | Schema routing with shared connections |

**Total:** 19 integration tests

The tests use a three-way context provider that routes:
- **p policies** → `casbin_policies` schema
- **g groupings** → `casbin_groupings` schema
- **g2 roles** → `casbin_roles` schema

This simulates real-world multi-context scenarios where different policy types are stored separately for compliance, multi-tenancy, or organizational requirements.

## Test Coverage

| Test | What It Proves |
|------|----------------|
| `SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically` | Policies written to 3 schemas in a single atomic transaction |
| `MultiContextSetup_WithSharedConnection_ShouldShareSamePhysicalConnection` | Reference equality confirms DbConnection object sharing |
| `SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts` | Severe failures cause complete rollback |
| `MultipleSaveOperations_WithSharedConnection_ShouldMaintainDataConsistency` | Multiple operations maintain consistency over time |
| `SavePolicy_WithSeparateConnections_ShouldNotBeAtomic` | **Negative test**: Proves separate connections are NOT atomic |
| `SavePolicy_ShouldReflectDatabaseStateNotCasbinMemory` | Tests verify actual database state, not just Casbin memory |

### SchemaDistributionTests

**File:** [SchemaDistributionTests.cs](SchemaDistributionTests.cs)
**Test Count:** 2
**Status:** ✅ All Passing

**Purpose:**

These tests verify that `CasbinDbContext.HasDefaultSchema()` correctly routes policies to their designated schemas when using shared connections, ensuring schema isolation is maintained.

**Test Coverage:**

| Test | Purpose | Status |
|------|---------|--------|
| `SavePolicy_SeparateConnections_ShouldDistributeAcrossSchemas` | Baseline behavior with separate connections | ✅ Passing |
| `SavePolicy_SharedConnection_ShouldDistributeAcrossSchemas` | Schema routing with shared connection | ✅ Passing |

**What They Test:**

1. **Schema Routing:**
   - `p` policies → `casbin_policies` schema
   - `g` policies → `casbin_groupings` schema
   - `g2` policies → `casbin_roles` schema

2. **Shared Connection Impact:**
   - Verifies `HasDefaultSchema()` returns correct schema name per context
   - Confirms shared connection doesn't break schema isolation
   - Validates multi-context routing works correctly

3. **Database Verification:**
   - Direct SQL queries to each schema
   - Counts policies by type in each schema
   - Asserts correct distribution (e.g., only `p` types in `casbin_policies` schema)

**Why This Matters:**

When using a shared connection for atomic transactions, each context must still route to its correct schema. These tests prove that sharing a connection object doesn't accidentally merge contexts or route to wrong schemas.

**Running the Tests:**

```bash
# Run both SchemaDistributionTests
dotnet test -f net6.0 --filter "FullyQualifiedName~SchemaDistributionTests" --verbosity normal

# Run specific test
dotnet test -f net6.0 --filter "FullyQualifiedName~SavePolicy_SharedConnection_ShouldDistributeAcrossSchemas" --verbosity normal
```

### AutoSaveTests

**File:** [AutoSaveTests.cs](AutoSaveTests.cs)
**Test Count:** 10
**Status:** ✅ All Passing

**Purpose:**

These tests verify the Casbin Enforcer's `EnableAutoSave` behavior in multi-context scenarios and prove that `EnableAutoSave(false)` is required for atomic rollback testing.

**Key Tests:**

| Test | What It Proves | Status |
|------|----------------|--------|
| `TestPolicyAutoSaveOn` / `TestPolicyAutoSaveOnAsync` | AutoSave ON commits immediately | ✅ Passing |
| `TestPolicyAutoSaveOff` | AutoSave OFF defers until SavePolicy | ✅ Passing |
| `TestGroupingPolicyAutoSaveOn` | Grouping policies also commit immediately | ✅ Passing |
| `TestGroupingPolicyAutoSaveOff` | Grouping policies defer with AutoSave OFF | ✅ Passing |
| `TestAutoSaveOn_MultiContext_IndividualCommits` | Multi-context: operations commit independently | ✅ Passing |
| `TestAutoSaveOff_MultiContext_RollbackOnFailure` | Multi-context: atomic rollback with AutoSave OFF | ✅ Passing |

**Why AutoSave Testing Matters:**

The rollback tests in `TransactionIntegrityTests` require `enforcer.EnableAutoSave(false)` (lines 302, 370) because:
- With AutoSave ON: Policies commit immediately when `AddPolicyAsync()` is called
- With AutoSave OFF: Policies stay in memory until `SavePolicyAsync()` is called
- Atomic rollback testing requires all policies to be part of the same transaction

**See:** [MULTI_CONTEXT_USAGE_GUIDE.md - EnableAutoSave and Transaction Atomicity](../../MULTI_CONTEXT_USAGE_GUIDE.md#enableautosave-and-transaction-atomicity) for detailed explanation.

## Why These Tests Are Excluded from CI/CD

These tests are marked with `[Trait("Category", "Integration")]` and **excluded from CI/CD** because:

1. **Pipeline Ownership**: The CI/CD pipeline is not owned by this project's maintainers
2. **External Dependency**: Requires a PostgreSQL instance with specific configuration
3. **Local Verification**: These tests are for **local verification only** - they prove the documented transaction guarantees work correctly

## Troubleshooting

### Error: "could not connect to server"

PostgreSQL is not running. Start it:
- **Windows**: Open Services → Start "postgresql-x64-XX"
- **macOS (Homebrew)**: `brew services start postgresql@17`
- **Linux**: `sudo systemctl start postgresql`

### Error: "database 'casbin_integration_test' does not exist"

Create the database:
```bash
psql -U postgres -c "CREATE DATABASE casbin_integration_test;"
```

### Error: "password authentication failed for user 'postgres'"

Either:
1. Update your PostgreSQL password: `ALTER USER postgres PASSWORD 'postgres';`
2. Or update the connection string in [TransactionIntegrityTestFixture.cs](TransactionIntegrityTestFixture.cs) to match your credentials

### Error: "relation 'casbin_rule' does not exist"

The test fixture should create tables automatically. If this fails:
1. Ensure the database exists
2. Ensure the user has CREATE privileges: `GRANT ALL PRIVILEGES ON DATABASE casbin_integration_test TO postgres;`
3. Try manually creating schemas: `CREATE SCHEMA casbin_policies;` etc.

## Verification of Transaction Guarantees

### Critical Rollback Tests

The **most critical tests** are the rollback verification tests:
- `SavePolicy_WhenTableDroppedInOneContext_ShouldRollbackAllContexts`
- `SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts`

**Key Implementation Detail:**

These tests call `enforcer.EnableAutoSave(false)` immediately after creating the enforcer (lines 302, 370 in `TransactionIntegrityTests.cs`). This is **critical** because:

- **With AutoSave ON (default):** `AddPolicyAsync()` commits immediately to database. When `SavePolicyAsync()` is called later and fails, it only rolls back DELETE operations, not the earlier INSERT operations that already committed.

- **With AutoSave OFF:** Policies stay in-memory until `SavePolicyAsync()` is called. When the transaction fails, ALL operations (INSERT and DELETE) roll back atomically.

**Code Reference:** See lines 302, 370 in [TransactionIntegrityTests.cs](TransactionIntegrityTests.cs)

## See Also

- [MULTI_CONTEXT_DESIGN.md](../../MULTI_CONTEXT_DESIGN.md) - Technical design and architecture
- [MULTI_CONTEXT_USAGE_GUIDE.md](../../MULTI_CONTEXT_USAGE_GUIDE.md) - User-facing usage guide
- [Main README](../../README.md) - Project overview
