using Casbin.Model;

namespace Casbin.Adapter.EFCore.UnitTest.Fixtures
{
    public class ModelProvideFixture
    {
        private readonly string _rbacModelText = System.IO.File.ReadAllText("examples/rbac_model.conf");

        public IModel GetNewRbacModel()
        {
            return DefaultModel.CreateFromText(_rbacModelText);
        }
    }
}