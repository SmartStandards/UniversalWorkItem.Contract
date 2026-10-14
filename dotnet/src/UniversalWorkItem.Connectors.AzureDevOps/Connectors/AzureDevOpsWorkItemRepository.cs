using Logging.SmartStandards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Fuse;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;

#if NET5_0_OR_GREATER

namespace Collaboration.WorkTracking.Connectors.AzureDevOps {

  public sealed class AzureDevOpsWorkItemRepository : IWorkItemRepository {

    public static class KnownWorkItemTypes {
      public const string Epic = "AzureDevOps.Epic";
      public const string Feature = "AzureDevOps.Feature";
      public const string UserStory = "AzureDevOps.UserStory";
      public const string ChangeRequest = "AzureDevOps.ChangeRequest";
      public const string Issue = "AzureDevOps.Issue";
      public const string ProductBacklogItem = "AzureDevOps.ProductBacklogItem";
      public const string Bug = "AzureDevOps.Bug";
      public const string Task = "AzureDevOps.Task";
    }

    private readonly string _Origin;
    private readonly string _Project;
    private readonly string _DefaultWorkItemType;
    private readonly AzureDevOpsClient _Client;
    private readonly object _AdoFieldDefinitionsSync = new object();
    private Dictionary<string, AdoFieldDefinition> _AdoFieldDefinitions = null;

    private const string _AzureDevOpsClassPrefix = "AzureDevOps.";
    private const string _AdoFieldId = "System.Id";
    private const string _AdoFieldTitle = "System.Title";
    private const string _AdoFieldWorkItemType = "System.WorkItemType";
    private const string _AdoFieldState = "System.State";
    private const string _AdoFieldTeamProject = "System.TeamProject";
    private const string _AdoFieldAreaPath = "System.AreaPath";
    private const string _AdoFieldIterationPath = "System.IterationPath";
    private const string _AdoFieldDescription = "System.Description";
    private const string _AdoFieldTags = "System.Tags";
    private const string _AdoFieldAssignedTo = "System.AssignedTo";
    private const string _AdoFieldCreatedDate = "System.CreatedDate";
    private const string _AdoFieldChangedDate = "System.ChangedDate";
    private const string _AdoFieldCreatedBy = "System.CreatedBy";
    private const string _AdoFieldChangedBy = "System.ChangedBy";
    private const string _AdoFieldStackRank = "Microsoft.VSTS.Common.StackRank";
    private const string _AdoFieldRemainingWork = "Microsoft.VSTS.Scheduling.RemainingWork";
    private const string _AdoFieldCompletedWork = "Microsoft.VSTS.Scheduling.CompletedWork";

    private static readonly Dictionary<string, string> _StateMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      { "New", "New" },
      { "Working", "Active" },
      { "Suspended", "Removed" },
      { "Done", "Closed" },
      { "Discontinued", "Removed" }
    };

    private static readonly HashSet<string> _MappedAdoFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      _AdoFieldId,
      _AdoFieldTitle,
      _AdoFieldWorkItemType,
      _AdoFieldState,
      _AdoFieldTeamProject,
      _AdoFieldAreaPath,
      _AdoFieldDescription,
      _AdoFieldTags,
      _AdoFieldAssignedTo,
      _AdoFieldCreatedDate,
      _AdoFieldChangedDate,
      _AdoFieldCreatedBy,
      _AdoFieldChangedBy,
      _AdoFieldStackRank,
      _AdoFieldRemainingWork,
      _AdoFieldCompletedWork
    };

    private static readonly HashSet<string> _WritableSystemAdoFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      _AdoFieldTitle,
      _AdoFieldWorkItemType,
      _AdoFieldState,
      _AdoFieldAreaPath,
      _AdoFieldIterationPath,
      _AdoFieldDescription,
      _AdoFieldTags,
      _AdoFieldAssignedTo
    };

    /// <summary>
    /// 
    /// </summary>
    /// <param name="originBaseUrl">https://dev.azure.com/YOUR-ORG-NAME></param>
    /// <param name="personalAccessToken"></param>
    /// <param name="storageArea">YOUT-TEAM-PROJECT/OPTIONAL-SUB-AREA</param>
    /// <param name="defaultWorkItemType">For example: 'AzureDevOpsWorkItemRepository.KnownWorkItemTypes.ProductBacklogItem'</param>
    public AzureDevOpsWorkItemRepository(
      string originBaseUrl, string personalAccessToken, string storageArea, string defaultWorkItemType
    ) {
      _Origin = originBaseUrl;
      _Client = new AzureDevOpsClient(originBaseUrl, personalAccessToken);
      _Project = NormalizeStorageAreaToProject(storageArea);
      _DefaultWorkItemType = string.IsNullOrWhiteSpace(defaultWorkItemType) ? "Task" : defaultWorkItemType.Trim();
    }

    /// <summary>
    /// Extract project name from StorageArea "/ProjectX/".
    /// </summary>
    private static string NormalizeStorageAreaToProject(string storageArea) {
      if (string.IsNullOrWhiteSpace(storageArea)) {
        return "";
      }

      string trimmed = storageArea.Trim();
      if (trimmed.StartsWith("/", StringComparison.Ordinal)) {
        trimmed = trimmed.Substring(1);
      }
      if (trimmed.EndsWith("/", StringComparison.Ordinal)) {
        trimmed = trimmed.Substring(0, trimmed.Length - 1);
      }
      return trimmed;
    }

    // ------------------------------------------------------------
    // IRepository members
    // ------------------------------------------------------------

    /// <summary>
    /// Returns the technical origin identity (Base URL).
    /// </summary>
    public string GetOriginIdentity() {
      return _Origin;
    }

    /// <summary>
    /// Returns supported capabilities.
    /// </summary>
    public RepositoryCapabilities GetCapabilities() {
      return new RepositoryCapabilities {
        CanReadContent = true,
        CanUpdateContent = true,
        CanAddNewEntities = true,
        CanDeleteEntities = true,
        SupportsMassupdate = false,
        SupportsKeyUpdate = false,
        SupportsStringBasedSearchExpressions = false,
        RequiresExternalKeys = false
      };
    }

    /// <summary>
    /// Builds WIQL from ExpressionTree (subset of operators).
    /// </summary>
    private string BuildWiql(ExpressionTree filter, string[] sortedBy, int limit, int skip) {
      StringBuilder where = new StringBuilder();

      if (filter == null) {
        filter = ExpressionTree.Empty();
      }

      // Group predicates by field.
      List<string> fragments = new List<string>();
      foreach (FieldPredicate predicate in filter.Predicates) {
        string frag = ConvertPredicateToWiql(predicate);
        if (!string.IsNullOrEmpty(frag)) {
          fragments.Add(frag);
        }
      }

      string joinOp = filter.MatchAll ? " AND " : " OR ";
      string whereClause = string.Join(joinOp, fragments);

      if (string.IsNullOrEmpty(whereClause)) {
        whereClause = "1 = 1";
      }

      // Project scope
      string proj = string.IsNullOrEmpty(_Project) ? "" : " AND [System.TeamProject] = '" + _Project.Replace("'", "''") + "'";

      // ORDER BY
      string orderClause = "";
      if (sortedBy != null && sortedBy.Length > 0) {
        string[] orderParts = sortedBy.Select(x =>
        {
          string field = x;
          bool desc = false;
          if (x.StartsWith("^", StringComparison.Ordinal)) {
            desc = true;
            field = x.Substring(1);
          }
          string adoField = ConvertSortFieldToAdoField(field);
          if (desc) {
            return "[" + adoField + "] DESC";
          }
          return "[" + adoField + "] ASC";
        }).ToArray();

        if (orderParts.Length > 0) {
          orderClause = " ORDER BY " + string.Join(", ", orderParts);
        }
      }

      // LIMIT via $top (include skip, we will drop first 'skip' locally)
      int top = limit + skip;
      if (top <= 0) {
        top = 200;
      }

      string wiql = "SELECT [System.Id] FROM WorkItems WHERE " + whereClause + proj + orderClause;
      string wrapped = "SELECT TOP " + top.ToString(CultureInfo.InvariantCulture) + " [System.Id] FROM (" + wiql + ")";
      return wrapped;
    }

    /// <summary>
    /// Translates known sort aliases to ADO fields.
    /// </summary>
    private static string ConvertSortFieldToAdoField(string field) {
      string adoField = MapFieldNameToAdoField(field);
      if (!string.IsNullOrWhiteSpace(adoField)) {
        return adoField;
      }
      return _AdoFieldChangedDate;
    }

    /// <summary>
    /// Converts a FUSE FieldPredicate to a WIQL fragment (limited set).
    /// </summary>
    private static string ConvertPredicateToWiql(FieldPredicate p) {
      // Only a handful of fields are supported in WIQL here.
      string adoField = MapFieldNameToAdoField(p.FieldName);
      if (string.IsNullOrEmpty(adoField)) {
        return "";
      }

      string op = p.Operator;
      string valueStr = ConvertValueForWiql(p.Value);

      // Operators
      if (string.Equals(op, FieldOperators.Equal, StringComparison.Ordinal)) {
        return "[" + adoField + "] = " + valueStr;
      }
      if (string.Equals(op, FieldOperators.NotEqual, StringComparison.Ordinal)) {
        return "[" + adoField + "] <> " + valueStr;
      }
      if (string.Equals(op, FieldOperators.StartsWith, StringComparison.Ordinal)) {
        return "[" + adoField + "] CONTAINSWORDS " + valueStr;
      }
      if (string.Equals(op, FieldOperators.Contains, StringComparison.Ordinal) || string.Equals(op, FieldOperators.SubstringOf, StringComparison.Ordinal)) {
        return "[" + adoField + "] CONTAINS " + valueStr;
      }
      if (string.Equals(op, FieldOperators.Greater, StringComparison.Ordinal)) {
        return "[" + adoField + "] > " + valueStr;
      }
      if (string.Equals(op, FieldOperators.GreaterOrEqual, StringComparison.Ordinal)) {
        return "[" + adoField + "] >= " + valueStr;
      }
      if (string.Equals(op, FieldOperators.Less, StringComparison.Ordinal)) {
        return "[" + adoField + "] < " + valueStr;
      }
      if (string.Equals(op, FieldOperators.LessOrEqual, StringComparison.Ordinal)) {
        return "[" + adoField + "] <= " + valueStr;
      }
      if (string.Equals(op, FieldOperators.In, StringComparison.Ordinal)) {
        // Expect array of scalars
        if (p.Value is IEnumerable<object>) {
          IEnumerable<object> arr = (IEnumerable<object>)p.Value;
          string[] parts = arr.Select(ConvertValueForWiql).ToArray();
          return "[" + adoField + "] IN (" + string.Join(",", parts) + ")";
        }
      }

      return "";
    }

    /// <summary>
    /// Maps known model field names to ADO field references.
    /// </summary>
    private static string MapFieldNameToAdoField(string field) {
      if (string.Equals(field, nameof(WorkItem.Title), StringComparison.OrdinalIgnoreCase)) {
        return _AdoFieldTitle;
      }
      if (string.Equals(field, nameof(WorkItem.BodyContent), StringComparison.OrdinalIgnoreCase)) {
        return _AdoFieldDescription;
      }
      if (string.Equals(field, nameof(WorkItem.ProgressState), StringComparison.OrdinalIgnoreCase)) {
        return _AdoFieldState;
      }
      if (string.Equals(field, nameof(WorkItem.AssignedTo), StringComparison.OrdinalIgnoreCase)) {
        return _AdoFieldAssignedTo;
      }
      if (string.Equals(field, nameof(WorkItem.ConcernArea), StringComparison.OrdinalIgnoreCase)) {
        return _AdoFieldAreaPath;
      }
      if (string.Equals(field, nameof(WorkItem.Tags), StringComparison.OrdinalIgnoreCase)) {
        return _AdoFieldTags;
      }
      if (string.Equals(field, nameof(WorkItem.CreatedTimestamp), StringComparison.OrdinalIgnoreCase)) {
        return _AdoFieldCreatedDate;
      }
      if (string.Equals(field, nameof(WorkItem.ModifiedTimestamp), StringComparison.OrdinalIgnoreCase)) {
        return _AdoFieldChangedDate;
      }
      if (IsSafeAdoReferenceName(field)) {
        return field;
      }
      return null;
    }

    /// <summary>
    /// Produces a WIQL literal for a given value.
    /// </summary>
    private static string ConvertValueForWiql(object value) {

      if (value == null) {
        return "NULL";
      }

      if (value is string) {
        string s = (string)value;
        return "'" + s.Replace("'", "''") + "'";
      }

      if (value is DateTime) {
        DateTime dt = (DateTime)value;
        return "'" + FormatDateTimeForWiql(dt) + "'";
      }

      if (value is DateTimeOffset) {
        DateTimeOffset dt = (DateTimeOffset)value;
        return "'" + FormatDateTimeForWiql(dt.UtcDateTime) + "'";
      }

      if (value is long || value is int || value is double || value is decimal) {
        return Convert.ToString(value, CultureInfo.InvariantCulture);
      }

      return "'" + value.ToString().Replace("'", "''") + "'";
    }

    /// <summary>
    /// Formats a DateTime value for WIQL date comparisons.
    /// </summary>
    private static string FormatDateTimeForWiql(DateTime value) {
      DateTime normalized;
      if (value.Kind == DateTimeKind.Unspecified) {
        normalized = DateTime.SpecifyKind(value, DateTimeKind.Utc);
      }
      else {
        normalized = value.ToUniversalTime();
      }
      return normalized.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets EntityRefs by filter.
    /// </summary>
    public EntityRef<WorkItemIdentity>[] GetEntityRefs(ExpressionTree filter, string[] sortedBy, int limit = 100, int skip = 0) {
      WorkItem[] items = GetEntities(filter, sortedBy, limit, skip);
      EntityRef<WorkItemIdentity>[] refsArr = items.Select(ToEntityRef).ToArray();
      return refsArr;
    }

    /// <summary>
    /// NOT IMPLEMENTED: string search expressions (capability disabled).
    /// </summary>
    public EntityRef<WorkItemIdentity>[] GetEntityRefsBySearchExpression(string searchExpression, string[] sortedBy, int limit = 100, int skip = 0) {
      throw new NotSupportedException("String-based search expressions are not supported by this repository configuration.");
    }

    /// <summary>
    /// Reads EntityRefs by keys.
    /// </summary>
    public EntityRef<WorkItemIdentity>[] GetEntityRefsByKey(WorkItemIdentity[] keysToLoad) {
      WorkItem[] ents = GetEntitiesByKey(keysToLoad);
      EntityRef<WorkItemIdentity>[] refsArr = ents.Select(ToEntityRef).ToArray();
      return refsArr;
    }

    /// <summary>
    /// Loads WorkItems by filter using WIQL and field expansion.
    /// </summary>
    public WorkItem[] GetEntities(ExpressionTree filter, string[] sortedBy, int limit = 100, int skip = 0) {
      try {
        string wiql = BuildWiql(filter, sortedBy, limit, skip);
        DevLogger.LogTrace(0, 99999, "WIQL: " + wiql);

        string url = _Client.BuildProjectApiUrl(_Project, "wit/wiql?api-version=7.1-preview.2");
        JObject wiqlBody = new JObject {
          ["query"] = wiql
        };
        JObject wiqlResult = _Client.PostJson(url, wiqlBody);

        int[] ids = wiqlResult["workItems"].Select(t => (int)t["id"]).ToArray();
        if (ids.Length == 0) {
          return new WorkItem[0];
        }

        // Apply skip
        int[] sliced = ids.Skip(skip).ToArray();
        if (sliced.Length == 0) {
          return new WorkItem[0];
        }

        // Batch load fields
        WorkItem[] result = LoadWorkItemsByIds(sliced);
        return result;
      }
      catch (Exception ex) {
        DevLogger.LogCritical(ex);
        throw;
      }
    }

    /// <summary>
    /// NOT IMPLEMENTED: string search expressions (capability disabled).
    /// </summary>
    public WorkItem[] GetEntitiesBySearchExpression(string searchExpression, string[] sortedBy, int limit = 100, int skip = 0) {
      throw new NotSupportedException("String-based search expressions are not supported by this repository configuration.");
    }

    /// <summary>
    /// Loads WorkItems by Keys.
    /// </summary>
    public WorkItem[] GetEntitiesByKey(WorkItemIdentity[] keysToLoad) {
      if (keysToLoad == null || keysToLoad.Length == 0) {
        return new WorkItem[0];
      }

      try {
        int[] ids = keysToLoad.Where(k => k != null && !string.IsNullOrWhiteSpace(k.OriginRelatedId))
                              .Select(k => int.Parse(k.OriginRelatedId, CultureInfo.InvariantCulture))
                              .ToArray();
        WorkItem[] result = LoadWorkItemsByIds(ids);
        return result;
      }
      catch (Exception ex) {
        DevLogger.LogCritical(ex);
        throw;
      }
    }

    /// <summary>
    /// Loads a set of work items by numeric IDs with field and relation expansion.
    /// </summary>
    private WorkItem[] LoadWorkItemsByIds(int[] ids) {
      if (ids == null || ids.Length == 0) {
        return new WorkItem[0];
      }

      string url = _Client.BuildOrgApiUrl("wit/workitemsbatch?api-version=7.1-preview.1");
      JObject body = new JObject {
        ["ids"] = new JArray(ids),
        ["$expand"] = "Relations"
      };

      JObject result = _Client.PostJson(url, body);
      JArray value = (JArray)result["value"];
      WorkItem[] items = value.Select(v => FromAdoWorkItem((JObject)v)).ToArray();
      return items;
    }

    /// <summary>
    /// Returns a dictionary projection (subset) by filter.
    /// </summary>
    public Dictionary<string, object>[] GetEntityFields(ExpressionTree filter, string[] includedFieldNames, string[] sortedBy, int limit = 100, int skip = 0) {
      WorkItem[] items = GetEntities(filter, sortedBy, limit, skip);
      return items.Select(e => ProjectFields(e, includedFieldNames)).ToArray();
    }

    /// <summary>
    /// NOT IMPLEMENTED: string search expressions (capability disabled).
    /// </summary>
    public Dictionary<string, object>[] GetEntityFieldsBySearchExpression(string searchExpression, string[] includedFieldNames, string[] sortedBy, int limit = 100, int skip = 0) {
      throw new NotSupportedException("String-based search expressions are not supported by this repository configuration.");
    }

    /// <summary>
    /// Returns a dictionary projection (subset) by keys.
    /// </summary>
    public Dictionary<string, object>[] GetEntityFieldsByKey(WorkItemIdentity[] keysToLoad, string[] includedFieldNames) {
      WorkItem[] items = GetEntitiesByKey(keysToLoad);
      return items.Select(e => ProjectFields(e, includedFieldNames)).ToArray();
    }

    /// <summary>
    /// Counts all work items in project.
    /// </summary>
    public int CountAll() {
      ExpressionTree any = ExpressionTree.Empty();
      return Count(any);
    }

    /// <summary>
    /// Counts by filter using WIQL.
    /// </summary>
    public int Count(ExpressionTree filter) {
      string wiql = BuildWiql(filter, new string[] { }, 200000, 0);
      string url = _Client.BuildProjectApiUrl(_Project, "wit/wiql?api-version=7.1-preview.2");
      JObject wiqlBody = new JObject {
        ["query"] = wiql
      };
      JObject wiqlResult = _Client.PostJson(url, wiqlBody);
      int count = wiqlResult["workItems"].Count();
      return count;
    }

    /// <summary>
    /// NOT IMPLEMENTED: string search expressions (capability disabled).
    /// </summary>
    public int CountBySearchExpression(string searchExpression) {
      throw new NotSupportedException("String-based search expressions are not supported by this repository configuration.");
    }

    /// <summary>
    /// Checks for existence by single key.
    /// </summary>
    public bool ContainsKey(WorkItemIdentity key) {
      try {
        if (key == null || string.IsNullOrWhiteSpace(key.OriginRelatedId)) {
          return false;
        }

        string url = _Client.BuildOrgApiUrl("wit/workitems/" + key.OriginRelatedId + "?api-version=7.1-preview.3&fields=System.Id");
        JObject obj = _Client.GetJson(url);
        int id = (int)obj["id"];
        return id > 0;
      }
      catch (Exception ex) {
        // 404 will throw – treat as not found
        DevLogger.LogTrace(0, 99999, "ContainsKey error treated as not found: " + ex.Message);
        return false;
      }
    }

    /// <summary>
    /// Adds or Updates selected fields. Returns fields that differ from input or null if not found (per contract).
    /// For ADO we simply return the persisted current state as dictionary.
    /// </summary>
    public Dictionary<string, object> AddOrUpdateEntityFields(Dictionary<string, object> fields) {
      WorkItem entity = DictionaryToEntity(fields);
      WorkItem updated = AddOrUpdateEntity(entity);
      if (updated == null) {
        return null;
      }

      Dictionary<string, object> dict = EntityToDictionary(updated);
      return dict;
    }

    /// <summary>
    /// Add or Update a WorkItem. If OriginRelatedId is present, we update; otherwise create.
    /// </summary>
    public WorkItem AddOrUpdateEntity(WorkItem entity) {
      try {
        if (entity == null) {
          throw new ArgumentNullException(nameof(entity));
        }

        if (string.IsNullOrWhiteSpace(entity.OriginRelatedId)) {
          string newId = CreateWorkItem(entity);
          entity.OriginRelatedId = newId;
          WorkItem fresh = GetEntitiesByKey(new[] { WorkItemIdentity.FromModel(entity) }).FirstOrDefault();
          return fresh;
        }
        else {
          UpdateWorkItem(entity);
          WorkItem fresh = GetEntitiesByKey(new[] { WorkItemIdentity.FromModel(entity) }).FirstOrDefault();
          return fresh;
        }
      }
      catch (Exception ex) {
        DevLogger.LogCritical(ex);
        throw;
      }
    }

    /// <summary>
    /// Tries to update certain fields for an existing entity; returns final field snapshot or null if not found.
    /// </summary>
    public Dictionary<string, object> TryUpdateEntityFields(Dictionary<string, object> fields) {
      try {
        WorkItem entity = DictionaryToEntity(fields);
        WorkItem updated = TryUpdateEntity(entity);
        if (updated == null) {
          return null;
        }
        return EntityToDictionary(updated);
      }
      catch (Exception ex) {
        DevLogger.LogCritical(ex);
        throw;
      }
    }

    /// <summary>
    /// Updates all updatable fields; returns updated entity or null if not found.
    /// </summary>
    public WorkItem TryUpdateEntity(WorkItem entity) {
      try {
        if (entity == null || string.IsNullOrWhiteSpace(entity.OriginRelatedId)) {
          throw new ArgumentException("TryUpdateEntity requires an existing OriginRelatedId.");
        }

        if (!ContainsKey(WorkItemIdentity.FromModel(entity))) {
          return null;
        }

        UpdateWorkItem(entity);
        WorkItem fresh = GetEntitiesByKey(new[] { WorkItemIdentity.FromModel(entity) }).FirstOrDefault();
        return fresh;
      }
      catch (Exception ex) {
        DevLogger.LogCritical(ex);
        throw;
      }
    }

    /// <summary>
    /// Tries to add entity, returns key or null if duplicate exists.
    /// </summary>
    public WorkItemIdentity TryAddEntity(WorkItem entity) {
      try {
        if (entity == null) {
          throw new ArgumentNullException(nameof(entity));
        }

        if (!string.IsNullOrWhiteSpace(entity.OriginRelatedId)) {
          // Already has ID -> consider duplicate
          return null;
        }

        string id = CreateWorkItem(entity);
        WorkItemIdentity key = new WorkItemIdentity {
          Origin = _Origin,
          OriginRelatedId = id
        };
        return key;
      }
      catch (Exception ex) {
        DevLogger.LogCritical(ex);
        return null;
      }
    }

    /// <summary>
    /// Massupdate by keys: not supported in this minimal version.
    /// </summary>
    public WorkItemIdentity[] MassupdateByKeys(WorkItemIdentity[] keysToUpdate, Dictionary<string, object> fields) {
      throw new NotSupportedException("Massupdate is not enabled in this repository.");
    }

    /// <summary>
    /// Massupdate by filter: not supported in this minimal version.
    /// </summary>
    public WorkItemIdentity[] Massupdate(ExpressionTree filter, Dictionary<string, object> fields) {
      throw new NotSupportedException("Massupdate is not enabled in this repository.");
    }

    /// <summary>
    /// Massupdate by search expression: not supported in this minimal version.
    /// </summary>
    public WorkItemIdentity[] MassupdateBySearchExpression(string searchExpression, Dictionary<string, object> fields) {
      throw new NotSupportedException("Massupdate is not enabled in this repository.");
    }

    /// <summary>
    /// Tries to delete entities by keys. Returns only deleted keys.
    /// </summary>
    public WorkItemIdentity[] TryDeleteEntities(WorkItemIdentity[] keysToDelete) {
      if (keysToDelete == null || keysToDelete.Length == 0) {
        return new WorkItemIdentity[0];
      }

      List<WorkItemIdentity> deleted = new List<WorkItemIdentity>();
      foreach (WorkItemIdentity key in keysToDelete) {
        if (key == null || string.IsNullOrWhiteSpace(key.OriginRelatedId)) {
          continue;
        }

        try {
          string url = _Client.BuildOrgApiUrl("wit/workitems/" + key.OriginRelatedId + "?api-version=7.1-preview.3");
          bool ok = _Client.Delete(url);
          if (ok) {
            deleted.Add(key);
          }
        }
        catch (Exception ex) {
          DevLogger.LogCritical(ex);
        }
      }

      return deleted.ToArray();
    }

    /// <summary>
    /// Updating the key is not supported in ADO in a simple way.
    /// </summary>
    public bool TryUpdateKey(WorkItemIdentity currentKey, WorkItemIdentity newKey) {
      throw new NotSupportedException("Key updates are not supported for Azure DevOps Work Items.");
    }

    // ------------------------------------------------------------
    // Mapping and helpers
    // ------------------------------------------------------------

    /// <summary>
    /// Determines whether the ADO field is already mapped to a typed WorkItem property.
    /// </summary>
    private static bool IsMappedAdoField(string fieldReferenceName) {
      if (string.IsNullOrWhiteSpace(fieldReferenceName)) {
        return false;
      }
      return _MappedAdoFields.Contains(fieldReferenceName);
    }

    /// <summary>
    /// Determines whether a field name is a safe Azure DevOps reference name.
    /// </summary>
    private static bool IsSafeAdoReferenceName(string fieldReferenceName) {
      if (string.IsNullOrWhiteSpace(fieldReferenceName)) {
        return false;
      }

      bool hasDot = false;
      for (int i = 0; i < fieldReferenceName.Length; i++) {
        char c = fieldReferenceName[i];
        if (c == '.') {
          hasDot = true;
          continue;
        }
        if (c == '_') {
          continue;
        }
        if (char.IsLetterOrDigit(c)) {
          continue;
        }
        return false;
      }
      return hasDot;
    }

    /// <summary>
    /// Determines whether an additional ADO field may be patched back to Azure DevOps.
    /// </summary>
    private bool IsWritableAdditionalAdoField(string fieldReferenceName) {
      if (!IsSafeAdoReferenceName(fieldReferenceName)) {
        return false;
      }
      if (IsMappedAdoField(fieldReferenceName)) {
        return false;
      }

      Dictionary<string, AdoFieldDefinition> definitions = this.GetAdoFieldDefinitions();
      AdoFieldDefinition definition;
      if (definitions.TryGetValue(fieldReferenceName, out definition)) {
        return !definition.IsReadOnly;
      }

      if (fieldReferenceName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)) {
        return _WritableSystemAdoFields.Contains(fieldReferenceName);
      }

      return true;
    }

    /// <summary>
    /// Returns cached Azure DevOps field definitions for writeability checks.
    /// </summary>
    private Dictionary<string, AdoFieldDefinition> GetAdoFieldDefinitions() {
      if (_AdoFieldDefinitions != null) {
        return _AdoFieldDefinitions;
      }

      lock (_AdoFieldDefinitionsSync) {
        if (_AdoFieldDefinitions == null) {
          _AdoFieldDefinitions = LoadAdoFieldDefinitions();
        }
        return _AdoFieldDefinitions;
      }
    }

    /// <summary>
    /// Loads Azure DevOps field definitions from the project collection.
    /// </summary>
    private Dictionary<string, AdoFieldDefinition> LoadAdoFieldDefinitions() {
      Dictionary<string, AdoFieldDefinition> definitions = new Dictionary<string, AdoFieldDefinition>(StringComparer.OrdinalIgnoreCase);
      try {
        string url = _Client.BuildOrgApiUrl("wit/fields?api-version=7.1-preview.3");
        JObject response = _Client.GetJson(url);
        JArray values = response["value"] as JArray;
        if (values == null) {
          return definitions;
        }

        foreach (JObject field in values.OfType<JObject>()) {
          string referenceName = (string)field["referenceName"];
          if (string.IsNullOrWhiteSpace(referenceName)) {
            continue;
          }

          bool isReadOnly = false;
          JToken readOnlyToken = field["readOnly"];
          if (readOnlyToken != null && readOnlyToken.Type != JTokenType.Null) {
            isReadOnly = readOnlyToken.ToObject<bool>();
          }

          definitions[referenceName] = new AdoFieldDefinition(referenceName, (string)field["type"], isReadOnly);
        }
      }
      catch (Exception ex) {
        DevLogger.LogTrace(0, 99999, "Could not load Azure DevOps field definitions. Falling back to reference-name based write checks: " + ex.Message);
      }
      return definitions;
    }

    /// <summary>
    /// Creates a new Work Item and returns the new numeric id as string.
    /// </summary>
    private string CreateWorkItem(WorkItem entity) {
      string workItemType = _DefaultWorkItemType;
      if (!string.IsNullOrWhiteSpace(entity.Class)) {
        // Allow Class like "AzureDevOps.Bug" to select type
        if (entity.Class.StartsWith(_AzureDevOpsClassPrefix, StringComparison.OrdinalIgnoreCase)) {
          workItemType = entity.Class.Substring(_AzureDevOpsClassPrefix.Length);
        }
      }

      string url = _Client.BuildProjectApiUrl(_Project, "wit/workitems/$" + Uri.EscapeDataString(workItemType) + "?api-version=7.1-preview.3");
      JArray patch = BuildPatchForEntity(entity, false);
      JObject created = _Client.PatchJson(url, patch);
      int id = (int)created["id"];
      return id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Updates an existing Work Item.
    /// </summary>
    private void UpdateWorkItem(WorkItem entity) {
      string url = _Client.BuildOrgApiUrl("wit/workitems/" + entity.OriginRelatedId + "?api-version=7.1-preview.3");
      JArray patch = BuildPatchForEntity(entity, true);
      _Client.PatchJson(url, patch);
    }

    /// <summary>
    /// Builds JSON Patch operations for create/update from our model.
    /// </summary>
    private JArray BuildPatchForEntity(WorkItem entity, bool isUpdate) {
      List<JObject> ops = new List<JObject>();

      AddPatch(ops, "add", "/fields/" + EscapeJsonPointerSegment(_AdoFieldTitle), entity.Title);
      if (!string.IsNullOrWhiteSpace(entity.BodyContent)) {
        AddPatch(ops, "add", "/fields/" + EscapeJsonPointerSegment(_AdoFieldDescription), entity.BodyContent);
      }

      if (entity.Tags != null && entity.Tags.Length > 0) {
        string tags = string.Join(";", entity.Tags);
        AddPatch(ops, "add", "/fields/" + EscapeJsonPointerSegment(_AdoFieldTags), tags);
      }

      if (!string.IsNullOrWhiteSpace(entity.AssignedTo)) {
        AddPatch(ops, "add", "/fields/" + EscapeJsonPointerSegment(_AdoFieldAssignedTo), entity.AssignedTo);
      }

      if (!string.IsNullOrWhiteSpace(entity.ProgressState)) {
        string state = ConvertModelStateToAdo(entity.ProgressState);
        AddPatch(ops, "add", "/fields/" + EscapeJsonPointerSegment(_AdoFieldState), state);
      }

      if (!string.IsNullOrWhiteSpace(entity.ConcernArea) && !string.Equals(entity.ConcernArea, "/", StringComparison.Ordinal)) {
        string adoAreaPath = ConvertUniversalPathToAdoPath(entity.ConcernArea, _Project);
        AddPatch(ops, "add", "/fields/" + EscapeJsonPointerSegment(_AdoFieldAreaPath), adoAreaPath);
      }

      if (entity.ManualReorderedPriority > 0) {
        double d = Convert.ToDouble(entity.ManualReorderedPriority, CultureInfo.InvariantCulture);
        AddPatch(ops, "add", "/fields/" + EscapeJsonPointerSegment(_AdoFieldStackRank), d);
      }

      if (entity.EstimatedEffort > 0) {
        AddPatch(ops, "add", "/fields/" + EscapeJsonPointerSegment(_AdoFieldRemainingWork), entity.EstimatedEffort);
      }

      if (entity.ProgressEffort > 0) {
        AddPatch(ops, "add", "/fields/" + EscapeJsonPointerSegment(_AdoFieldCompletedWork), entity.ProgressEffort);
      }

      this.AddAdditionalFieldPatchOperations(ops, entity);

      // Relations (Parent/Child/Related/Hyperlink/Attachment)
      if (entity.LinkedItems != null && entity.LinkedItems.Length > 0) {
        // We append all new links (for simplicity: not diffing existing)
        foreach (LinkedItem link in entity.LinkedItems) {
          JObject rel = ConvertLinkedItemToRelation(link);
          if (rel != null) {
            JObject addRel = new JObject {
              ["op"] = "add",
              ["path"] = "/relations/-",
              ["value"] = rel
            };
            ops.Add(addRel);
          }
        }
      }

      return new JArray(ops.ToArray());
    }

    /// <summary>
    /// Adds one patch op.
    /// </summary>
    private static void AddPatch(List<JObject> ops, string op, string path, object value) {
      JObject o = new JObject {
        ["op"] = op,
        ["path"] = path,
        ["value"] = value == null ? JValue.CreateNull() : JToken.FromObject(value)
      };
      ops.Add(o);
    }

    /// <summary>
    /// Adds patch operations for dynamic ADO fields stored in AdditionalFields.
    /// </summary>
    private void AddAdditionalFieldPatchOperations(List<JObject> ops, WorkItem entity) {
      if (entity == null || entity.AdditionalFields == null) {
        return;
      }

      foreach (KeyValuePair<string, object> field in entity.AdditionalFields) {
        if (!this.IsWritableAdditionalAdoField(field.Key)) {
          continue;
        }

        string path = "/fields/" + EscapeJsonPointerSegment(field.Key);
        AddPatch(ops, "add", path, field.Value);
      }
    }

    /// <summary>
    /// Escapes one JSON pointer path segment.
    /// </summary>
    private static string EscapeJsonPointerSegment(string segment) {
      if (segment == null) {
        return "";
      }
      return segment.Replace("~", "~0").Replace("/", "~1");
    }

    /// <summary>
    /// Converts our LinkedItem to an ADO relation object.
    /// </summary>
    private static JObject ConvertLinkedItemToRelation(LinkedItem li) {
      if (li == null || string.IsNullOrWhiteSpace(li.LinkType)) {
        return null;
      }

      string linkType = li.LinkType;
      if (string.Equals(linkType, "Parent", StringComparison.OrdinalIgnoreCase)) {
        return new JObject {
          ["rel"] = "System.LinkTypes.Hierarchy-Reverse",
          ["url"] = BuildWorkItemUrl(li.Origin, li.Target),
          ["attributes"] = new JObject { ["comment"] = "Parent link" }
        };
      }
      if (string.Equals(linkType, "Child", StringComparison.OrdinalIgnoreCase)) {
        return new JObject {
          ["rel"] = "System.LinkTypes.Hierarchy-Forward",
          ["url"] = BuildWorkItemUrl(li.Origin, li.Target),
          ["attributes"] = new JObject { ["comment"] = "Child link" }
        };
      }
      if (linkType.StartsWith("Related", StringComparison.OrdinalIgnoreCase)) {
        return new JObject {
          ["rel"] = "System.LinkTypes.Related",
          ["url"] = BuildWorkItemUrl(li.Origin, li.Target),
          ["attributes"] = new JObject { ["comment"] = linkType }
        };
      }
      if (string.Equals(linkType, "Hyperlink", StringComparison.OrdinalIgnoreCase)) {
        return new JObject {
          ["rel"] = "Hyperlink",
          ["url"] = li.Target,
          ["attributes"] = new JObject { ["comment"] = li.Origin }
        };
      }
      if (string.Equals(linkType, "Attachment", StringComparison.OrdinalIgnoreCase)) {
        // For simplicity: accept "Target" as a already-uploaded attachment URL (RFC-2397 inline not directly supported here)
        return new JObject {
          ["rel"] = "AttachedFile",
          ["url"] = li.Target,
          ["attributes"] = new JObject { ["comment"] = li.Origin }
        };
      }

      DevLogger.LogTrace(0, 99999, "Skipping unsupported LinkedItem type: " + linkType);
      return null;
    }

    /// <summary>
    /// Builds a work item URL for relation from base origin + numeric id.
    /// </summary>
    private static string BuildWorkItemUrl(string origin, string idStr) {
      string baseUrl = origin.TrimEnd('/');
      string rel = baseUrl + "/_apis/wit/workItems/" + idStr;
      return rel;
    }

    /// <summary>
    /// Maps model state to ADO process state via _StateMap.
    /// </summary>
    private static string ConvertModelStateToAdo(string state) {
      if (string.IsNullOrWhiteSpace(state)) {
        return "New";
      }

      string mapped;
      if (_StateMap.TryGetValue(state, out mapped)) {
        return mapped;
      }
      return state;
    }

    /// <summary>
    /// Converts an ADO work item JSON to our model.
    /// </summary>
    private WorkItem FromAdoWorkItem(JObject wi) {
      JObject fields = wi["fields"] as JObject;
      if (fields == null) {
        fields = new JObject();
      }

      WorkItem e = new WorkItem();
      e.Origin = _Origin;
      e.StorageArea = "/" + _Project + "/";
      e.OriginRelatedId = ((int)wi["id"]).ToString(CultureInfo.InvariantCulture);

      string workItemType = GetField<string>(fields, _AdoFieldWorkItemType, "");
      if (!string.IsNullOrWhiteSpace(workItemType)) {
        e.Class = _AzureDevOpsClassPrefix + workItemType;
      }

      string teamProject = GetField<string>(fields, _AdoFieldTeamProject, _Project);
      if (!string.IsNullOrWhiteSpace(teamProject)) {
        e.StorageArea = "/" + teamProject + "/";
      }

      e.ConcernArea = ConvertAdoPathToUniversalPath(GetField<string>(fields, _AdoFieldAreaPath, ""), teamProject);
      e.Title = GetField<string>(fields, _AdoFieldTitle, "Untitled");
      e.ProgressState = GetField<string>(fields, _AdoFieldState, "New");
      e.BodyContent = GetField<string>(fields, _AdoFieldDescription, "Untitled");
      e.BodyContentMimetype = "text/html";

      string tags = GetField<string>(fields, _AdoFieldTags, "");
      if (string.IsNullOrWhiteSpace(tags)) {
        e.Tags = new string[0];
      }
      else {
        e.Tags = tags.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
      }

      string assignedTo = ExtractIdentityDisplay(fields, _AdoFieldAssignedTo);
      e.AssignedTo = assignedTo;

      e.CreatedTimestamp = GetNullableDateTimeField(fields, _AdoFieldCreatedDate);
      e.CreatedBy = ExtractIdentityDisplay(fields, _AdoFieldCreatedBy);

      e.ModifiedTimestamp = GetNullableDateTimeField(fields, _AdoFieldChangedDate);
      e.ModifiedBy = ExtractIdentityDisplay(fields, _AdoFieldChangedBy);

      double stack = GetField<double>(fields, _AdoFieldStackRank, 0);
      if (stack > 0) {
        long l = Convert.ToInt64(Math.Round(stack), CultureInfo.InvariantCulture);
        e.ManualReorderedPriority = l;
      }

      int rem = (int)Math.Round(GetField<double>(fields, _AdoFieldRemainingWork, 0.0), 0);
      int comp = (int)Math.Round(GetField<double>(fields, _AdoFieldCompletedWork, 0.0), 0);
      e.EstimatedEffort = rem;
      e.ProgressEffort = comp;
      e.EffortUnit = "hours";

      StoreAdditionalAdoFields(e, fields);

      // Relations -> LinkedItems
      if (wi["relations"] is JArray rels && rels.Count > 0) {
        List<LinkedItem> links = new List<LinkedItem>();
        foreach (JObject r in rels) {
          string rel = (string)r["rel"];
          string url = (string)r["url"];
          LinkedItem li = ConvertRelationToLinkedItem(rel, url);
          if (li != null) {
            links.Add(li);
          }
        }
        e.LinkedItems = links.ToArray();
      }
      else {
        e.LinkedItems = new LinkedItem[0];
      }

      return e;
    }

    /// <summary>
    /// Stores every non-universal ADO field as additional field under its ADO reference name.
    /// </summary>
    private static void StoreAdditionalAdoFields(WorkItem entity, JObject fields) {
      if (entity == null || fields == null) {
        return;
      }

      foreach (JProperty field in fields.Properties()) {
        if (IsMappedAdoField(field.Name)) {
          continue;
        }
        entity.SetField(field.Name, field.Value);
      }
    }

    /// <summary>
    /// Converts an Azure DevOps path to the slash-based universal path shape.
    /// </summary>
    private static string ConvertAdoPathToUniversalPath(string adoPath, string projectName) {
      if (string.IsNullOrWhiteSpace(adoPath)) {
        return "/";
      }

      string normalized = adoPath.Trim().Replace('\\', '/');
      string project = string.IsNullOrWhiteSpace(projectName) ? "" : projectName.Trim().Replace('\\', '/');
      if (!string.IsNullOrWhiteSpace(project)) {
        if (string.Equals(normalized, project, StringComparison.OrdinalIgnoreCase)) {
          normalized = "";
        }
        else if (normalized.StartsWith(project + "/", StringComparison.OrdinalIgnoreCase)) {
          normalized = normalized.Substring(project.Length + 1);
        }
      }

      normalized = normalized.Trim('/');
      if (string.IsNullOrWhiteSpace(normalized)) {
        return "/";
      }
      return "/" + normalized + "/";
    }

    /// <summary>
    /// Converts a slash-based universal path to an Azure DevOps area path.
    /// </summary>
    private static string ConvertUniversalPathToAdoPath(string universalPath, string projectName) {
      string project = string.IsNullOrWhiteSpace(projectName) ? "" : projectName.Trim().Trim('/').Replace('/', '\\');
      string path = string.IsNullOrWhiteSpace(universalPath) ? "" : universalPath.Trim().Trim('/').Replace('/', '\\');

      if (string.IsNullOrWhiteSpace(project)) {
        return path;
      }
      if (string.IsNullOrWhiteSpace(path)) {
        return project;
      }
      if (string.Equals(path, project, StringComparison.OrdinalIgnoreCase)) {
        return project;
      }
      if (path.StartsWith(project + "\\", StringComparison.OrdinalIgnoreCase)) {
        return path;
      }
      return project + "\\" + path;
    }

    /// <summary>
    /// Extracts a string display name from an ADO identity field.
    /// </summary>
    private static string ExtractIdentityDisplay(JObject fields, string name) {
      if (fields == null || fields[name] == null) {
        return "";
      }

      JToken token = fields[name];
      if (token.Type == JTokenType.Object) {
        string displayName = (string)token["displayName"];
        if (string.IsNullOrWhiteSpace(displayName)) {
          return token.ToString(Formatting.None);
        }
        return displayName;
      }

      return token.ToString();
    }

    /// <summary>
    /// Converts ADO relation (rel + url) to LinkedItem.
    /// </summary>
    private static LinkedItem ConvertRelationToLinkedItem(string rel, string url) {
      if (string.IsNullOrWhiteSpace(rel) || string.IsNullOrWhiteSpace(url)) {
        return null;
      }

      if (rel.Equals("System.LinkTypes.Hierarchy-Forward", StringComparison.OrdinalIgnoreCase)) {
        return new LinkedItem {
          LinkType = "Child",
          Origin = url.Split(new[] { "/_apis/" }, StringSplitOptions.None)[0],
          Target = url.Substring(url.LastIndexOf("/", StringComparison.Ordinal) + 1)
        };
      }
      if (rel.Equals("System.LinkTypes.Hierarchy-Reverse", StringComparison.OrdinalIgnoreCase)) {
        return new LinkedItem {
          LinkType = "Parent",
          Origin = url.Split(new[] { "/_apis/" }, StringSplitOptions.None)[0],
          Target = url.Substring(url.LastIndexOf("/", StringComparison.Ordinal) + 1)
        };
      }
      if (rel.Equals("System.LinkTypes.Related", StringComparison.OrdinalIgnoreCase)) {
        return new LinkedItem {
          LinkType = "Related",
          Origin = url.Split(new[] { "/_apis/" }, StringSplitOptions.None)[0],
          Target = url.Substring(url.LastIndexOf("/", StringComparison.Ordinal) + 1)
        };
      }
      if (rel.Equals("Hyperlink", StringComparison.OrdinalIgnoreCase)) {
        return new LinkedItem {
          LinkType = "Hyperlink",
          Origin = "Hyperlink",
          Target = url
        };
      }
      if (rel.Equals("AttachedFile", StringComparison.OrdinalIgnoreCase)) {
        return new LinkedItem {
          LinkType = "Attachment",
          Origin = "Attachment",
          Target = url
        };
      }

      return null;
    }

    /// <summary>
    /// Reads a typed field with default.
    /// </summary>
    private static T GetField<T>(JObject fields, string name, T defaultValue) {
      if (fields == null || fields[name] == null) {
        return defaultValue;
      }

      try {
        T v = fields[name].ToObject<T>();
        return v;
      }
      catch {
        return defaultValue;
      }
    }

    /// <summary>
    /// Reads an Azure DevOps date field as nullable DateTime.
    /// </summary>
    private static DateTime? GetNullableDateTimeField(JObject fields, string name) {
      if (fields == null || fields[name] == null || fields[name].Type == JTokenType.Null) {
        return null;
      }

      try {
        JToken token = fields[name];
        if (token.Type == JTokenType.Date) {
          return token.ToObject<DateTime>();
        }

        string text = token.ToString();
        DateTime parsedDateTime;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsedDateTime)) {
          return parsedDateTime;
        }

        DateTimeOffset parsedOffset;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsedOffset)) {
          return parsedOffset.UtcDateTime;
        }
      }
      catch {
        return null;
      }

      return null;
    }

    /// <summary>
    /// Minimal field projection helper.
    /// </summary>
    private static Dictionary<string, object> ProjectFields(WorkItem e, string[] includedFieldNames) {
      Dictionary<string, object> dict = EntityToDictionary(e);
      if (includedFieldNames == null || includedFieldNames.Length == 0) {
        return dict;
      }

      HashSet<string> set = new HashSet<string>(includedFieldNames, StringComparer.OrdinalIgnoreCase);
      Dictionary<string, object> projected = new Dictionary<string, object>(StringComparer.Ordinal);
      foreach (KeyValuePair<string, object> kv in dict) {
        if (set.Contains(kv.Key)) {
          projected[kv.Key] = kv.Value;
        }
      }
      return projected;
    }

    /// <summary>
    /// Converts entity to a simple dictionary (flat).
    /// </summary>
    private static Dictionary<string, object> EntityToDictionary(WorkItem e) {
      Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.Ordinal);
      d[nameof(WorkItem.Origin)] = e.Origin;
      d[nameof(WorkItem.StorageArea)] = e.StorageArea;
      d[nameof(WorkItem.OriginRelatedId)] = e.OriginRelatedId;
      d[nameof(WorkItem.Class)] = e.Class;
      d[nameof(WorkItem.ConcernArea)] = e.ConcernArea;
      d[nameof(WorkItem.Title)] = e.Title;
      d[nameof(WorkItem.BodyContent)] = e.BodyContent;
      d[nameof(WorkItem.BodyContentMimetype)] = e.BodyContentMimetype;
      d[nameof(WorkItem.Tags)] = e.Tags;
      d[nameof(WorkItem.AssignedTo)] = e.AssignedTo;
      d[nameof(WorkItem.ProgressState)] = e.ProgressState;
      d[nameof(WorkItem.CreatedTimestamp)] = e.CreatedTimestamp;
      d[nameof(WorkItem.CreatedBy)] = e.CreatedBy;
      d[nameof(WorkItem.ModifiedTimestamp)] = e.ModifiedTimestamp;
      d[nameof(WorkItem.ModifiedBy)] = e.ModifiedBy;
      d[nameof(WorkItem.ManualReorderedPriority)] = e.ManualReorderedPriority;
      d[nameof(WorkItem.EstimatedEffort)] = e.EstimatedEffort;
      d[nameof(WorkItem.ProgressEffort)] = e.ProgressEffort;
      d[nameof(WorkItem.EffortUnit)] = e.EffortUnit;
      if (e.AdditionalFields != null) {
        foreach (KeyValuePair<string, object> field in e.AdditionalFields) {
          d[field.Key] = field.Value;
        }
      }
      return d;
    }

    /// <summary>
    /// Builds an entity from a flat dictionary (subset).
    /// </summary>
    private static WorkItem DictionaryToEntity(Dictionary<string, object> dict) {
      if (dict == null) {
        throw new ArgumentNullException(nameof(dict));
      }

      WorkItem e = new WorkItem();
      e.Origin = Read<string>(dict, nameof(WorkItem.Origin), "");
      e.StorageArea = Read<string>(dict, nameof(WorkItem.StorageArea), "/");
      e.OriginRelatedId = Read<string>(dict, nameof(WorkItem.OriginRelatedId), "");
      e.Class = ReadAny<string>(dict, "Generic.WorkItem", nameof(WorkItem.Class));
      string adoWorkItemType = Read<string>(dict, _AdoFieldWorkItemType, "");
      if (!string.IsNullOrWhiteSpace(adoWorkItemType)) {
        e.Class = _AzureDevOpsClassPrefix + adoWorkItemType;
      }
      e.ConcernArea = ReadAny<string>(dict, "/", nameof(WorkItem.ConcernArea));
      string adoAreaPath = Read<string>(dict, _AdoFieldAreaPath, "");
      if (!string.IsNullOrWhiteSpace(adoAreaPath)) {
        e.ConcernArea = ConvertAdoPathToUniversalPath(adoAreaPath, Read<string>(dict, _AdoFieldTeamProject, ""));
      }
      e.Title = ReadAny<string>(dict, "Untitled", nameof(WorkItem.Title), _AdoFieldTitle);
      e.BodyContent = ReadAny<string>(dict, "", nameof(WorkItem.BodyContent), _AdoFieldDescription);
      e.BodyContentMimetype = Read<string>(dict, nameof(WorkItem.BodyContentMimetype), "text/plain");
      e.AssignedTo = ReadAny<string>(dict, "", nameof(WorkItem.AssignedTo), _AdoFieldAssignedTo);
      e.ProgressState = ReadAny<string>(dict, "New", nameof(WorkItem.ProgressState), _AdoFieldState);
      e.CreatedTimestamp = ReadAnyNullableDateTime(dict, nameof(WorkItem.CreatedTimestamp), _AdoFieldCreatedDate);
      e.CreatedBy = ReadAny<string>(dict, "Unknown", nameof(WorkItem.CreatedBy), _AdoFieldCreatedBy);
      e.ModifiedTimestamp = ReadAnyNullableDateTime(dict, nameof(WorkItem.ModifiedTimestamp), _AdoFieldChangedDate);
      e.ModifiedBy = ReadAny<string>(dict, "Unknown", nameof(WorkItem.ModifiedBy), _AdoFieldChangedBy);
      e.ManualReorderedPriority = ReadAny<long>(dict, 0, nameof(WorkItem.ManualReorderedPriority), _AdoFieldStackRank);
      e.EstimatedEffort = ReadAny<int>(dict, 0, nameof(WorkItem.EstimatedEffort), _AdoFieldRemainingWork);
      e.ProgressEffort = ReadAny<int>(dict, 0, nameof(WorkItem.ProgressEffort), _AdoFieldCompletedWork);
      e.EffortUnit = Read<string>(dict, nameof(WorkItem.EffortUnit), "");
      string[] tags = ReadTags(dict, nameof(WorkItem.Tags), _AdoFieldTags);
      if (tags != null) {
        e.Tags = tags;
      }

      foreach (KeyValuePair<string, object> field in dict) {
        if (IsMappedAdoField(field.Key)) {
          continue;
        }
        e.SetField(field.Key, field.Value);
      }
      return e;
    }

    /// <summary>
    /// Reads a typed value from dictionary with default.
    /// </summary>
    private static T Read<T>(Dictionary<string, object> d, string key, T def) {
      object v;
      if (!d.TryGetValue(key, out v) || v == null) {
        return def;
      }

      try {
        if (typeof(T) == typeof(string)) {
          string s = Convert.ToString(v, CultureInfo.InvariantCulture);
          return (T)(object)s;
        }
        if (typeof(T) == typeof(int)) {
          int i = Convert.ToInt32(v, CultureInfo.InvariantCulture);
          return (T)(object)i;
        }
        if (typeof(T) == typeof(long)) {
          long l = Convert.ToInt64(v, CultureInfo.InvariantCulture);
          return (T)(object)l;
        }
        if (typeof(T) == typeof(double)) {
          double dval = Convert.ToDouble(v, CultureInfo.InvariantCulture);
          return (T)(object)dval;
        }
        return (T)v;
      }
      catch {
        return def;
      }
    }

    /// <summary>
    /// Reads the first present key from a dictionary.
    /// </summary>
    private static T ReadAny<T>(Dictionary<string, object> d, T def, params string[] keys) {
      if (keys == null) {
        return def;
      }

      foreach (string key in keys) {
        object value;
        if (d.TryGetValue(key, out value) && value != null) {
          return Read<T>(d, key, def);
        }
      }
      return def;
    }

    /// <summary>
    /// Reads the first present date/time key from a dictionary.
    /// </summary>
    private static DateTime? ReadAnyNullableDateTime(Dictionary<string, object> d, params string[] keys) {
      if (keys == null) {
        return null;
      }

      foreach (string key in keys) {
        object value;
        if (d.TryGetValue(key, out value) && value != null) {
          return ConvertToNullableDateTime(value);
        }
      }
      return null;
    }

    /// <summary>
    /// Converts a dictionary value to nullable DateTime.
    /// </summary>
    private static DateTime? ConvertToNullableDateTime(object value) {
      if (value == null) {
        return null;
      }

      if (value is DateTime) {
        return (DateTime)value;
      }
      if (value is DateTimeOffset) {
        DateTimeOffset offset = (DateTimeOffset)value;
        return offset.UtcDateTime;
      }

      JToken token = value as JToken;
      if (token != null) {
        if (token.Type == JTokenType.Null) {
          return null;
        }
        if (token.Type == JTokenType.Date) {
          return token.ToObject<DateTime>();
        }
        value = token.ToString();
      }

      string text = Convert.ToString(value, CultureInfo.InvariantCulture);
      if (string.IsNullOrWhiteSpace(text)) {
        return null;
      }

      DateTime parsedDateTime;
      if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsedDateTime)) {
        return parsedDateTime;
      }

      DateTimeOffset parsedOffset;
      if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsedOffset)) {
        return parsedOffset.UtcDateTime;
      }

      return null;
    }

    /// <summary>
    /// Reads tags from WorkItem shape or Azure DevOps semicolon-separated shape.
    /// </summary>
    private static string[] ReadTags(Dictionary<string, object> d, params string[] keys) {
      if (keys == null) {
        return null;
      }

      foreach (string key in keys) {
        object value;
        if (!d.TryGetValue(key, out value) || value == null) {
          continue;
        }

        string text = value as string;
        if (text != null) {
          if (string.IsNullOrWhiteSpace(text)) {
            return new string[0];
          }
          return text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        }

        string[] stringArray = value as string[];
        if (stringArray != null) {
          return stringArray;
        }

        IEnumerable<object> objectSequence = value as IEnumerable<object>;
        if (objectSequence != null) {
          return objectSequence.Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)).ToArray();
        }
      }

      return null;
    }

    /// <summary>
    /// Converts a WorkItem to an EntityRef.
    /// </summary>
    private static EntityRef<WorkItemIdentity> ToEntityRef(WorkItem e) {
      EntityRef<WorkItemIdentity> r = new EntityRef<WorkItemIdentity> {
        Key = new WorkItemIdentity() {
           Origin = e.Origin, 
          OriginRelatedId = e.OriginRelatedId
        },
        Label = e.Title
      };
      return r;
    }

    private sealed class AdoFieldDefinition {

      private readonly string _ReferenceName;
      private readonly string _Type;
      private readonly bool _IsReadOnly;

      public AdoFieldDefinition(string referenceName, string type, bool isReadOnly) {
        _ReferenceName = referenceName;
        _Type = type;
        _IsReadOnly = isReadOnly;
      }

      public string ReferenceName {
        get {
          return _ReferenceName;
        }
      }

      public string Type {
        get {
          return _Type;
        }
      }

      public bool IsReadOnly {
        get {
          return _IsReadOnly;
        }
      }

    }

  }

}

#endif
