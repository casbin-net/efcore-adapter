using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures
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
                .AddScoped<IAdapter>(sp => new EFCoreAdapter<int>(sp.GetRequiredService<CasbinDbContext<int>>()))
                .BuildServiceProvider();
            Server = new TestServer(Services);
        }

        public TestServer Server { get; set; }

        public IServiceProvider Services { get; set; }
    }
}