using GenHTTP.Api.Content;
using GenHTTP.Engine.Internal;
using GenHTTP.Modules.ApiBrowsing;
using GenHTTP.Modules.Layouting;
using GenHTTP.Modules.Layouting.Provider;
using GenHTTP.Modules.OpenApi;
using GenHTTP.Modules.OpenApi.Handler;
using GenHTTP.Modules.Practices;
using GenHTTP.Modules.Security;
using GenHTTP.Modules.Webservices;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
namespace SplunkAPI;

[AttributeUsage(AttributeTargets.Class)]
class WebServiceAttribute(string path = "") : Attribute
{
    public string Path { get; } = path;
}

class Program
{
    static async Task Main()
    {
        LayoutBuilder layout = Layout.Create();
        foreach ((Type type, string path) in Assembly.GetExecutingAssembly().GetTypes().Select(x => (x, x.GetCustomAttribute<WebServiceAttribute>())).Where(x => x.Item2 != null).Select(x => (x.x, x.Item2!.Path)))
            layout = layout.AddService(path, Activator.CreateInstance(type)!);
        OpenApiConcernBuilder desc = ApiDescription.Create().Title("Splunk API").Version("1.0").PostProcessor((_, document) =>
        {
            document.Servers.Clear();
            document.Servers.Add(new() { Url = "/" });
        });
        IHandlerBuilder builder = layout.AddSwaggerUi().AddRedoc("docs").Add(CorsPolicy.Permissive());
        await Host.Create().Handler(builder).Defaults().Console().RunAsync();
    }
}
