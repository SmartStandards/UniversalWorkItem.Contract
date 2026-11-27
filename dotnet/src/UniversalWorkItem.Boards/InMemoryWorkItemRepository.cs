using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Fuse;
using System.Data.Fuse.Convenience;
using System.Data.ModelDescription;
using System.Linq;
using System.Reflection;

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

    #region " Key Based Loading (composite-key-safe) "

    // The FUSE base implementation materializes the keys of loaded entities as generic 'CompositeKey2<,>'
    // and casts them to 'WorkItemIdentity', which fails. The key based loading methods are therefore
    // re-implemented here on top of the filter based API.

    /// <summary>
    /// Loads the cards with the given keys in the exact order of the given keys (NULL for non-existing keys).
    /// </summary>
    WorkItem[] IRepository<WorkItem, WorkItemIdentity>.GetEntitiesByKey(WorkItemIdentity[] keysToLoad) {
      return this.GetEntitiesByIdentity(keysToLoad);
    }

    /// <summary>
    /// Loads the given fields of the cards with the given keys in the exact order of the given keys
    /// (NULL for non-existing keys).
    /// </summary>
    Dictionary<string, object>[] IRepository<WorkItem, WorkItemIdentity>.GetEntityFieldsByKey(
      WorkItemIdentity[] keysToLoad, string[] includedFieldNames
    ) {

      WorkItem[] entities = this.GetEntitiesByIdentity(keysToLoad);
      Dictionary<string, object>[] result = new Dictionary<string, object>[entities.Length];

      for (int i = 0; i < entities.Length; i++) {

        if (entities[i] == null) {
          continue;
        }

        Dictionary<string, object> fields = new Dictionary<string, object>();

        foreach (string fieldName in includedFieldNames) {
          fields[fieldName] = entities[i][fieldName];
        }

        result[i] = fields;
      }

      return result;
    }

    /// <summary>
    /// Loads references to the cards with the given keys in the exact order of the given keys
    /// (NULL for non-existing keys).
    /// </summary>
    EntityRef<WorkItemIdentity>[] IRepository<WorkItem, WorkItemIdentity>.GetEntityRefsByKey(WorkItemIdentity[] keysToLoad) {

      WorkItem[] entities = this.GetEntitiesByIdentity(keysToLoad);
      EntityRef<WorkItemIdentity>[] result = new EntityRef<WorkItemIdentity>[entities.Length];

      for (int i = 0; i < entities.Length; i++) {

        if (entities[i] != null) {
          result[i] = new EntityRef<WorkItemIdentity>(WorkItemIdentity.FromModel(entities[i]), entities[i].ToString());
        }

      }

      return result;
    }

    /// <summary>
    /// Loads the cards with the given keys in the exact order of the given keys (NULL for non-existing keys).
    /// </summary>
    private WorkItem[] GetEntitiesByIdentity(WorkItemIdentity[] keysToLoad) {

      if (keysToLoad == null) {
        throw new ArgumentNullException(nameof(keysToLoad));
      }

      WorkItem[] result = new WorkItem[keysToLoad.Length];

      for (int i = 0; i < keysToLoad.Length; i++) {

        WorkItemIdentity key = keysToLoad[i];

        if (key == null) {
          continue;
        }

        ExpressionTree filter = ExpressionTree.And(
          FieldPredicate.Equal(nameof(WorkItemIdentity.Origin), key.Origin),
          FieldPredicate.Equal(nameof(WorkItemIdentity.OriginRelatedId), key.OriginRelatedId)
        );

        // the repository matches strings case-insensitive, so the exact (ordinal) match is ensured here
        foreach (WorkItem candidate in this.GetEntities(filter, new string[0], 500, 0)) {

          if (key.Equals((WorkItemIdentity)candidate)) {
            result[i] = candidate;
            break;
          }

        }

      }

      return result;
    }

    #endregion

    #region " Field Based Updates (indexer-safe) "

    // The FUSE base implementation of the field based update methods enumerates ALL public properties
    // of the entity type - including the indexer 'WorkItem[string]' - and therefore fails with
    // 'Parameter count mismatch'. Both methods are re-implemented here on top of the entity based API.

    /// <summary>
    /// Updates the given fields of an existing card and returns the given fields whose stored value differs
    /// from the given value (null, if the card does not exist).
    /// </summary>
    Dictionary<string, object> IRepository<WorkItem, WorkItemIdentity>.TryUpdateEntityFields(Dictionary<string, object> fields) {

      if (fields == null) {
        throw new ArgumentNullException(nameof(fields));
      }

      WorkItemIdentity key = GetKeyFromFields(fields);

      if (key == null) {
        throw new ArgumentException("The given fields must contain the key fields 'Origin' and 'OriginRelatedId'.", nameof(fields));
      }

      WorkItem entity = this.TryLoadCopy(key);

      if (entity == null) {
        return null;
      }

      ApplyFields(entity, fields);

      WorkItem updatedEntity = this.TryUpdateEntityIndexerSafe(entity);

      if (updatedEntity == null) {
        return null;
      }

      return GetDeviatingFields(updatedEntity, fields);
    }

    /// <summary>
    /// Updates the given fields of an existing card or creates a new card from them and returns the given
    /// fields whose stored value differs from the given value.
    /// </summary>
    Dictionary<string, object> IRepository<WorkItem, WorkItemIdentity>.AddOrUpdateEntityFields(Dictionary<string, object> fields) {

      if (fields == null) {
        throw new ArgumentNullException(nameof(fields));
      }

      WorkItemIdentity key = GetKeyFromFields(fields);
      WorkItem entity = null;

      if (key != null) {
        entity = this.TryLoadCopy(key);
      }

      if (entity == null) {
        entity = new WorkItem();
      }

      ApplyFields(entity, fields);

      WorkItem storedEntity = this.AddOrUpdateEntityIndexerSafe(entity);

      if (storedEntity == null) {
        throw new InvalidOperationException("The card could not be stored (key conflict).");
      }

      return GetDeviatingFields(storedEntity, fields);
    }

    /// <summary>
    /// Loads a detached copy of the card with the given key (null, if it does not exist).
    /// </summary>
    private WorkItem TryLoadCopy(WorkItemIdentity key) {

      WorkItem[] loaded = this.GetEntitiesByIdentity(new WorkItemIdentity[] { key });

      if (loaded.Length == 0 || loaded[0] == null) {
        return null;
      }

      return CreateDetachedCopy(loaded[0]);
    }

    private static WorkItem CreateDetachedCopy(WorkItem entity) {

      string json = JsonConvert.SerializeObject(entity);

      return JsonConvert.DeserializeObject<WorkItem>(json);
    }

    #endregion

    #region " Entity Based Updates (indexer-safe) "

    // The FUSE base implementation copies the values into the stored entity by enumerating ALL public
    // properties (including the indexer 'WorkItem[string]'), fails with 'Parameter count mismatch' and
    // (silently) returns null. Both methods are therefore re-implemented here.

    private static readonly PropertyInfo[] _CopyableProperties = typeof(WorkItem)
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where((p) => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
      .ToArray();

    /// <summary>
    /// Updates an existing card (returns null, if the card does not exist).
    /// </summary>
    WorkItem IRepository<WorkItem, WorkItemIdentity>.TryUpdateEntity(WorkItem entity) {
      return this.TryUpdateEntityIndexerSafe(entity);
    }

    /// <summary>
    /// Updates an existing card or adds it as new card.
    /// </summary>
    WorkItem IRepository<WorkItem, WorkItemIdentity>.AddOrUpdateEntity(WorkItem entity) {
      return this.AddOrUpdateEntityIndexerSafe(entity);
    }

    private WorkItem TryUpdateEntityIndexerSafe(WorkItem entity) {

      if (entity == null) {
        throw new ArgumentNullException(nameof(entity));
      }

      WorkItem storedEntity = this.GetEntitiesByIdentity(new WorkItemIdentity[] { WorkItemIdentity.FromModel(entity) })[0];

      if (storedEntity == null) {
        return null;
      }

      WorkItem source = CreateDetachedCopy(entity);

      lock (storedEntity) {

        foreach (PropertyInfo property in _CopyableProperties) {
          property.SetValue(storedEntity, property.GetValue(source));
        }

      }

      return storedEntity;
    }

    private WorkItem AddOrUpdateEntityIndexerSafe(WorkItem entity) {

      if (entity == null) {
        throw new ArgumentNullException(nameof(entity));
      }

      WorkItem updatedEntity = this.TryUpdateEntityIndexerSafe(entity);

      if (updatedEntity != null) {
        return updatedEntity;
      }

      return this.AddOrUpdateEntity(entity);
    }

    private static WorkItemIdentity GetKeyFromFields(Dictionary<string, object> fields) {

      object origin = null;
      object originRelatedId = null;

      foreach (KeyValuePair<string, object> field in fields) {

        if (string.Equals(field.Key, nameof(WorkItemIdentity.Origin), StringComparison.OrdinalIgnoreCase)) {
          origin = field.Value;
        }
        else if (string.Equals(field.Key, nameof(WorkItemIdentity.OriginRelatedId), StringComparison.OrdinalIgnoreCase)) {
          originRelatedId = field.Value;
        }

      }

      if (origin == null || originRelatedId == null) {
        return null;
      }

      WorkItemIdentity key = new WorkItemIdentity();
      key.Origin = Convert.ToString(origin);
      key.OriginRelatedId = Convert.ToString(originRelatedId);

      return key;
    }

    /// <summary>
    /// Writes the given fields via the unified field access of the contract (converts the values to the property types).
    /// </summary>
    private static void ApplyFields(WorkItem entity, Dictionary<string, object> fields) {

      foreach (KeyValuePair<string, object> field in fields) {
        entity[field.Key] = field.Value;
      }

    }

    private static Dictionary<string, object> GetDeviatingFields(WorkItem entity, Dictionary<string, object> fields) {

      Dictionary<string, object> result = new Dictionary<string, object>();

      foreach (KeyValuePair<string, object> field in fields) {

        object storedValue = entity[field.Key];

        if (!JToken.DeepEquals(ToToken(storedValue), ToToken(field.Value))) {
          result[field.Key] = storedValue;
        }

      }

      return result;
    }

    private static JToken ToToken(object value) {

      if (value == null) {
        return JValue.CreateNull();
      }

      return JToken.FromObject(value);
    }

    #endregion

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
