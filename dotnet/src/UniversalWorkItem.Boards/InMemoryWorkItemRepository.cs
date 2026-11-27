using System;
using System.Data.Fuse;
using System.Data.Fuse.Convenience;
using System.Data.ModelDescription;

namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// Volatile WorkItem repository based on the FUSE <see cref="InMemoryRepository{TEntity, TKey}"/>.
  /// Used for demo boards and as a stand-in until real providers are configured.
  /// </summary>
  public class InMemoryWorkItemRepository : InMemoryRepository<WorkItem, WorkItemIdentity>, IWorkItemRepository {

    private static readonly string[] _DemoStates = new string[] {
      "New", "Suspended.AwaitingExternalResponse", "Working.Implementation", "Working.UAT", "Done.Implemented", "Discontinued.WontFix"
    };

    private static readonly string[] _DemoAreas = new string[] {
      "/", "/ui", "/ui/forms", "/backend", "/backend/persistence", "/docs"
    };

    private static readonly string[] _DemoAssignees = new string[] {
      "", "Alice", "Bob", "Carol"
    };

    public InMemoryWorkItemRepository() : base(CreateSchema(), true, null) {
    }

    private static SchemaRoot CreateSchema() {

      return ModelReader.GetSchema(new Type[] { typeof(WorkItem) }, false);

    }

    /// <summary>
    /// Creates a repository pre-filled with demo cards spread over the well-known root states (with sub-states)
    /// and some source-related areas ('/ui', '/backend/persistence', ...).
    /// </summary>
    /// <param name="origin">The 'Origin' of all generated cards.</param>
    /// <param name="titlePrefix">Prefix of the generated titles (to distinguish several demo sources).</param>
    /// <param name="cardCount">Number of cards to generate.</param>
    public static InMemoryWorkItemRepository CreateDemo(string origin, string titlePrefix = "Demo card", int cardCount = 12) {

      InMemoryWorkItemRepository repository = new InMemoryWorkItemRepository();
      Random random = new Random(GetStableSeed(origin));

      for (int i = 1; i <= cardCount; i++) {

        WorkItem item = new WorkItem();
        item.Origin = origin;
        item.OriginRelatedId = i.ToString();
        item.Title = titlePrefix + " " + i.ToString();
        item.BodyContent = "This card was generated for a demo board.";
        item.ConcernArea = _DemoAreas[random.Next(_DemoAreas.Length)];
        item.StorageArea = "/";
        item.ProgressState = _DemoStates[random.Next(_DemoStates.Length)];
        item.AssignedTo = _DemoAssignees[random.Next(_DemoAssignees.Length)];
        item.ManualReorderedPriority = random.Next(1, 1000000);
        item.CreatedTimestamp = DateTime.UtcNow;

        if (i % 3 == 0) {
          item.Tags = new string[] { "demo", "bug" };
        }
        else {
          item.Tags = new string[] { "demo" };
        }

        repository.AddOrUpdateEntity(item);
      }

      return repository;
    }

    /// <summary>
    /// Process-independent seed (string.GetHashCode is randomized per process), so demo data stays stable.
    /// </summary>
    private static int GetStableSeed(string text) {

      int seed = 17;

      foreach (char character in text) {
        seed = unchecked(seed * 31 + character);
      }

      return seed;
    }

  }

}
