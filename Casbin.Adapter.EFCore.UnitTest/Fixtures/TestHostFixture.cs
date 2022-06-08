using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Casbin.Persist;
using System;

namespace Casbin.Adapter.EFCore.UnitTest.Fixtures
{
    public class TestHostFixture
    {
        public TestHostFixture()
        {
            Services = new ServiceCollection()
                .AddDbContext<CasbinDbContext<int>>(options =>
                {
                    options.UseSqlite("Data Source=CasbinHostTest.db");
                })
                .AddScoped<IAdapter, EFCoreAdapter<int>>()
                .BuildServiceProvider();
            Server = new TestServer(Services);
        }

        public TestServer Server { get; set; }

        public IServiceProvider Services { get; set; }
    }
}