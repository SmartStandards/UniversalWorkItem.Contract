using Collaboration.WorkTracking.Boards;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Collaboration.WorkTracking.UWIboard {

  /// <summary>
  /// Launch target 2: ASP.NET Core service hosting the board UI and the board services (UJMW).
  /// </summary>
  public static class Program {

    /// <summary>
    /// Entry point.
    /// </summary>
    public static void Main(string[] args) {

      WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

      ConfigureServices(builder.Services, builder.Configuration);

      WebApplication app = builder.Build();

      ConfigureApplication(app, builder.Configuration);

      app.Run();

    }

    /// <summary>
    /// Registers the board registry (MVP: in-memory demo boards) and the shared board UI services.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, IConfiguration config) {

      BoardRegistry registry = DemoBoards.CreateRegistry();

      services.AddSingleton(registry);

      services.AddControllers().AddNewtonsoftJson();

      services.AddBoardUi(GetMountPath(config));

    }

    /// <summary>
    /// Configures the request pipeline.
    /// </summary>
    private static void ConfigureApplication(WebApplication app, IConfiguration config) {

      app.UseRouting();

      app.MapControllers();
      app.MapBoardUi(GetMountPath(config));

    }

    private static string GetMountPath(IConfiguration config) {

      string mountPath = config["BoardUi:MountPath"];

      if (string.IsNullOrWhiteSpace(mountPath)) {
        return "/";
      }

      return mountPath;
    }

  }

}
