using System.Collections.Generic;
using System.Data.Fuse;
using Collaboration.WorkTracking.Boards;
using Microsoft.VisualStudio.TestTools.UnitTesting;

#nullable disable

namespace Collaboration.WorkTracking {

  [TestClass]
  public class InMemoryWorkItemRepositoryTests {

    /// <summary>
    /// Verifies that cards can be loaded by their composite key (Origin + OriginRelatedId) in the requested order.
    /// </summary>
    [TestMethod()]
    public void GetEntitiesByKey_CompositeKey_ReturnsCardsInRequestedOrder() {

      IWorkItemRepository repository = InMemoryWorkItemRepository.CreateDemo("demo://a", "Card", 5);

      WorkItemIdentity[] keys = new WorkItemIdentity[] {
        new WorkItemIdentity("demo://a", "3"),
        new WorkItemIdentity("demo://a", "999"),
        new WorkItemIdentity("demo://a", "1")
      };

      WorkItem[] loaded = repository.GetEntitiesByKey(keys);

      Assert.AreEqual(3, loaded.Length);
      Assert.AreEqual("Card 3", loaded[0].Title);
      Assert.IsNull(loaded[1]);
      Assert.AreEqual("Card 1", loaded[2].Title);

    }

    /// <summary>
    /// Verifies that an entity based update of an existing card is applied.
    /// </summary>
    [TestMethod()]
    public void TryUpdateEntity_ExistingCard_UpdatesCard() {

      IWorkItemRepository repository = InMemoryWorkItemRepository.CreateDemo("demo://a", "Card", 5);

      WorkItem item = repository.GetEntitiesByKey(new WorkItemIdentity[] { new WorkItemIdentity("demo://a", "2") })[0];
      WorkItem copy = Newtonsoft.Json.JsonConvert.DeserializeObject<WorkItem>(Newtonsoft.Json.JsonConvert.SerializeObject(item));
      copy.Title = "Changed";

      WorkItem updated = repository.TryUpdateEntity(copy);

      Assert.IsNotNull(updated);
      Assert.AreEqual("Changed", updated.Title);

    }

    /// <summary>
    /// Verifies that a field based update (which internally loads the card by its key) is applied.
    /// </summary>
    [TestMethod()]
    public void TryUpdateEntityFields_ExistingCard_UpdatesFields() {

      IWorkItemRepository repository = InMemoryWorkItemRepository.CreateDemo("demo://a", "Card", 5);

      Dictionary<string, object> fields = new Dictionary<string, object>();
      fields["Origin"] = "demo://a";
      fields["OriginRelatedId"] = "2";
      fields["Title"] = "Changed";

      Dictionary<string, object> deviations = repository.TryUpdateEntityFields(fields);

      Assert.IsNotNull(deviations);
      Assert.AreEqual(0, deviations.Count);

      WorkItem[] loaded = repository.GetEntitiesByKey(new WorkItemIdentity[] { new WorkItemIdentity("demo://a", "2") });
      Assert.AreEqual("Changed", loaded[0].Title);

      Assert.IsTrue(repository.ContainsKey(new WorkItemIdentity("demo://a", "2")));
      Assert.IsFalse(repository.ContainsKey(new WorkItemIdentity("demo://b", "2")));

    }

    /// <summary>
    /// Verifies value based equality of the composite key.
    /// </summary>
    [TestMethod()]
    public void WorkItemIdentity_Equality_IsValueBased() {

      WorkItemIdentity a = new WorkItemIdentity("o", "1");
      WorkItemIdentity b = new WorkItemIdentity("o", "1");
      WorkItemIdentity c = new WorkItemIdentity("o", "2");

      Assert.AreEqual(a, b);
      Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
      Assert.AreNotEqual(a, c);
      Assert.IsTrue(a.Equals((ICompositeKey)new CompositeKey2<string, string>("o", "1")));

    }

  }

}
