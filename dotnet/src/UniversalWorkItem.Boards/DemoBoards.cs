namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// MVP demo board setup shared by both launch targets (until boards are persisted, see backlog I1).
  /// </summary>
  public static class DemoBoards {

    /// <summary>
    /// Creates a registry containing:
    /// 'demo' - a single in-memory source,
    /// 'composed' - three in-memory sources composed by the D14 area routing (root area segment = source repository).
    /// </summary>
    public static BoardRegistry CreateRegistry() {

      BoardRegistry registry = new BoardRegistry();

      registry.Register("demo", "Demo Board (single source)", false, InMemoryWorkItemRepository.CreateDemo("demo"));
      registry.Register("composed", "Composed Board (3 sources, area routing)", false, CreateComposedRepository());

      return registry;
    }

    /// <summary>
    /// Composes three mock 'repositories' via <see cref="AreaRoutingWorkItemRepository"/>.
    /// They stand in for the GitHub repositories of iteration I2.
    /// </summary>
    public static AreaRoutingWorkItemRepository CreateComposedRepository() {

      AreaRoutingWorkItemRepository routing = new AreaRoutingWorkItemRepository("composed");

      routing.AddSource("repo-alpha", "mock:repo-alpha", InMemoryWorkItemRepository.CreateDemo("mock:repo-alpha", "Alpha", 10));
      routing.AddSource("repo-beta", "mock:repo-beta", InMemoryWorkItemRepository.CreateDemo("mock:repo-beta", "Beta", 8));
      routing.AddSource("repo-gamma", "mock:repo-gamma", InMemoryWorkItemRepository.CreateDemo("mock:repo-gamma", "Gamma", 6));

      return routing;
    }

  }

}
