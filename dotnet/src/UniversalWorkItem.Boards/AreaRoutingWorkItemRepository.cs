using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Fuse;
using System.Data.Fuse.LinqSupport;
using System.Linq;

namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// Source routing decorator (composite over N WorkItem repositories) implementing the MVP rule D14:
  /// the ROOT SEGMENT of the 'ConcernArea' represents the addressed source repository 1:1.
  /// </summary>
  /// <remarks>
  ///   * Reads fan out to all sources; the root segment of the source is prepended to 'ConcernArea'
  ///     ('/ui/forms' of source 'repo-a' becomes '/repo-a/ui/forms').
  ///   * Creates are routed by the root segment of 'ConcernArea' (the root segment is removed before forwarding).
  ///   * Updates / deletes are routed by the key ('Origin', fallback: asking every source).
  ///   * Changing the root segment of an existing card is rejected (no card moves in the MVP).
  ///   * Filters on 'ConcernArea' are not supported yet (they would have to be translated per source).
  ///   * Entities returned by the sources are copied before the area is rewritten, so the sources are never mutated.
  ///   This class will be refined after the MVP (epic Q14: dynamic routing, authentication, tenancy).
  /// </remarks>
  public class AreaRoutingWorkItemRepository : IWorkItemRepository {

    private const string _ConcernAreaFieldName = "ConcernArea";
    private const string _OriginFieldName = "Origin";
    private const string _OriginRelatedIdFieldName = "OriginRelatedId";

    private readonly string _OriginIdentity;
    private readonly List<RoutedSource> _Sources = new List<RoutedSource>();

    /// <summary>
    /// Creates an empty routing repository; sources are added via <see cref="AddSource"/>.
    /// </summary>
    /// <param name="originIdentity">The identity of the composed board data source (returned by GetOriginIdentity).</param>
    public AreaRoutingWorkItemRepository(string originIdentity) {
      _OriginIdentity = originIdentity;
    }

    #region " Configuration "

    /// <summary>
    /// Adds a source repository which is addressed by the given root area segment.
    /// </summary>
    /// <param name="rootSegment">Root segment of 'ConcernArea' (without slashes), e.g. the repository name.</param>
    /// <param name="origin">The 'Origin' value of the work items provided by this source (used for key routing).</param>
    /// <param name="repository">The source repository.</param>
    public void AddSource(string rootSegment, string origin, IWorkItemRepository repository) {

      string normalizedRoot = NormalizeRootSegment(rootSegment);

      if (repository == null) {
        throw new ArgumentNullException(nameof(repository));
      }

      lock (_Sources) {

        foreach (RoutedSource existing in _Sources) {
          if (string.Equals(existing.RootSegment, normalizedRoot, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"The root area segment '{normalizedRoot}' is already registered.");
          }
        }

        _Sources.Add(new RoutedSource(normalizedRoot, origin, repository));
      }

    }

    /// <summary>
    /// Returns the root area segments of all registered sources.
    /// </summary>
    public string[] GetRootSegments() {

      return this.GetSources().Select((source) => source.RootSegment).ToArray();

    }

    private RoutedSource[] GetSources() {

      lock (_Sources) {
        return _Sources.ToArray();
      }

    }

    private static string NormalizeRootSegment(string rootSegment) {

      if (string.IsNullOrWhiteSpace(rootSegment)) {
        throw new ArgumentException("The root segment must not be empty.", nameof(rootSegment));
      }

      string normalized = rootSegment.Trim().Trim('/');

      if (normalized.Length == 0 || normalized.Contains("/")) {
        throw new ArgumentException($"Invalid root segment '{rootSegment}': it must be exactly one path segment.", nameof(rootSegment));
      }

      return normalized;
    }

    #endregion

    #region " Area Translation "

    /// <summary>
    /// Converts a source-related area ('/ui') into a board-related area ('/root/ui').
    /// </summary>
    private static string ToBoardArea(RoutedSource source, string sourceArea) {

      string relativePath = "";

      if (!string.IsNullOrWhiteSpace(sourceArea)) {
        relativePath = sourceArea.Trim().Trim('/');
      }

      if (relativePath.Length == 0) {
        return "/" + source.RootSegment;
      }

      return "/" + source.RootSegment + "/" + relativePath;
    }

    /// <summary>
    /// Splits a board-related area ('/root/ui/forms') into its root segment ('root') and the
    /// source-related area ('/ui/forms').
    /// </summary>
    private static string SplitBoardArea(string boardArea, out string sourceArea) {

      sourceArea = "/";

      if (string.IsNullOrWhiteSpace(boardArea)) {
        return null;
      }

      string trimmed = boardArea.Trim().Trim('/');

      if (trimmed.Length == 0) {
        return null;
      }

      int separatorIndex = trimmed.IndexOf('/');

      if (separatorIndex < 0) {
        return trimmed;
      }

      sourceArea = "/" + trimmed.Substring(separatorIndex + 1);

      return trimmed.Substring(0, separatorIndex);
    }

    private RoutedSource ResolveSourceByBoardArea(string boardArea, out string sourceArea) {

      string rootSegment = SplitBoardArea(boardArea, out sourceArea);

      if (rootSegment == null) {
        throw new InvalidOperationException(
          $"The '{_ConcernAreaFieldName}' must start with the root segment of a source (one of: {string.Join(", ", this.GetRootSegments())})."
        );
      }

      foreach (RoutedSource source in this.GetSources()) {
        if (string.Equals(source.RootSegment, rootSegment, StringComparison.OrdinalIgnoreCase)) {
          return source;
        }
      }

      throw new InvalidOperationException(
        $"Unknown root area segment '{rootSegment}' (known: {string.Join(", ", this.GetRootSegments())})."
      );
    }

    /// <summary>
    /// Returns the source-related area for an existing card of the given source and rejects root segment changes.
    /// </summary>
    private static string ToSourceAreaOfSameSource(RoutedSource source, string boardArea) {

      string sourceArea;
      string rootSegment = SplitBoardArea(boardArea, out sourceArea);

      if (!string.Equals(rootSegment, source.RootSegment, StringComparison.OrdinalIgnoreCase)) {
        throw new InvalidOperationException(
          $"Moving a card between sources is not supported in the MVP (D14): the root area segment must stay '{source.RootSegment}'."
        );
      }

      return sourceArea;
    }

    private static WorkItem CopyEntity(WorkItem entity) {

      string json = JsonConvert.SerializeObject(entity);

      return JsonConvert.DeserializeObject<WorkItem>(json);
    }

    private static WorkItem ToBoardEntity(RoutedSource source, WorkItem sourceEntity) {

      if (sourceEntity == null) {
        return null;
      }

      WorkItem boardEntity = CopyEntity(sourceEntity);
      boardEntity.ConcernArea = ToBoardArea(source, sourceEntity.ConcernArea);

      return boardEntity;
    }

    private static Dictionary<string, object> ToBoardFields(RoutedSource source, Dictionary<string, object> sourceFields) {

      if (sourceFields == null) {
        return null;
      }

      Dictionary<string, object> boardFields = new Dictionary<string, object>(sourceFields);
      string areaKey = FindFieldName(boardFields, _ConcernAreaFieldName);

      if (areaKey != null) {
        boardFields[areaKey] = ToBoardArea(source, Convert.ToString(boardFields[areaKey]));
      }

      return boardFields;
    }

    private static string FindFieldName(Dictionary<string, object> fields, string fieldName) {

      foreach (string key in fields.Keys) {
        if (string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase)) {
          return key;
        }
      }

      return null;
    }

    private static WorkItemIdentity CopyKey(WorkItemIdentity key) {

      WorkItemIdentity copy = new WorkItemIdentity();
      copy.Origin = key.Origin;
      copy.OriginRelatedId = key.OriginRelatedId;

      return copy;
    }

    #endregion

    #region " Key Routing "

    private RoutedSource TryResolveSourceByKey(WorkItemIdentity key) {

      if (key == null) {
        return null;
      }

      RoutedSource[] sources = this.GetSources();

      foreach (RoutedSource source in sources) {
        if (source.Origin != null && string.Equals(source.Origin, key.Origin, StringComparison.OrdinalIgnoreCase)) {
          return source;
        }
      }

      foreach (RoutedSource source in sources) {
        if (source.Repository.ContainsKey(key)) {
          return source;
        }
      }

      return null;
    }

    private static WorkItemIdentity GetKeyFromFields(Dictionary<string, object> fields) {

      string originKey = FindFieldName(fields, _OriginFieldName);
      string idKey = FindFieldName(fields, _OriginRelatedIdFieldName);

      if (originKey == null || idKey == null) {
        return null;
      }

      WorkItemIdentity key = new WorkItemIdentity();
      key.Origin = Convert.ToString(fields[originKey]);
      key.OriginRelatedId = Convert.ToString(fields[idKey]);

      return key;
    }

    /// <summary>
    /// Groups the given keys by their source and keeps the original positions (for order-preserving results).
    /// </summary>
    private Dictionary<RoutedSource, List<int>> GroupKeysBySource(WorkItemIdentity[] keys) {

      Dictionary<RoutedSource, List<int>> result = new Dictionary<RoutedSource, List<int>>();

      for (int i = 0; i < keys.Length; i++) {

        RoutedSource source = this.TryResolveSourceByKey(keys[i]);

        if (source == null) {
          continue;
        }

        List<int> positions;
        if (!result.TryGetValue(source, out positions)) {
          positions = new List<int>();
          result[source] = positions;
        }

        positions.Add(i);
      }

      return result;
    }

    #endregion

    #region " Filter Validation "

    private static void EnsureFilterIsRoutable(ExpressionTree filter) {

      if (filter == null) {
        return;
      }

      if (filter.Predicates != null) {
        foreach (FieldPredicate predicate in filter.Predicates) {
          if (string.Equals(predicate.FieldName, _ConcernAreaFieldName, StringComparison.OrdinalIgnoreCase)) {
            throw new NotSupportedException(
              $"Filtering by '{_ConcernAreaFieldName}' is not supported by the MVP area routing (D14)."
            );
          }
        }
      }

      if (filter.SubTree != null) {
        foreach (ExpressionTree subTree in filter.SubTree) {
          EnsureFilterIsRoutable(subTree);
        }
      }

    }

    private static void EnsureNoAreaChange(Dictionary<string, object> fields) {

      if (fields != null && FindFieldName(fields, _ConcernAreaFieldName) != null) {
        throw new NotSupportedException(
          $"Mass updates of '{_ConcernAreaFieldName}' are not supported by the MVP area routing (D14)."
        );
      }

    }

    #endregion

    #region " Read (fan out) "

    /// <summary>
    /// Loads 'skip + limit' entities from every source, merges and sorts them and applies paging afterwards.
    /// </summary>
    private WorkItem[] FanOutAndPage(Func<IWorkItemRepository, int, WorkItem[]> loadFromSource, string[] sortedBy, int limit, int skip) {

      int perSourceLimit = limit + skip;
      List<WorkItem> merged = new List<WorkItem>();

      foreach (RoutedSource source in this.GetSources()) {

        WorkItem[] sourceEntities = loadFromSource.Invoke(source.Repository, perSourceLimit);

        foreach (WorkItem sourceEntity in sourceEntities) {
          merged.Add(ToBoardEntity(source, sourceEntity));
        }

      }

      IEnumerable<WorkItem> sorted = merged;

      if (sortedBy != null && sortedBy.Length > 0) {
        sorted = merged.ApplySorting(sortedBy);
      }

      return sorted.Skip(skip).Take(limit).ToArray();
    }

    /// <summary>
    /// Projects an entity to a field dictionary (all fields, if no field names are given).
    /// </summary>
    private static Dictionary<string, object> ToFieldDictionary(WorkItem entity, string[] includedFieldNames) {

      if (entity == null) {
        return null;
      }

      if (includedFieldNames == null || includedFieldNames.Length == 0) {
        return JObject.FromObject(entity).ToObject<Dictionary<string, object>>();
      }

      Dictionary<string, object> fields = new Dictionary<string, object>();

      foreach (string fieldName in includedFieldNames) {
        fields[fieldName] = entity[fieldName];
      }

      return fields;
    }

    private static EntityRef<WorkItemIdentity> ToEntityRef(WorkItem entity) {

      if (entity == null) {
        return null;
      }

      return new EntityRef<WorkItemIdentity>(CopyKey(entity), entity.Title);
    }

    public WorkItem[] GetEntities(ExpressionTree filter, string[] sortedBy, int limit = 100, int skip = 0) {

      EnsureFilterIsRoutable(filter);

      return this.FanOutAndPage(
        (repository, perSourceLimit) => repository.GetEntities(filter, sortedBy, perSourceLimit, 0),
        sortedBy, limit, skip
      );
    }

    public WorkItem[] GetEntitiesBySearchExpression(string searchExpression, string[] sortedBy, int limit = 100, int skip = 0) {

      return this.FanOutAndPage(
        (repository, perSourceLimit) => repository.GetEntitiesBySearchExpression(searchExpression, sortedBy, perSourceLimit, 0),
        sortedBy, limit, skip
      );
    }

    public WorkItem[] GetEntitiesByKey(WorkItemIdentity[] keysToLoad) {

      WorkItem[] result = new WorkItem[keysToLoad.Length];
      Dictionary<RoutedSource, List<int>> positionsBySource = this.GroupKeysBySource(keysToLoad);

      foreach (KeyValuePair<RoutedSource, List<int>> entry in positionsBySource) {

        WorkItemIdentity[] sourceKeys = entry.Value.Select((position) => keysToLoad[position]).ToArray();
        WorkItem[] sourceEntities = entry.Key.Repository.GetEntitiesByKey(sourceKeys);

        for (int i = 0; i < sourceKeys.Length; i++) {
          result[entry.Value[i]] = ToBoardEntity(entry.Key, sourceEntities[i]);
        }

      }

      return result;
    }

    public Dictionary<string, object>[] GetEntityFields(ExpressionTree filter, string[] includedFieldNames, string[] sortedBy, int limit = 100, int skip = 0) {

      return this.GetEntities(filter, sortedBy, limit, skip).Select(
        (entity) => ToFieldDictionary(entity, includedFieldNames)
      ).ToArray();
    }

    public Dictionary<string, object>[] GetEntityFieldsBySearchExpression(string searchExpression, string[] includedFieldNames, string[] sortedBy, int limit = 100, int skip = 0) {

      return this.GetEntitiesBySearchExpression(searchExpression, sortedBy, limit, skip).Select(
        (entity) => ToFieldDictionary(entity, includedFieldNames)
      ).ToArray();
    }

    public Dictionary<string, object>[] GetEntityFieldsByKey(WorkItemIdentity[] keysToLoad, string[] includedFieldNames) {

      return this.GetEntitiesByKey(keysToLoad).Select(
        (entity) => ToFieldDictionary(entity, includedFieldNames)
      ).ToArray();
    }

    public EntityRef<WorkItemIdentity>[] GetEntityRefs(ExpressionTree filter, string[] sortedBy, int limit = 100, int skip = 0) {

      return this.GetEntities(filter, sortedBy, limit, skip).Select(
        (entity) => ToEntityRef(entity)
      ).ToArray();
    }

    public EntityRef<WorkItemIdentity>[] GetEntityRefsBySearchExpression(string searchExpression, string[] sortedBy, int limit = 100, int skip = 0) {

      return this.GetEntitiesBySearchExpression(searchExpression, sortedBy, limit, skip).Select(
        (entity) => ToEntityRef(entity)
      ).ToArray();
    }

    public EntityRef<WorkItemIdentity>[] GetEntityRefsByKey(WorkItemIdentity[] keysToLoad) {

      return this.GetEntitiesByKey(keysToLoad).Select(
        (entity) => ToEntityRef(entity)
      ).ToArray();
    }

    public bool ContainsKey(WorkItemIdentity key) {

      return this.TryResolveSourceByKey(key) != null;

    }

    public int Count(ExpressionTree filter) {

      EnsureFilterIsRoutable(filter);

      return this.GetSources().Sum((source) => source.Repository.Count(filter));
    }

    public int CountAll() {

      return this.GetSources().Sum((source) => source.Repository.CountAll());

    }

    public int CountBySearchExpression(string searchExpression) {

      return this.GetSources().Sum((source) => source.Repository.CountBySearchExpression(searchExpression));

    }

    #endregion

    #region " Write (routed) "

    public WorkItem AddOrUpdateEntity(WorkItem entity) {

      if (entity == null) {
        throw new ArgumentNullException(nameof(entity));
      }

      RoutedSource existingSource = this.TryResolveSourceByKey(entity);

      if (existingSource != null && existingSource.Repository.ContainsKey(entity)) {
        return this.TryUpdateEntity(entity);
      }

      string sourceArea;
      RoutedSource targetSource = this.ResolveSourceByBoardArea(entity.ConcernArea, out sourceArea);

      WorkItem sourceEntity = CopyEntity(entity);
      sourceEntity.ConcernArea = sourceArea;

      return ToBoardEntity(targetSource, targetSource.Repository.AddOrUpdateEntity(sourceEntity));
    }

    public WorkItemIdentity TryAddEntity(WorkItem entity) {

      if (entity == null) {
        throw new ArgumentNullException(nameof(entity));
      }

      string sourceArea;
      RoutedSource targetSource = this.ResolveSourceByBoardArea(entity.ConcernArea, out sourceArea);

      WorkItem sourceEntity = CopyEntity(entity);
      sourceEntity.ConcernArea = sourceArea;

      return targetSource.Repository.TryAddEntity(sourceEntity);
    }

    public WorkItem TryUpdateEntity(WorkItem entity) {

      if (entity == null) {
        throw new ArgumentNullException(nameof(entity));
      }

      RoutedSource source = this.TryResolveSourceByKey(entity);

      if (source == null) {
        return null;
      }

      WorkItem sourceEntity = CopyEntity(entity);
      sourceEntity.ConcernArea = ToSourceAreaOfSameSource(source, entity.ConcernArea);

      return ToBoardEntity(source, source.Repository.TryUpdateEntity(sourceEntity));
    }

    public Dictionary<string, object> TryUpdateEntityFields(Dictionary<string, object> fields) {

      if (fields == null) {
        throw new ArgumentNullException(nameof(fields));
      }

      WorkItemIdentity key = GetKeyFromFields(fields);

      if (key == null) {
        throw new ArgumentException("The given fields must contain the key fields 'Origin' and 'OriginRelatedId'.", nameof(fields));
      }

      RoutedSource source = this.TryResolveSourceByKey(key);

      if (source == null) {
        return null;
      }

      Dictionary<string, object> sourceFields = new Dictionary<string, object>(fields);
      string areaKey = FindFieldName(sourceFields, _ConcernAreaFieldName);

      if (areaKey != null) {
        sourceFields[areaKey] = ToSourceAreaOfSameSource(source, Convert.ToString(sourceFields[areaKey]));
      }

      return ToBoardFields(source, source.Repository.TryUpdateEntityFields(sourceFields));
    }

    public Dictionary<string, object> AddOrUpdateEntityFields(Dictionary<string, object> fields) {

      if (fields == null) {
        throw new ArgumentNullException(nameof(fields));
      }

      WorkItemIdentity key = GetKeyFromFields(fields);
      RoutedSource existingSource = this.TryResolveSourceByKey(key);

      if (existingSource != null && existingSource.Repository.ContainsKey(key)) {
        return this.TryUpdateEntityFields(fields);
      }

      string areaKey = FindFieldName(fields, _ConcernAreaFieldName);

      if (areaKey == null) {
        throw new ArgumentException(
          $"New cards must provide a '{_ConcernAreaFieldName}' starting with the root segment of a source.", nameof(fields)
        );
      }

      string sourceArea;
      RoutedSource targetSource = this.ResolveSourceByBoardArea(Convert.ToString(fields[areaKey]), out sourceArea);

      Dictionary<string, object> sourceFields = new Dictionary<string, object>(fields);
      sourceFields[areaKey] = sourceArea;

      return ToBoardFields(targetSource, targetSource.Repository.AddOrUpdateEntityFields(sourceFields));
    }

    public WorkItemIdentity[] Massupdate(ExpressionTree filter, Dictionary<string, object> fields) {

      EnsureFilterIsRoutable(filter);
      EnsureNoAreaChange(fields);

      List<WorkItemIdentity> updatedKeys = new List<WorkItemIdentity>();

      foreach (RoutedSource source in this.GetSources()) {
        updatedKeys.AddRange(source.Repository.Massupdate(filter, fields));
      }

      return updatedKeys.ToArray();
    }

    public WorkItemIdentity[] MassupdateBySearchExpression(string searchExpression, Dictionary<string, object> fields) {

      EnsureNoAreaChange(fields);

      List<WorkItemIdentity> updatedKeys = new List<WorkItemIdentity>();

      foreach (RoutedSource source in this.GetSources()) {
        updatedKeys.AddRange(source.Repository.MassupdateBySearchExpression(searchExpression, fields));
      }

      return updatedKeys.ToArray();
    }

    public WorkItemIdentity[] MassupdateByKeys(WorkItemIdentity[] keysToUpdate, Dictionary<string, object> fields) {

      EnsureNoAreaChange(fields);

      List<WorkItemIdentity> updatedKeys = new List<WorkItemIdentity>();
      Dictionary<RoutedSource, List<int>> positionsBySource = this.GroupKeysBySource(keysToUpdate);

      foreach (KeyValuePair<RoutedSource, List<int>> entry in positionsBySource) {

        WorkItemIdentity[] sourceKeys = entry.Value.Select((position) => keysToUpdate[position]).ToArray();
        updatedKeys.AddRange(entry.Key.Repository.MassupdateByKeys(sourceKeys, fields));

      }

      return updatedKeys.ToArray();
    }

    public WorkItemIdentity[] TryDeleteEntities(WorkItemIdentity[] keysToDelete) {

      List<WorkItemIdentity> deletedKeys = new List<WorkItemIdentity>();
      Dictionary<RoutedSource, List<int>> positionsBySource = this.GroupKeysBySource(keysToDelete);

      foreach (KeyValuePair<RoutedSource, List<int>> entry in positionsBySource) {

        WorkItemIdentity[] sourceKeys = entry.Value.Select((position) => keysToDelete[position]).ToArray();
        deletedKeys.AddRange(entry.Key.Repository.TryDeleteEntities(sourceKeys));

      }

      return deletedKeys.ToArray();
    }

    public bool TryUpdateKey(WorkItemIdentity currentKey, WorkItemIdentity newKey) {

      RoutedSource source = this.TryResolveSourceByKey(currentKey);

      if (source == null) {
        return false;
      }

      if (source.Origin != null && newKey != null && !string.Equals(source.Origin, newKey.Origin, StringComparison.OrdinalIgnoreCase)) {
        throw new InvalidOperationException("Changing the 'Origin' of a card is not supported (it would move the card to another source).");
      }

      return source.Repository.TryUpdateKey(currentKey, newKey);
    }

    #endregion

    #region " Meta "

    /// <summary>
    /// Returns the capabilities shared by ALL sources (AND), except 'RequiresExternalKeys' (OR).
    /// </summary>
    public RepositoryCapabilities GetCapabilities() {

      RepositoryCapabilities result = new RepositoryCapabilities();
      RoutedSource[] sources = this.GetSources();

      result.CanReadContent = true;
      result.CanUpdateContent = sources.Length > 0;
      result.CanAddNewEntities = sources.Length > 0;
      result.CanDeleteEntities = sources.Length > 0;
      result.SupportsMassupdate = sources.Length > 0;
      result.SupportsKeyUpdate = sources.Length > 0;
      result.SupportsStringBasedSearchExpressions = sources.Length > 0;
      result.RequiresExternalKeys = false;

      foreach (RoutedSource source in sources) {

        RepositoryCapabilities sourceCapabilities = source.Repository.GetCapabilities();

        result.CanReadContent = result.CanReadContent && sourceCapabilities.CanReadContent;
        result.CanUpdateContent = result.CanUpdateContent && sourceCapabilities.CanUpdateContent;
        result.CanAddNewEntities = result.CanAddNewEntities && sourceCapabilities.CanAddNewEntities;
        result.CanDeleteEntities = result.CanDeleteEntities && sourceCapabilities.CanDeleteEntities;
        result.SupportsMassupdate = result.SupportsMassupdate && sourceCapabilities.SupportsMassupdate;
        result.SupportsKeyUpdate = result.SupportsKeyUpdate && sourceCapabilities.SupportsKeyUpdate;
        result.SupportsStringBasedSearchExpressions = result.SupportsStringBasedSearchExpressions && sourceCapabilities.SupportsStringBasedSearchExpressions;
        result.RequiresExternalKeys = result.RequiresExternalKeys || sourceCapabilities.RequiresExternalKeys;

      }

      return result;
    }

    public string GetOriginIdentity() {

      return _OriginIdentity;

    }

    #endregion

    /// <summary>
    /// A source repository together with its root area segment and origin.
    /// </summary>
    private sealed class RoutedSource {

      private readonly string _RootSegment;
      private readonly string _Origin;
      private readonly IWorkItemRepository _Repository;

      public RoutedSource(string rootSegment, string origin, IWorkItemRepository repository) {
        _RootSegment = rootSegment;
        _Origin = origin;
        _Repository = repository;
      }

      public string RootSegment {
        get {
          return _RootSegment;
        }
      }

      public string Origin {
        get {
          return _Origin;
        }
      }

      public IWorkItemRepository Repository {
        get {
          return _Repository;
        }
      }

    }

  }

}
