using Collaboration.WorkTracking.Boards;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;
using System.Windows.Forms;

namespace Collaboration.WorkTracking.UWIboard {

  /// <summary>
  /// Launch target 1: desktop shell. Hosts the board UI (embedded resources) and the board services (UJMW)
  /// within an in-memory ASP.NET Core engine, which is displayed via WebView2.
  /// </summary>
  public static class Program {

    private const string _MountPath = "/";

    private static WebViewAspNetCoreAdapter _AspApplication;

    internal static WebViewAspNetCoreAdapter AspApplication {
      get {
        return _AspApplication;
      }
    }

    internal static string MountPath {
      get {
        return _MountPath;
      }
    }

    /// <summary>
    /// Entry point.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) {

      Application.SetHighDpiMode(HighDpiMode.SystemAware);
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);

      FormMain mainWindow = null;

      try {

        _AspApplication = new WebViewAspNetCoreAdapter(
          Assembly.GetExecutingAssembly(),
          (services, config) => ConfigureServices(services, config),
          (app, config, serviceProvider, environment, lifetime) => ConfigureApplication(app)
        );

        mainWindow = new FormMain();

        Application.Run(mainWindow);

      }
      finally {

        if (mainWindow != null) {
          mainWindow.Dispose();
        }

        if (_AspApplication != null) {
          _AspApplication.Dispose();
        }

      }

    }

    /// <summary>
    /// Registers the board registry (MVP: in-memory demo boards) and the shared board UI services.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, IConfiguration config) {

      BoardRegistry registry = DemoBoards.CreateRegistry();

      services.AddSingleton(registry);

      //WebView2 + System.Text.Json has a known issue within the TestServer pipeline (see template)
      services.AddControllers().AddNewtonsoftJson();

      services.AddBoardUi(_MountPath);

    }

    /// <summary>
    /// Configures the request pipeline.
    /// </summary>
    private static void ConfigureApplication(WebApplication app) {

      app.UseRouting();

      app.MapControllers();
      app.MapBoardUi(_MountPath);

    }

  }

}
