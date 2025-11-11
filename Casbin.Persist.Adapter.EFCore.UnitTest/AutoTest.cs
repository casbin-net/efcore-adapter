using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Persist.Adapter.EFCore.UnitTest.Extensions;
using Microsoft.EntityFrameworkCore;
using Casbin.Persist.Adapter.EFCore.Entities;
using Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures;
using Xunit;

namespace Casbin.Persist.Adapter.EFCore.UnitTest
{
    public class EFCoreAdapterTest : TestUtil, IClassFixture<ModelProvideFixture>, IClassFixture<DbContextProviderFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly DbContextProviderFixture _dbContextProviderFixture;

        public EFCoreAdapterTest(ModelProvideFixture modelProvideFixture, DbContextProviderFixture dbContextProviderFixture)
        {
            _modelProvideFixture = modelProvideFixture;
            _dbContextProviderFixture = dbContextProviderFixture;
        }

        private static void InitPolicy(CasbinDbContext<int> context)
        {
            context.Clear();
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "alice",
                Value2 = "data1",
                Value3 = "read",
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "bob",
                Value2 = "data2",
                Value3 = "write",
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "data2_admin",
                Value2 = "data2",
                Value3 = "read",
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "data2_admin",
                Value2 = "data2",
                Value3 = "write",
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "g",
                Value1 = "alice",
                Value2 = "data2_admin",
            });
            context.SaveChanges();
        }
    }
}
