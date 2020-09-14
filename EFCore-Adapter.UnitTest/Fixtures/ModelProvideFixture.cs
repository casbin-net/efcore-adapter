using System.IO;
using NetCasbin;
using NetCasbin.Model;

namespace EFCore_Adapter.UnitTest.Fixtures
{
    public class ModelProvideFixture
    {
        private readonly string _rbacModelText = File.ReadAllText("examples/rbac_model.conf");

        public Model GetNewRbacModel()
        {
            return CoreEnforcer.NewModel(_rbacModelText);
        }
    }
}