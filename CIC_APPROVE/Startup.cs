using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(CIC_APPROVE.Startup))]
namespace CIC_APPROVE
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
