using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.UJMW;

namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// Setup extensions shared by both launch targets (desktop shell and ASP.NET Core service).
  /// The UI files are served from embedded resources and use relative URLs only, so the UI can be mounted at any path.
  /// The host must register a <see cref="BoardRegistry"/> singleton before calling <see cref="AddBoardUi"/>
  /// (otherwise an empty registry is registered).
  /// </summary>
  public static class BoardUiSetupExtensions {

    /// <summary>
    /// Route segment (relative to the mount path) of the UJMW endpoint for <see cref="IBoardManagementService"/>.
    /// </summary>
    public const string ManagementRouteSegment = "boardApi/management";

    /// <summary>
    /// Route segment (relative to the mount path) of the board-specific UJMW endpoint for
    /// <see cref="IWorkItemRepository"/>; the board id is the last segment.
    /// </summary>
    public const string WorkItemsRouteSegment = "boardApi/workItems/{boardId}";

    private const string _BoardIdArgumentName = "BoardId";

    private const string _ResourcePrefix = "UniversalWorkItem.BoardUi.";

    /// <summary>
    /// Registers the board services and their UJMW controllers under the given mount path.
    /// </summary>
    public static void AddBoardUi(this IServiceCollection services, string mountPath = "/") {

      if (services == null) {
        throw new ArgumentNullException(nameof(services));
      }

      string routePrefix = NormalizeRoutePrefix(mountPath);

      services.TryAddSingleton<BoardRegistry>();
      services.TryAddSingleton<IBoardManagementService, BoardManagementService>();
      services.TryAddSingleton<IWorkItemRepository, BoardWorkItemRepositoryFacade>();

      services.AddDynamicUjmwControllers((registrar) => {

        registrar.AddControllerFor<IBoardManagementService>((options) => {
          options.ControllerRoute = routePrefix + ManagementRouteSegment;
          options.ControllerTitle = "Board Management";
          options.EnableRequestSidechannel = false;
          options.EnableResponseSidechannel = false;
        });

        registrar.AddControllerFor<IWorkItemRepository>((options) => {
          options.ControllerRoute = routePrefix + WorkItemsRouteSegment;
          options.ControllerTitle = "Board WorkItems";
          options.EnableRequestSidechannel = false;
          options.EnableResponseSidechannel = false;
          options.BindContextualArgumentToRouteSegment(_BoardIdArgumentName, "boardId");
          options.ContextualizationHook = (endpointContextualArguments, innerInvokeContextual) => {

            string boardId = null;
            endpointContextualArguments.TryGetValue(_BoardIdArgumentName, out boardId);

            BoardWorkItemRepositoryFacade.RunForBoard(boardId, innerInvokeContextual);

          };
        });

      });

    }

    /// <summary>
    /// Maps the embedded UI files (index.html, app.js, app.css) under the given mount path.
    /// </summary>
    public static void MapBoardUi(this IEndpointRouteBuilder endpoints, string mountPath = "/") {

      if (endpoints == null) {
        throw new ArgumentNullException(nameof(endpoints));
      }

      string routePrefix = NormalizeRoutePrefix(mountPath);
      Dictionary<string, byte[]> files = LoadEmbeddedFiles(typeof(BoardUiSetupExtensions).Assembly);

      foreach (KeyValuePair<string, byte[]> file in files) {

        byte[] content = file.Value;
        string contentType = GetContentType(file.Key);

        RequestDelegate handler = (HttpContext context) => WriteFile(context, content, contentType);

        endpoints.MapGet("/" + routePrefix + file.Key, handler);

        if (string.Equals(file.Key, "index.html", StringComparison.OrdinalIgnoreCase)) {
          MapMountPathEndpoint(endpoints, routePrefix, handler);
        }

      }

    }

    /// <summary>
    /// Maps the mount path itself to the index page; requests without trailing slash are redirected
    /// (relative URLs within the UI require it).
    /// </summary>
    private static void MapMountPathEndpoint(IEndpointRouteBuilder endpoints, string routePrefix, RequestDelegate indexHandler) {

      if (routePrefix.Length == 0) {
        endpoints.MapGet("/", indexHandler);
        return;
      }

      endpoints.MapGet("/" + routePrefix, (HttpContext context) => {

        string requestPath = context.Request.PathBase.Value + context.Request.Path.Value;

        if (!requestPath.EndsWith("/", StringComparison.Ordinal)) {
          context.Response.Redirect(requestPath + "/" + context.Request.QueryString.Value);
          return Task.CompletedTask;
        }

        return indexHandler.Invoke(context);
      });

    }

    private static Dictionary<string, byte[]> LoadEmbeddedFiles(Assembly uiAssembly) {

      Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

      foreach (string resourceName in uiAssembly.GetManifestResourceNames()) {

        if (!resourceName.StartsWith(_ResourcePrefix, StringComparison.Ordinal)) {
          continue;
        }

        string fileName = resourceName.Substring(_ResourcePrefix.Length);

        using (Stream resourceStream = uiAssembly.GetManifestResourceStream(resourceName)) {
          using (MemoryStream buffer = new MemoryStream()) {
            resourceStream.CopyTo(buffer);
            files[fileName] = buffer.ToArray();
          }
        }

      }

      return files;
    }

    private static Task WriteFile(HttpContext context, byte[] content, string contentType) {

      context.Response.ContentType = contentType;
      context.Response.Headers["Cache-Control"] = "no-cache";

      return context.Response.Body.WriteAsync(content, 0, content.Length);
    }

    private static string GetContentType(string fileName) {

      string extension = Path.GetExtension(fileName).ToLowerInvariant();

      if (extension == ".html") {
        return "text/html; charset=utf-8";
      }
      else if (extension == ".js") {
        return "text/javascript; charset=utf-8";
      }
      else if (extension == ".css") {
        return "text/css; charset=utf-8";
      }
      else if (extension == ".svg") {
        return "image/svg+xml";
      }

      return "application/octet-stream";
    }

    /// <summary>
    /// Converts a mount path like '/', '/board' or 'board/' into a route prefix like '' or 'board/'.
    /// </summary>
    private static string NormalizeRoutePrefix(string mountPath) {

      if (string.IsNullOrWhiteSpace(mountPath)) {
        return string.Empty;
      }

      string trimmed = mountPath.Trim().Trim('/');

      if (trimmed.Length == 0) {
        return string.Empty;
      }

      return trimmed + "/";
    }

  }

}
