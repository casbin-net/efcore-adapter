using System.IO;
using NetCasbin.Model;

namespace Casbin.Adapter.EFCore.UnitTest.Fixtures
{
    public class ModelProvideFixture
    {
        private readonly string _rbacModelText = File.ReadAllText("examples/rbac_model.conf");

        public Model GetNewRbacModel()
        {
            return Model.CreateDefaultFromText(_rbacModelText);
        }
    }
}