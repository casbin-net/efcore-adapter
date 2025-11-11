# Integration Tests for Multi-Context Transaction Integrity

This directory contains integration tests that verify the transaction integrity guarantees of the multi-context EFCore adapter feature.

## ⚠️ Known Issue: Casbin.NET AutoSave Bug (Upstream)

### Test Status Summary

| Test | Status | Reason |
|------|--------|--------|
| **AutoSaveTests.TestAutoSaveOn_MultiContext_IndividualCommits** | ✅ PASSING | Documents non-atomic behavior with AutoSave ON (expected) |
| **AutoSaveTests.TestAutoSaveOff_MultiContext_RollbackOnFailure_ExpectingFix** | ❌ FAILING | Casbin.NET bug: groupings commit despite AutoSave OFF |
| **AutoSaveTests.TestAutoSaveOff_MultiContext_BatchedCommit_ExpectingFix** | ❌ FAILING | Casbin.NET bug: groupings commit despite AutoSave OFF |
| **AutoSaveTests.TestAutoSaveOff_MultiContext_MinimalBugRepro_ExpectingFix** | ❌ FAILING | Minimal repro for Casbin.NET bug |
| **AutoSaveTests.TestGroupingPolicyAutoSaveOff_ExpectingFix** | ❌ FAILING | Casbin.NET bug: single-context version |
| **AutoSaveTests.TestGroupingPolicyAutoSaveOffAsync_ExpectingFix** | ❌ FAILING | Casbin.NET bug: single-context async version |

### Bug Summary

Casbin.NET has a bug where `AddGroupingPolicy()` and `AddNamedGroupingPolicy()` ignore the `EnableAutoSave(false)` setting:
- ✅ `AddPolicy()` respects AutoSave OFF (doesn't call adapter)
- ❌ `AddGroupingPolicy()` ignores AutoSave OFF (calls adapter immediately)
- ❌ `AddNamedGroupingPolicy()` ignores AutoSave OFF (calls adapter immediately)

This violates Casbin documentation:
> "When AutoSave is disabled, policy changes only affect the policy in Casbin enforcer, they do not affect the policy in the storage."

### Evidence from Test Runs

**Test output showing the bug:**
```
Database state BEFORE SavePolicy: (0, 2, 2)
Expected: (0, 0, 0) with AutoSave OFF

✗✗✗ BUG CONFIRMED - AutoSave OFF saved to database!
```

**Diagnostic logs showing adapter calls:**
```
[ADAPTER] AddPolicy INVOKED: policyType=g, section=g
[ADAPTER] Call stack:
   at Casbin.ManagementEnforcerExtension.AddGroupingPolicy(...)
[ADAPTER] Calling context.SaveChanges() to commit immediately
[ADAPTER] SaveChanges() completed
```

### Impact on Integration Tests

Five tests in **AutoSaveTests.cs** currently fail due to this Casbin.NET bug:
1. `TestAutoSaveOff_MultiContext_RollbackOnFailure_ExpectingFix` - Can't test atomic rollback because groupings already committed
2. `TestAutoSaveOff_MultiContext_BatchedCommit_ExpectingFix` - Can't test batched commit because groupings already committed
3. `TestAutoSaveOff_MultiContext_MinimalBugRepro_ExpectingFix` - Minimal reproduction showing (0, 1, 1) instead of (0, 0, 0)
4. `TestGroupingPolicyAutoSaveOff_ExpectingFix` - Single-context sync version showing (5, 6) instead of (5, 5)
5. `TestGroupingPolicyAutoSaveOffAsync_ExpectingFix` - Single-context async version

**These tests document the correct expected behavior and will pass once Casbin.NET fixes the bug.**

### Diagnostic Logging

The adapter code includes diagnostic Console.WriteLine statements that help understand the bug:
- Shows when adapter methods are called
- Shows call stacks proving Casbin.NET is calling the adapter
- Shows SaveChanges() being invoked despite AutoSave OFF

**These diagnostics will be kept until Casbin.NET fixes the bug**, then removed.

### TODO: File Bug with Casbin.NET (Future Session)

**In a separate session in the Casbin.NET repository**, we will:

1. Create simplified reproduction test (SQLite, single context, no multi-context complexity)
2. File bug report with Casbin.NET project
3. Include evidence from our investigation (shown above)

**Reference**: See `CASBIN_NET_BUG_NOTES.md` in repo root for detailed notes.

### After Casbin.NET Fixes the Bug

1. ✅ Update Casbin.NET dependency to fixed version
2. ✅ Run integration tests - should all pass
3. ✅ Remove diagnostic logging from EFCoreAdapter.cs
4. ✅ Delete MINIMAL_AUTOSAVE_OFF_BugReproduction test
5. ✅ Delete CASBIN_NET_BUG_NOTES.md
6. ✅ Update this README to remove bug section

---

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
- **Password**: `postgres`

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
| `AutoSaveTests` | 11 | Casbin.NET AutoSave behavior verification |
| `SchemaDistributionTests` | 2 | Schema routing with shared connections |

**Total:** 20 integration tests

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
| `SavePolicy_WhenDuplicateKeyViolationInOneContext_ShouldRollbackAllContexts` | **CRITICAL**: Failure in one context rolls back ALL contexts |
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
| `BaselineSchemaDistributionTest` | Baseline behavior with separate connections | ✅ Passing |
| `SchemaDistributionTest_WithSharedConnection` | Schema routing with shared connection | ✅ Passing |

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
dotnet test -f net6.0 --filter "FullyQualifiedName~SchemaDistributionTest_WithSharedConnection" --verbosity normal
```

### AutoSaveTests

**File:** [AutoSaveTests.cs](AutoSaveTests.cs)
**Test Count:** 11
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
| `TestRemovePolicyAutoSaveOn` / `Off` | Remove operations respect AutoSave | ✅ Passing |
| `TestUpdatePolicyAutoSaveOn` | Update operations respect AutoSave | ✅ Passing |

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

**Test Evolution:**

These tests originally failed with "Expected: 0, Actual: 1-2" because they used AutoSave ON. Adding `EnableAutoSave(false)` fixed them by ensuring proper rollback verification.

**Code Reference:** See lines 302, 370 in [TransactionIntegrityTests.cs](TransactionIntegrityTests.cs)

### Duplicate Key Violation Test

The test `SavePolicy_WhenDuplicateKeyViolationInOneContext_ShouldRollbackAllContexts` verifies rollback on constraint violations.

This test:
1. Inserts a policy directly into the `casbin_roles` schema
2. Adds NEW policies to `casbin_policies` and `casbin_groupings` schemas
3. Manipulates Casbin's in-memory model to create a duplicate in the `casbin_roles` schema
4. Calls `SavePolicyAsync()` which should fail due to the duplicate
5. **Verifies that NO policies were written** to ANY schema (complete rollback)

This proves that the shared transaction mechanism works correctly - when one context fails, all contexts roll back atomically.

## See Also

- [MULTI_CONTEXT_DESIGN.md](../../MULTI_CONTEXT_DESIGN.md) - Technical design and architecture
- [MULTI_CONTEXT_USAGE_GUIDE.md](../../MULTI_CONTEXT_USAGE_GUIDE.md) - User-facing usage guide
- [Main README](../../README.md) - Project overview
