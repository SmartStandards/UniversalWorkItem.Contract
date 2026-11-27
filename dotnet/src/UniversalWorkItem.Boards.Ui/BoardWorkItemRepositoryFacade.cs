using System;
using System.Collections.Generic;
using System.Data.Fuse;
using System.Threading;

namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// The single <see cref="IWorkItemRepository"/> instance published via UJMW. Every call is forwarded to the
  /// repository of the board which is addressed by the last route segment of the current request
  /// (provided by the UJMW contextualization hook, see <see cref="BoardUiSetupExtensions"/>).
  /// </summary>
  public class BoardWorkItemRepositoryFacade : IWorkItemRepository {

    private static readonly AsyncLocal<string> _CurrentBoardId = new AsyncLocal<string>();

    private readonly BoardRegistry _Registry;

    public BoardWorkItemRepositoryFacade(BoardRegistry registry) {

      if (registry == null) {
        throw new ArgumentNullException(nameof(registry));
      }

      _Registry = registry;
    }

    /// <summary>
    /// Executes the given action with the given board as current board.
    /// </summary>
    public static void RunForBoard(string boardId, Action action) {

      string previousBoardId = _CurrentBoardId.Value;
      _CurrentBoardId.Value = boardId;

      try {
        action.Invoke();
      }
      finally {
        _CurrentBoardId.Value = previousBoardId;
      }

    }

    private IWorkItemRepository CurrentRepository {
      get {

        string boardId = _CurrentBoardId.Value;
        BoardRegistration board;

        if (!_Registry.TryGetBoard(boardId, out board)) {
          throw new InvalidOperationException($"Unknown board '{boardId}'.");
        }

        return board.Repository;
      }
    }

    public WorkItem AddOrUpdateEntity(WorkItem entity) {
      return this.CurrentRepository.AddOrUpdateEntity(entity);
    }

    public Dictionary<string, object> AddOrUpdateEntityFields(Dictionary<string, object> fields) {
      return this.CurrentRepository.AddOrUpdateEntityFields(fields);
    }

    public bool ContainsKey(WorkItemIdentity key) {
      return this.CurrentRepository.ContainsKey(key);
    }

    public int Count(ExpressionTree filter) {
      return this.CurrentRepository.Count(filter);
    }

    public int CountAll() {
      return this.CurrentRepository.CountAll();
    }

    public int CountBySearchExpression(string searchExpression) {
      return this.CurrentRepository.CountBySearchExpression(searchExpression);
    }

    public RepositoryCapabilities GetCapabilities() {
      return this.CurrentRepository.GetCapabilities();
    }

    public WorkItem[] GetEntities(ExpressionTree filter, string[] sortedBy, int limit = 100, int skip = 0) {
      return this.CurrentRepository.GetEntities(filter, sortedBy, limit, skip);
    }

    public WorkItem[] GetEntitiesByKey(WorkItemIdentity[] keysToLoad) {
      return this.CurrentRepository.GetEntitiesByKey(keysToLoad);
    }

    public WorkItem[] GetEntitiesBySearchExpression(string searchExpression, string[] sortedBy, int limit = 100, int skip = 0) {
      return this.CurrentRepository.GetEntitiesBySearchExpression(searchExpression, sortedBy, limit, skip);
    }

    public Dictionary<string, object>[] GetEntityFields(ExpressionTree filter, string[] includedFieldNames, string[] sortedBy, int limit = 100, int skip = 0) {
      return this.CurrentRepository.GetEntityFields(filter, includedFieldNames, sortedBy, limit, skip);
    }

    public Dictionary<string, object>[] GetEntityFieldsByKey(WorkItemIdentity[] keysToLoad, string[] includedFieldNames) {
      return this.CurrentRepository.GetEntityFieldsByKey(keysToLoad, includedFieldNames);
    }

    public Dictionary<string, object>[] GetEntityFieldsBySearchExpression(string searchExpression, string[] includedFieldNames, string[] sortedBy, int limit = 100, int skip = 0) {
      return this.CurrentRepository.GetEntityFieldsBySearchExpression(searchExpression, includedFieldNames, sortedBy, limit, skip);
    }

    public EntityRef<WorkItemIdentity>[] GetEntityRefs(ExpressionTree filter, string[] sortedBy, int limit = 100, int skip = 0) {
      return this.CurrentRepository.GetEntityRefs(filter, sortedBy, limit, skip);
    }

    public EntityRef<WorkItemIdentity>[] GetEntityRefsByKey(WorkItemIdentity[] keysToLoad) {
      return this.CurrentRepository.GetEntityRefsByKey(keysToLoad);
    }

    public EntityRef<WorkItemIdentity>[] GetEntityRefsBySearchExpression(string searchExpression, string[] sortedBy, int limit = 100, int skip = 0) {
      return this.CurrentRepository.GetEntityRefsBySearchExpression(searchExpression, sortedBy, limit, skip);
    }

    public string GetOriginIdentity() {
      return this.CurrentRepository.GetOriginIdentity();
    }

    public WorkItemIdentity[] Massupdate(ExpressionTree filter, Dictionary<string, object> fields) {
      return this.CurrentRepository.Massupdate(filter, fields);
    }

    public WorkItemIdentity[] MassupdateByKeys(WorkItemIdentity[] keysToUpdate, Dictionary<string, object> fields) {
      return this.CurrentRepository.MassupdateByKeys(keysToUpdate, fields);
    }

    public WorkItemIdentity[] MassupdateBySearchExpression(string searchExpression, Dictionary<string, object> fields) {
      return this.CurrentRepository.MassupdateBySearchExpression(searchExpression, fields);
    }

    public WorkItemIdentity TryAddEntity(WorkItem entity) {
      return this.CurrentRepository.TryAddEntity(entity);
    }

    public WorkItemIdentity[] TryDeleteEntities(WorkItemIdentity[] keysToDelete) {
      return this.CurrentRepository.TryDeleteEntities(keysToDelete);
    }

    public WorkItem TryUpdateEntity(WorkItem entity) {
      return this.CurrentRepository.TryUpdateEntity(entity);
    }

    public Dictionary<string, object> TryUpdateEntityFields(Dictionary<string, object> fields) {
      return this.CurrentRepository.TryUpdateEntityFields(fields);
    }

    public bool TryUpdateKey(WorkItemIdentity currentKey, WorkItemIdentity newKey) {
      return this.CurrentRepository.TryUpdateKey(currentKey, newKey);
    }

  }

}
