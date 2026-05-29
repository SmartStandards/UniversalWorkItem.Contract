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

    private static readonly Dictionary<string, string> _StateMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      { "New", "New" },
      { "Working", "Active" },
      { "Suspended", "Removed" },
      { "Done", "Closed" },
      { "Discontinued", "Removed" }
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
      if (string.Equals(field, nameof(WorkItem.CreatedTimestamp), StringComparison.OrdinalIgnoreCase)) {
        return "System.CreatedDate";
      }
      if (string.Equals(field, nameof(WorkItem.ModifiedTimestamp), StringComparison.OrdinalIgnoreCase)) {
        return "System.ChangedDate";
      }
      if (string.Equals(field, nameof(WorkItem.Title), StringComparison.OrdinalIgnoreCase)) {
        return "System.Title";
      }
      if (string.Equals(field, nameof(WorkItem.ProgressState), StringComparison.OrdinalIgnoreCase)) {
        return "System.State";
      }
      return "System.ChangedDate";
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
        return "System.Title";
      }
      if (string.Equals(field, nameof(WorkItem.ProgressState), StringComparison.OrdinalIgnoreCase)) {
        return "System.State";
      }
      if (string.Equals(field, nameof(WorkItem.AssignedTo), StringComparison.OrdinalIgnoreCase)) {
        return "System.AssignedTo";
      }
      if (string.Equals(field, nameof(WorkItem.CreatedTimestamp), StringComparison.OrdinalIgnoreCase)) {
        return "System.CreatedDate";
      }
      if (string.Equals(field, nameof(WorkItem.ModifiedTimestamp), StringComparison.OrdinalIgnoreCase)) {
        return "System.ChangedDate";
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
        string iso = dt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return "'" + iso + "'";
      }

      if (value is long || value is int || value is double || value is decimal) {
        return Convert.ToString(value, CultureInfo.InvariantCulture);
      }

      return "'" + value.ToString().Replace("'", "''") + "'";
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

      string idsParam = string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray());
      string fieldsParam = string.Join(",", new string[]
      {
                "System.Id",
                "System.Title",
                "System.State",
                "System.TeamProject",
                "System.Description",
                "System.Tags",
                "System.AssignedTo",
                "System.CreatedDate",
                "System.ChangedDate",
                "System.CreatedBy",
                "System.ChangedBy",
                "Microsoft.VSTS.Common.StackRank",
                "Microsoft.VSTS.Scheduling.RemainingWork",
                "Microsoft.VSTS.Scheduling.CompletedWork"
      });

      string url = _Client.BuildOrgApiUrl("wit/workitemsbatch?api-version=7.1-preview.1");
      JObject body = new JObject {
        ["ids"] = new JArray(ids),
        ["$expand"] = "Relations",
        ["fields"] = new JArray(fieldsParam.Split(','))
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
    /// Creates a new Work Item and returns the new numeric id as string.
    /// </summary>
    private string CreateWorkItem(WorkItem entity) {
      string workItemType = _DefaultWorkItemType;
      if (!string.IsNullOrWhiteSpace(entity.Class)) {
        // Allow Class like "AzureDevOps.Bug" to select type
        if (entity.Class.StartsWith("AzureDevOps.", StringComparison.OrdinalIgnoreCase)) {
          workItemType = entity.Class.Substring("AzureDevOps.".Length);
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

      AddPatch(ops, "add", "/fields/System.Title", entity.Title);
      if (!string.IsNullOrWhiteSpace(entity.BodyContent)) {
        AddPatch(ops, "add", "/fields/System.Description", entity.BodyContent);
      }

      if (entity.Tags != null && entity.Tags.Length > 0) {
        string tags = string.Join(";", entity.Tags);
        AddPatch(ops, "add", "/fields/System.Tags", tags);
      }

      if (!string.IsNullOrWhiteSpace(entity.AssignedTo)) {
        AddPatch(ops, "add", "/fields/System.AssignedTo", entity.AssignedTo);
      }

      if (!string.IsNullOrWhiteSpace(entity.ProgressState)) {
        string state = ConvertModelStateToAdo(entity.ProgressState);
        AddPatch(ops, "add", "/fields/System.State", state);
      }

      if (entity.ManualReorderedPriority > 0) {
        double d = Convert.ToDouble(entity.ManualReorderedPriority, CultureInfo.InvariantCulture);
        AddPatch(ops, "add", "/fields/Microsoft.VSTS.Common.StackRank", d);
      }

      if (entity.EstimatedEffort > 0) {
        AddPatch(ops, "add", "/fields/Microsoft.VSTS.Scheduling.RemainingWork", entity.EstimatedEffort);
      }

      if (entity.ProgressEffort > 0) {
        AddPatch(ops, "add", "/fields/Microsoft.VSTS.Scheduling.CompletedWork", entity.ProgressEffort);
      }

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
        ["value"] = JToken.FromObject(value)
      };
      ops.Add(o);
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
      JObject fields = (JObject)wi["fields"];

      WorkItem e = new WorkItem();
      e.Origin = _Origin;
      e.StorageArea = "/" + _Project + "/";
      e.OriginRelatedId = ((int)wi["id"]).ToString(CultureInfo.InvariantCulture);

      e.Title = GetField<string>(fields, "System.Title", "Untitled");
      e.ProgressState = GetField<string>(fields, "System.State", "New");
      e.BodyContent = GetField<string>(fields, "System.Description", "Untitled");
      e.BodyContentMimetype = "text/html";

      string tags = GetField<string>(fields, "System.Tags", "");
      if (string.IsNullOrWhiteSpace(tags)) {
        e.Tags = new string[0];
      }
      else {
        e.Tags = tags.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
      }

      string assignedTo = ExtractIdentityDisplay(fields, "System.AssignedTo");
      e.AssignedTo = assignedTo;

      DateTime created = GetField<DateTime>(fields, "System.CreatedDate", DateTime.MinValue);
      e.CreatedTimestamp = created == DateTime.MinValue ? 0 : ToUnix(created);
      e.CreatedBy = ExtractIdentityDisplay(fields, "System.CreatedBy");

      DateTime changed = GetField<DateTime>(fields, "System.ChangedDate", DateTime.MinValue);
      e.ModifiedTimestamp = changed == DateTime.MinValue ? 0 : ToUnix(changed);
      e.ModifiedBy = ExtractIdentityDisplay(fields, "System.ChangedBy");

      double stack = GetField<double>(fields, "Microsoft.VSTS.Common.StackRank", 0);
      if (stack > 0) {
        long l = Convert.ToInt64(Math.Round(stack), CultureInfo.InvariantCulture);
        e.ManualReorderedPriority = l;
      }

      int rem = (int)Math.Round(GetField<double>(fields, "Microsoft.VSTS.Scheduling.RemainingWork", 0.0), 0);
      int comp = (int)Math.Round(GetField<double>(fields, "Microsoft.VSTS.Scheduling.CompletedWork", 0.0), 0);
      e.EstimatedEffort = rem;
      e.ProgressEffort = comp;
      e.EffortUnit = "hours";

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
    /// Extracts a string display name from an ADO identity field.
    /// </summary>
    private static string ExtractIdentityDisplay(JObject fields, string name) {
      if (fields[name] == null) {
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
    /// Converts DateTime (assumed UTC) to UNIX seconds.
    /// </summary>
    private static long ToUnix(DateTime dt) {
      DateTime utc = dt.ToUniversalTime();
      long unix = (long)(utc - new DateTime(1970, 1, 1)).TotalSeconds;
      return unix;
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
      Dictionary<string, object> projected = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
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
      Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
      d[nameof(WorkItem.Origin)] = e.Origin;
      d[nameof(WorkItem.StorageArea)] = e.StorageArea;
      d[nameof(WorkItem.OriginRelatedId)] = e.OriginRelatedId;
      d[nameof(WorkItem.Class)] = e.Class;
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
      return d;
    }

    /// <summary>
    /// Builds an entity from a flat dictionary (subset).
    /// </summary>
    private static WorkItem DictionaryToEntity(Dictionary<string, object> dict) {
      WorkItem e = new WorkItem();
      e.Origin = Read<string>(dict, nameof(WorkItem.Origin), "");
      e.StorageArea = Read<string>(dict, nameof(WorkItem.StorageArea), "/");
      e.OriginRelatedId = Read<string>(dict, nameof(WorkItem.OriginRelatedId), "");
      e.Class = Read<string>(dict, nameof(WorkItem.Class), "Generic.WorkItem");
      e.Title = Read<string>(dict, nameof(WorkItem.Title), "Untitled");
      e.BodyContent = Read<string>(dict, nameof(WorkItem.BodyContent), "");
      e.BodyContentMimetype = Read<string>(dict, nameof(WorkItem.BodyContentMimetype), "text/plain");
      e.AssignedTo = Read<string>(dict, nameof(WorkItem.AssignedTo), "");
      e.ProgressState = Read<string>(dict, nameof(WorkItem.ProgressState), "New");
      e.ManualReorderedPriority = Read<long>(dict, nameof(WorkItem.ManualReorderedPriority), 0);
      e.EstimatedEffort = Read<int>(dict, nameof(WorkItem.EstimatedEffort), 0);
      e.ProgressEffort = Read<int>(dict, nameof(WorkItem.ProgressEffort), 0);
      e.EffortUnit = Read<string>(dict, nameof(WorkItem.EffortUnit), "");
      object tagsRaw;
      if (dict.TryGetValue(nameof(WorkItem.Tags), out tagsRaw) && tagsRaw is IEnumerable<object>) {
        IEnumerable<object> en = (IEnumerable<object>)tagsRaw;
        e.Tags = en.Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)).ToArray();
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

  }

}

#endif
