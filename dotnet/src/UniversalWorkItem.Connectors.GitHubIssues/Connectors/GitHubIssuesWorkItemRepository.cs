using System;
using System.Collections.Generic;
using System.Data.Fuse;

namespace Collaboration.WorkTracking.Connectors.GitHubIssues {

  public class GitHubIssuesWorkItemRepository : IWorkItemRepository {

    public GitHubIssuesWorkItemRepository(string organisationName, string accessToken, string teamProjectFilter = null) {


    }

    public WorkItem AddOrUpdateEntity(WorkItem entity) {
      throw new NotImplementedException();
    }

    public Dictionary<string, object> AddOrUpdateEntityFields(Dictionary<string, object> fields) {
      throw new NotImplementedException();
    }

    public bool ContainsKey(WorkItemIdentity key) {
      throw new NotImplementedException();
    }

    public int Count(ExpressionTree filter) {
      throw new NotImplementedException();
    }

    public int CountAll() {
      throw new NotImplementedException();
    }

    public int CountBySearchExpression(string searchExpression) {
      throw new NotImplementedException();
    }

    public RepositoryCapabilities GetCapabilities() {
      throw new NotImplementedException();
    }

    public WorkItem[] GetEntities(ExpressionTree filter, string[] sortedBy, int limit = 100, int skip = 0) {
      throw new NotImplementedException();
    }

    public WorkItem[] GetEntitiesByKey(WorkItemIdentity[] keysToLoad) {
      throw new NotImplementedException();
    }

    public WorkItem[] GetEntitiesBySearchExpression(string searchExpression, string[] sortedBy, int limit = 100, int skip = 0) {
      throw new NotImplementedException();
    }

    public Dictionary<string, object>[] GetEntityFields(ExpressionTree filter, string[] includedFieldNames, string[] sortedBy, int limit = 100, int skip = 0) {
      throw new NotImplementedException();
    }

    public Dictionary<string, object>[] GetEntityFieldsByKey(WorkItemIdentity[] keysToLoad, string[] includedFieldNames) {
      throw new NotImplementedException();
    }

    public Dictionary<string, object>[] GetEntityFieldsBySearchExpression(string searchExpression, string[] includedFieldNames, string[] sortedBy, int limit = 100, int skip = 0) {
      throw new NotImplementedException();
    }

    public EntityRef<WorkItemIdentity>[] GetEntityRefs(ExpressionTree filter, string[] sortedBy, int limit = 100, int skip = 0) {
      throw new NotImplementedException();
    }

    public EntityRef<WorkItemIdentity>[] GetEntityRefsByKey(WorkItemIdentity[] keysToLoad) {
      throw new NotImplementedException();
    }

    public EntityRef<WorkItemIdentity>[] GetEntityRefsBySearchExpression(string searchExpression, string[] sortedBy, int limit = 100, int skip = 0) {
      throw new NotImplementedException();
    }

    public string GetOriginIdentity() {
      throw new NotImplementedException();
    }

    public WorkItemIdentity[] Massupdate(ExpressionTree filter, Dictionary<string, object> fields) {
      throw new NotImplementedException();
    }

    public WorkItemIdentity[] MassupdateByKeys(WorkItemIdentity[] keysToUpdate, Dictionary<string, object> fields) {
      throw new NotImplementedException();
    }

    public WorkItemIdentity[] MassupdateBySearchExpression(string searchExpression, Dictionary<string, object> fields) {
      throw new NotImplementedException();
    }

    public WorkItemIdentity TryAddEntity(WorkItem entity) {
      throw new NotImplementedException();
    }

    public WorkItemIdentity[] TryDeleteEntities(WorkItemIdentity[] keysToDelete) {
      throw new NotImplementedException();
    }

    public WorkItem TryUpdateEntity(WorkItem entity) {
      throw new NotImplementedException();
    }

    public Dictionary<string, object> TryUpdateEntityFields(Dictionary<string, object> fields) {
      throw new NotImplementedException();
    }

    public bool TryUpdateKey(WorkItemIdentity currentKey, WorkItemIdentity newKey) {
      throw new NotImplementedException();
    }
  }

}
