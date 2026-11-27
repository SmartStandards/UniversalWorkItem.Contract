using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Fuse;
using System.Data.Fuse.LinqSupport;
using System.Globalization;
using System.Linq;

namespace Collaboration.WorkTracking.Connectors.GitHubIssues {

	/// <summary>
	/// WorkItem repository for the issues of ONE GitHub repository (see epic section 8.1).
	/// </summary>
	/// <remarks>
	///   Mapping:
	///   * Origin = 'github:owner/repo', OriginRelatedId = issue number, Class = 'GitHub.Issue'
	///   * ProgressState: open -> 'New', closed (completed) -> 'Done', closed (not planned) -> 'Discontinued';
	///     a label 'state:...' refines the state (e.g. 'state:Working.Implementation')
	///   * ConcernArea: label 'area:...' (default '/'), Tags: all other labels, AssignedTo: first assignee
	///   * pull requests are skipped
	///   Limitations of this first version:
	///   * the issues are cached for a short time and filtered/sorted/paged in memory
	///   * 'ManualReorderedPriority' is NOT persisted in GitHub yet (epic Q13 is open): it is kept in memory
	///     and defaults to a value derived from the issue number (newest on top)
	///   * deleting issues is not supported by the GitHub REST API (issues can only be closed)
	/// </remarks>
	public class GitHubIssuesWorkItemRepository : IWorkItemRepository, IDisposable {

		public const string IssueClass = "GitHub.Issue";

		private const string _AreaLabelPrefix = "area:";
		private const string _StateLabelPrefix = "state:";
		private const int _PageSize = 100;
		private const long _PriorityStepPerIssue = 1000000;

		private readonly string _Owner;
		private readonly string _RepositoryName;
		private readonly string _Origin;
		private readonly int _MaxIssues;
		private readonly TimeSpan _CacheDuration;
		private readonly GitHubIssuesClient _Client;

		private readonly object _CacheSync = new object();
		private WorkItem[] _CachedItems = null;
		private DateTime _CacheTimestamp = DateTime.MinValue;
		private readonly Dictionary<string, long> _PriorityOverlay = new Dictionary<string, long>(StringComparer.Ordinal);

		/// <summary>
		/// Creates a repository for the issues of the given GitHub repository.
		/// </summary>
		/// <param name="owner">The user or organisation owning the repository.</param>
		/// <param name="repositoryName">The name of the repository.</param>
		/// <param name="accessToken">A personal access token (fine-grained: 'Issues' read or read/write); null = anonymous (public repositories only).</param>
		/// <param name="maxIssues">The maximum number of issues to load (newest first).</param>
		/// <param name="cacheSeconds">How long loaded issues are reused before GitHub is asked again.</param>
		public GitHubIssuesWorkItemRepository(string owner, string repositoryName, string accessToken, int maxIssues = 500, int cacheSeconds = 30) {

			_Client = new GitHubIssuesClient(owner, repositoryName, accessToken);
			_Owner = owner.Trim();
			_RepositoryName = repositoryName.Trim();
			_Origin = BuildOrigin(_Owner, _RepositoryName);
			_MaxIssues = Math.Max(1, maxIssues);
			_CacheDuration = TimeSpan.FromSeconds(Math.Max(0, cacheSeconds));

		}

		/// <summary>
		/// Returns the origin used for the issues of the given repository ('github:owner/repo').
		/// </summary>
		public static string BuildOrigin(string owner, string repositoryName) {
			return "github:" + owner.Trim() + "/" + repositoryName.Trim();
		}

		public string Owner {
			get {
				return _Owner;
			}
		}

		public string RepositoryName {
			get {
				return _RepositoryName;
			}
		}

		/// <summary>
		/// Verifies the connection (repository exists and is accessible with the given token).
		/// Throws an exception with the GitHub error message otherwise.
		/// </summary>
		public void TestConnection() {

			_Client.GetRepository();

		}

		/// <summary>
		/// Discards the cached issues, so that the next read loads them from GitHub again.
		/// </summary>
		public void InvalidateCache() {

			lock (_CacheSync) {
				_CachedItems = null;
			}

		}

		#region " Loading & Mapping "

		private WorkItem[] GetAllItems() {

			lock (_CacheSync) {

				bool isCacheValid = (_CachedItems != null && DateTime.UtcNow - _CacheTimestamp < _CacheDuration);

				if (!isCacheValid) {
					_CachedItems = this.LoadAllItems();
					_CacheTimestamp = DateTime.UtcNow;
				}

				return _CachedItems;
			}

		}

		private WorkItem[] LoadAllItems() {

			List<WorkItem> result = new List<WorkItem>();
			int page = 1;

			while (result.Count < _MaxIssues) {

				JArray issues = _Client.GetIssuePage(page, _PageSize);

				foreach (JObject issue in issues.OfType<JObject>()) {

					if (issue["pull_request"] != null) {
						continue;
					}

					result.Add(this.MapIssue(issue));

					if (result.Count >= _MaxIssues) {
						break;
					}

				}

				if (issues.Count < _PageSize) {
					break;
				}

				page++;
			}

			return result.ToArray();
		}

		private WorkItem MapIssue(JObject issue) {

			WorkItem item = new WorkItem();

			long issueNumber = issue.Value<long>("number");
			string number = Convert.ToString(issueNumber, CultureInfo.InvariantCulture);

			item.Origin = _Origin;
			item.OriginRelatedId = number;
			item.Class = IssueClass;
			item.StorageArea = "/";
			item.Title = GetString(issue, "title");
			item.BodyContent = GetString(issue, "body");
			item.BodyContentMimetype = "text/markdown";

			string[] labels = GetLabelNames(issue);
			string areaLabel = FindPrefixedLabel(labels, _AreaLabelPrefix);
			string stateLabel = FindPrefixedLabel(labels, _StateLabelPrefix);

			item.ConcernArea = NormalizeArea(areaLabel);
			item.ProgressState = MapState(GetString(issue, "state"), GetString(issue, "state_reason"), stateLabel);
			item.Tags = labels.Where((label) => !IsReservedLabel(label)).ToArray();

			JArray assignees = issue["assignees"] as JArray;

			if (assignees != null && assignees.Count > 0) {
				item.AssignedTo = GetLogin(assignees[0]);
			}
			else {
				item.AssignedTo = string.Empty;
			}

			item.CreatedTimestamp = GetTimestamp(issue, "created_at");
			item.ModifiedTimestamp = GetTimestamp(issue, "updated_at");
			item.CreatedBy = GetLogin(issue["user"]);
			item.ModifiedBy = string.Empty;

			DateTime? closedAt = GetTimestamp(issue, "closed_at");

			if (closedAt.HasValue) {
				item.ProgressStateTimestamp = closedAt;
				item.ModifiedBy = GetLogin(issue["closed_by"]);
			}

			long priority;

			lock (_PriorityOverlay) {
				if (!_PriorityOverlay.TryGetValue(number, out priority)) {
					priority = issueNumber * _PriorityStepPerIssue;
				}
			}

			item.ManualReorderedPriority = priority;

			return item;
		}

		private static string MapState(string gitHubState, string stateReason, string stateLabel) {

			if (string.Equals(gitHubState, "closed", StringComparison.OrdinalIgnoreCase)) {

				string closedRootState = "Done";

				if (string.Equals(stateReason, "not_planned", StringComparison.OrdinalIgnoreCase)) {
					closedRootState = "Discontinued";
				}

				// a sub-state label is only used if it refines the root state given by GitHub
				if (stateLabel != null && stateLabel.StartsWith(closedRootState + ".", StringComparison.OrdinalIgnoreCase)) {
					return stateLabel;
				}

				return closedRootState;
			}

			if (stateLabel != null) {

				string rootState = GetRootState(stateLabel);

				if (rootState == "New" || rootState == "Working" || rootState == "Suspended") {
					return stateLabel;
				}

			}

			return "New";
		}

		private static string GetRootState(string state) {

			if (string.IsNullOrWhiteSpace(state)) {
				return "New";
			}

			int separatorIndex = state.IndexOf('.');

			if (separatorIndex < 0) {
				return state.Trim();
			}

			return state.Substring(0, separatorIndex).Trim();
		}

		private static string[] GetLabelNames(JObject issue) {

			JArray labels = issue["labels"] as JArray;

			if (labels == null) {
				return new string[0];
			}

			List<string> names = new List<string>();

			foreach (JToken label in labels) {

				JObject labelObject = label as JObject;

				if (labelObject != null) {
					names.Add(GetString(labelObject, "name"));
				}
				else {
					names.Add(label.ToString());
				}

			}

			return names.Where((name) => name.Length > 0).ToArray();
		}

		private static string FindPrefixedLabel(string[] labels, string prefix) {

			foreach (string label in labels) {
				if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
					return label.Substring(prefix.Length).Trim();
				}
			}

			return null;
		}

		private static bool IsReservedLabel(string label) {

			return (
				label.StartsWith(_AreaLabelPrefix, StringComparison.OrdinalIgnoreCase) ||
				label.StartsWith(_StateLabelPrefix, StringComparison.OrdinalIgnoreCase)
			);
		}

		private static string NormalizeArea(string area) {

			if (string.IsNullOrWhiteSpace(area)) {
				return "/";
			}

			string trimmed = area.Trim().Trim('/');

			if (trimmed.Length == 0) {
				return "/";
			}

			return "/" + trimmed;
		}

		private static string GetString(JObject source, string propertyName) {

			JToken token = source[propertyName];

			if (token == null || token.Type == JTokenType.Null) {
				return string.Empty;
			}

			return token.ToString();
		}

		private static string GetLogin(JToken user) {

			JObject userObject = user as JObject;

			if (userObject == null) {
				return string.Empty;
			}

			return GetString(userObject, "login");
		}

		private static DateTime? GetTimestamp(JObject source, string propertyName) {

			JToken token = source[propertyName];

			if (token == null || token.Type == JTokenType.Null) {
				return null;
			}

			if (token.Type == JTokenType.Date) {
				return token.Value<DateTime>().ToUniversalTime();
			}

			DateTime parsed;

			if (DateTime.TryParse(token.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed)) {
				return parsed;
			}

			return null;
		}

		private static int ParseIssueNumber(WorkItemIdentity key) {

			int number;

			if (key == null || !int.TryParse(key.OriginRelatedId, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) {
				return -1;
			}

			return number;
		}

		private bool IsOwnKey(WorkItemIdentity key) {

			if (key == null) {
				return false;
			}

			return string.Equals(key.Origin, _Origin, StringComparison.OrdinalIgnoreCase) && ParseIssueNumber(key) > 0;
		}

		#endregion

		#region " Filtering, Sorting & Projection (in memory) "

		private static bool Matches(WorkItem item, ExpressionTree filter) {

			if (filter == null) {
				return true;
			}

			List<bool> results = new List<bool>();

			if (filter.Predicates != null) {
				foreach (FieldPredicate predicate in filter.Predicates) {
					results.Add(MatchesPredicate(item, predicate));
				}
			}

			if (filter.SubTree != null) {
				foreach (ExpressionTree subTree in filter.SubTree) {
					results.Add(Matches(item, subTree));
				}
			}

			bool result = true;

			if (results.Count > 0) {
				if (filter.MatchAll) {
					result = results.All((value) => value);
				}
				else {
					result = results.Any((value) => value);
				}
			}

			if (filter.Negate) {
				return !result;
			}

			return result;
		}

		private static bool MatchesPredicate(WorkItem item, FieldPredicate predicate) {

			object fieldValue = item[predicate.FieldName];
			string expected = GetRawPredicateValue(predicate.ValueSerialized);
			string actual = FormatValue(fieldValue);

			switch (predicate.Operator) {

				case FieldOperators.Equal:
					return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

				case FieldOperators.NotEqual:
					return !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

				case FieldOperators.StartsWith:
					return actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase);

				case FieldOperators.EndsWith:
					return actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase);

				case FieldOperators.GreaterOrEqual:
					return CompareValues(actual, expected) >= 0;

				case FieldOperators.LessOrEqual:
					return CompareValues(actual, expected) <= 0;

				case FieldOperators.Less:
					return CompareValues(actual, expected) < 0;

				case FieldOperators.Greater:
					return CompareValues(actual, expected) > 0;

				default:
					throw new NotSupportedException("The filter operator '" + predicate.Operator + "' is not supported by the GitHub connector.");
			}

		}

		private static string FormatValue(object value) {

			if (value == null) {
				return string.Empty;
			}

			string[] array = value as string[];

			if (array != null) {
				return string.Join(",", array);
			}

			IFormattable formattable = value as IFormattable;

			if (formattable != null) {
				return formattable.ToString(null, CultureInfo.InvariantCulture);
			}

			return value.ToString();
		}

		private static int CompareValues(string actual, string expected) {

			decimal actualNumber;
			decimal expectedNumber;

			bool isActualNumeric = decimal.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out actualNumber);
			bool isExpectedNumeric = decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out expectedNumber);

			if (isActualNumeric && isExpectedNumeric) {
				return actualNumber.CompareTo(expectedNumber);
			}

			return string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Returns the (JSON serialized) predicate value as plain text (strings are unquoted, arrays are comma-joined).
		/// </summary>
		private static string GetRawPredicateValue(string valueSerialized) {

			if (string.IsNullOrEmpty(valueSerialized)) {
				return string.Empty;
			}

			JToken token;

			try {
				token = JToken.Parse(valueSerialized);
			}
			catch (Newtonsoft.Json.JsonReaderException) {
				// not valid JSON - the value is used as it is
				return valueSerialized;
			}

			if (token.Type == JTokenType.Null) {
				return string.Empty;
			}

			JArray array = token as JArray;

			if (array != null) {
				return string.Join(",", array.Select((element) => GetRawPredicateValue(element.ToString(Newtonsoft.Json.Formatting.None))));
			}

			JValue value = token as JValue;

			if (value != null) {
				return FormatValue(value.Value);
			}

			return token.ToString();
		}

		private static string[] ParseSearchTerms(string searchExpression) {

			if (string.IsNullOrWhiteSpace(searchExpression)) {
				return new string[0];
			}

			return searchExpression.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		}

		private static bool MatchesSearch(WorkItem item, string[] terms) {

			string text = item.Title + " " + item.BodyContent + " " + string.Join(" ", item.Tags) + " " + item.OriginRelatedId;

			return terms.All((term) => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
		}

		private static WorkItem[] SortAndPage(IEnumerable<WorkItem> items, string[] sortedBy, int limit, int skip) {

			IEnumerable<WorkItem> sorted = items;

			if (sortedBy != null && sortedBy.Length > 0) {
				sorted = items.ApplySorting(sortedBy);
			}

			if (skip > 0) {
				sorted = sorted.Skip(skip);
			}

			if (limit > 0) {
				sorted = sorted.Take(limit);
			}

			return sorted.ToArray();
		}

		private static Dictionary<string, object> ToFieldDictionary(WorkItem item, string[] includedFieldNames) {

			if (item == null) {
				return null;
			}

			if (includedFieldNames == null || includedFieldNames.Length == 0) {
				return JObject.FromObject(item).ToObject<Dictionary<string, object>>();
			}

			Dictionary<string, object> fields = new Dictionary<string, object>();

			foreach (string fieldName in includedFieldNames) {
				fields[fieldName] = item[fieldName];
			}

			return fields;
		}

		private static EntityRef<WorkItemIdentity> ToEntityRef(WorkItem item) {

			if (item == null) {
				return null;
			}

			return new EntityRef<WorkItemIdentity>(new WorkItemIdentity(item.Origin, item.OriginRelatedId), item.Title);
		}

		#endregion

		#region " Read "

		public string GetOriginIdentity() {
			return _Origin;
		}

		public RepositoryCapabilities GetCapabilities() {
			return new RepositoryCapabilities {
				CanReadContent = true,
				CanUpdateContent = true,
				CanAddNewEntities = true,
				CanDeleteEntities = false,
				SupportsMassupdate = false,
				SupportsKeyUpdate = false,
				SupportsStringBasedSearchExpressions = true,
				RequiresExternalKeys = false
			};
		}

		public WorkItem[] GetEntities(ExpressionTree filter, string[] sortedBy, int limit = 100, int skip = 0) {

			return SortAndPage(this.GetAllItems().Where((item) => Matches(item, filter)), sortedBy, limit, skip);

		}

		public WorkItem[] GetEntitiesBySearchExpression(string searchExpression, string[] sortedBy, int limit = 100, int skip = 0) {

			string[] terms = ParseSearchTerms(searchExpression);

			return SortAndPage(this.GetAllItems().Where((item) => MatchesSearch(item, terms)), sortedBy, limit, skip);
		}

		public WorkItem[] GetEntitiesByKey(WorkItemIdentity[] keysToLoad) {

			if (keysToLoad == null) {
				return new WorkItem[0];
			}

			List<WorkItem> result = new List<WorkItem>();

			foreach (WorkItemIdentity key in keysToLoad) {

				if (!this.IsOwnKey(key)) {
					continue;
				}

				JObject issue = _Client.TryGetIssue(ParseIssueNumber(key));

				if (issue != null && issue["pull_request"] == null) {
					result.Add(this.MapIssue(issue));
				}

			}

			return result.ToArray();
		}

		public Dictionary<string, object>[] GetEntityFields(ExpressionTree filter, string[] includedFieldNames, string[] sortedBy, int limit = 100, int skip = 0) {

			return this.GetEntities(filter, sortedBy, limit, skip).Select((item) => ToFieldDictionary(item, includedFieldNames)).ToArray();

		}

		public Dictionary<string, object>[] GetEntityFieldsBySearchExpression(string searchExpression, string[] includedFieldNames, string[] sortedBy, int limit = 100, int skip = 0) {

			return this.GetEntitiesBySearchExpression(searchExpression, sortedBy, limit, skip).Select((item) => ToFieldDictionary(item, includedFieldNames)).ToArray();

		}

		public Dictionary<string, object>[] GetEntityFieldsByKey(WorkItemIdentity[] keysToLoad, string[] includedFieldNames) {

			return this.GetEntitiesByKey(keysToLoad).Select((item) => ToFieldDictionary(item, includedFieldNames)).ToArray();

		}

		public EntityRef<WorkItemIdentity>[] GetEntityRefs(ExpressionTree filter, string[] sortedBy, int limit = 100, int skip = 0) {

			return this.GetEntities(filter, sortedBy, limit, skip).Select((item) => ToEntityRef(item)).ToArray();

		}

		public EntityRef<WorkItemIdentity>[] GetEntityRefsBySearchExpression(string searchExpression, string[] sortedBy, int limit = 100, int skip = 0) {

			return this.GetEntitiesBySearchExpression(searchExpression, sortedBy, limit, skip).Select((item) => ToEntityRef(item)).ToArray();

		}

		public EntityRef<WorkItemIdentity>[] GetEntityRefsByKey(WorkItemIdentity[] keysToLoad) {

			return this.GetEntitiesByKey(keysToLoad).Select((item) => ToEntityRef(item)).ToArray();

		}

		public bool ContainsKey(WorkItemIdentity key) {

			if (!this.IsOwnKey(key)) {
				return false;
			}

			foreach (WorkItem item in this.GetAllItems()) {
				if (string.Equals(item.OriginRelatedId, key.OriginRelatedId, StringComparison.Ordinal)) {
					return true;
				}
			}

			JObject issue = _Client.TryGetIssue(ParseIssueNumber(key));

			return (issue != null && issue["pull_request"] == null);
		}

		public int Count(ExpressionTree filter) {

			return this.GetAllItems().Count((item) => Matches(item, filter));

		}

		public int CountAll() {

			return this.GetAllItems().Length;

		}

		public int CountBySearchExpression(string searchExpression) {

			string[] terms = ParseSearchTerms(searchExpression);

			return this.GetAllItems().Count((item) => MatchesSearch(item, terms));
		}

		#endregion

		#region " Write "

		/// <summary>
		/// Updates the given fields of an existing issue. Supported fields: Title, BodyContent, ProgressState,
		/// ConcernArea, Tags, AssignedTo and ManualReorderedPriority (in memory only, see remarks of the class).
		/// Returns the fields which were updated, or null if the issue does not exist.
		/// </summary>
		public Dictionary<string, object> TryUpdateEntityFields(Dictionary<string, object> fields) {

			if (fields == null) {
				throw new ArgumentNullException(nameof(fields));
			}

			WorkItemIdentity key = GetKeyFromFields(fields);

			if (!this.IsOwnKey(key)) {
				return null;
			}

			int number = ParseIssueNumber(key);
			JObject current = _Client.TryGetIssue(number);

			if (current == null || current["pull_request"] != null) {
				return null;
			}

			WorkItem currentItem = this.MapIssue(current);
			Dictionary<string, object> updatedFields = new Dictionary<string, object>();
			JObject patch = this.BuildPatch(currentItem, fields, updatedFields);

			if (patch.Count > 0) {
				_Client.PatchIssue(number, patch);
			}

			this.InvalidateCache();

			return updatedFields;
		}

		private JObject BuildPatch(WorkItem currentItem, Dictionary<string, object> fields, Dictionary<string, object> updatedFields) {

			JObject patch = new JObject();
			string area = currentItem.ConcernArea;
			string state = currentItem.ProgressState;
			string[] tags = currentItem.Tags;
			bool labelsChanged = false;

			foreach (KeyValuePair<string, object> field in fields) {

				string fieldName = field.Key;

				if (IsField(fieldName, nameof(WorkItem.Origin)) || IsField(fieldName, nameof(WorkItem.OriginRelatedId))) {
					continue;
				}

				if (IsField(fieldName, nameof(WorkItem.Title))) {
					patch["title"] = ToText(field.Value);
				}
				else if (IsField(fieldName, nameof(WorkItem.BodyContent))) {
					patch["body"] = ToText(field.Value);
				}
				else if (IsField(fieldName, nameof(WorkItem.AssignedTo))) {

					string assignee = ToText(field.Value).Trim();
					JArray assignees = new JArray();

					if (assignee.Length > 0) {
						assignees.Add(assignee);
					}

					patch["assignees"] = assignees;
				}
				else if (IsField(fieldName, nameof(WorkItem.ProgressState))) {
					state = ToText(field.Value).Trim();
					ApplyState(patch, state);
					labelsChanged = true;
				}
				else if (IsField(fieldName, nameof(WorkItem.ConcernArea))) {
					area = NormalizeArea(ToText(field.Value));
					labelsChanged = true;
				}
				else if (IsField(fieldName, nameof(WorkItem.Tags))) {
					tags = ToStringArray(field.Value);
					labelsChanged = true;
				}
				else if (IsField(fieldName, nameof(WorkItem.ManualReorderedPriority))) {
					lock (_PriorityOverlay) {
						_PriorityOverlay[currentItem.OriginRelatedId] = Convert.ToInt64(field.Value, CultureInfo.InvariantCulture);
					}
				}
				else {
					throw new NotSupportedException("The field '" + fieldName + "' cannot be updated by the GitHub connector.");
				}

				updatedFields[fieldName] = field.Value;
			}

			if (labelsChanged) {
				patch["labels"] = new JArray(BuildLabels(area, state, tags));
			}

			return patch;
		}

		private static void ApplyState(JObject patch, string state) {

			string rootState = GetRootState(state);

			if (string.Equals(rootState, "Done", StringComparison.OrdinalIgnoreCase)) {
				patch["state"] = "closed";
				patch["state_reason"] = "completed";
			}
			else if (string.Equals(rootState, "Discontinued", StringComparison.OrdinalIgnoreCase)) {
				patch["state"] = "closed";
				patch["state_reason"] = "not_planned";
			}
			else {
				patch["state"] = "open";
			}

		}

		private static string[] BuildLabels(string area, string state, string[] tags) {

			List<string> labels = new List<string>();

			foreach (string tag in tags) {
				if (!string.IsNullOrWhiteSpace(tag) && !IsReservedLabel(tag)) {
					labels.Add(tag.Trim());
				}
			}

			string normalizedArea = NormalizeArea(area);

			if (normalizedArea != "/") {
				labels.Add(_AreaLabelPrefix + normalizedArea.Substring(1));
			}

			// the plain root states 'New', 'Done' and 'Discontinued' are fully represented by the GitHub state
			string rootState = GetRootState(state);

			bool needsStateLabel = (
				!string.Equals(state, rootState, StringComparison.Ordinal) ||
				string.Equals(rootState, "Working", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(rootState, "Suspended", StringComparison.OrdinalIgnoreCase)
			);

			if (needsStateLabel && !string.IsNullOrWhiteSpace(state)) {
				labels.Add(_StateLabelPrefix + state);
			}

			return labels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		}

		private static string ToText(object value) {

			if (value == null) {
				return string.Empty;
			}

			return Convert.ToString(value, CultureInfo.InvariantCulture);
		}

		private static string[] ToStringArray(object value) {

			if (value == null) {
				return new string[0];
			}

			string text = value as string;

			if (text != null) {
				return text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select((tag) => tag.Trim()).ToArray();
			}

			System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;

			if (enumerable != null) {
				return enumerable.Cast<object>().Select((element) => ToText(element)).ToArray();
			}

			return new string[] { ToText(value) };
		}

		private static bool IsField(string fieldName, string expectedName) {
			return string.Equals(fieldName, expectedName, StringComparison.OrdinalIgnoreCase);
		}

		private static WorkItemIdentity GetKeyFromFields(Dictionary<string, object> fields) {

			object origin = null;
			object id = null;

			foreach (KeyValuePair<string, object> field in fields) {
				if (IsField(field.Key, nameof(WorkItem.Origin))) {
					origin = field.Value;
				}
				else if (IsField(field.Key, nameof(WorkItem.OriginRelatedId))) {
					id = field.Value;
				}
			}

			if (origin == null || id == null) {
				return null;
			}

			return new WorkItemIdentity(ToText(origin), ToText(id));
		}

		private WorkItem LoadSingle(WorkItemIdentity key) {

			WorkItem[] loaded = this.GetEntitiesByKey(new WorkItemIdentity[] { key });

			if (loaded.Length == 0) {
				return null;
			}

			return loaded[0];
		}

		public WorkItem TryUpdateEntity(WorkItem entity) {

			if (entity == null) {
				throw new ArgumentNullException(nameof(entity));
			}

			Dictionary<string, object> fields = new Dictionary<string, object>();
			fields[nameof(WorkItem.Origin)] = entity.Origin;
			fields[nameof(WorkItem.OriginRelatedId)] = entity.OriginRelatedId;
			fields[nameof(WorkItem.Title)] = entity.Title;
			fields[nameof(WorkItem.BodyContent)] = entity.BodyContent;
			fields[nameof(WorkItem.ProgressState)] = entity.ProgressState;
			fields[nameof(WorkItem.ConcernArea)] = entity.ConcernArea;
			fields[nameof(WorkItem.Tags)] = entity.Tags;
			fields[nameof(WorkItem.AssignedTo)] = entity.AssignedTo;
			fields[nameof(WorkItem.ManualReorderedPriority)] = entity.ManualReorderedPriority;

			if (this.TryUpdateEntityFields(fields) == null) {
				return null;
			}

			return this.LoadSingle(entity);
		}

		/// <summary>
		/// Creates a new issue (the key of the given entity is ignored - GitHub assigns the issue number).
		/// </summary>
		public WorkItemIdentity TryAddEntity(WorkItem entity) {

			if (entity == null) {
				throw new ArgumentNullException(nameof(entity));
			}

			string[] tags = entity.Tags;

			if (tags == null) {
				tags = new string[0];
			}

			JObject body = new JObject();
			body["title"] = entity.Title;
			body["body"] = entity.BodyContent;
			body["labels"] = new JArray(BuildLabels(entity.ConcernArea, entity.ProgressState, tags));

			if (!string.IsNullOrWhiteSpace(entity.AssignedTo)) {
				body["assignees"] = new JArray(entity.AssignedTo.Trim());
			}

			JObject created = _Client.CreateIssue(body);
			int number = created.Value<int>("number");
			string numberText = Convert.ToString(number, CultureInfo.InvariantCulture);

			string rootState = GetRootState(entity.ProgressState);

			if (rootState == "Done" || rootState == "Discontinued") {
				JObject patch = new JObject();
				ApplyState(patch, entity.ProgressState);
				_Client.PatchIssue(number, patch);
			}

			if (entity.ManualReorderedPriority != 0) {
				lock (_PriorityOverlay) {
					_PriorityOverlay[numberText] = entity.ManualReorderedPriority;
				}
			}

			this.InvalidateCache();

			return new WorkItemIdentity(_Origin, numberText);
		}

		public WorkItem AddOrUpdateEntity(WorkItem entity) {

			if (entity == null) {
				throw new ArgumentNullException(nameof(entity));
			}

			if (this.IsOwnKey(entity)) {

				WorkItem updated = this.TryUpdateEntity(entity);

				if (updated != null) {
					return updated;
				}

			}

			WorkItemIdentity newKey = this.TryAddEntity(entity);

			return this.LoadSingle(newKey);
		}

		public Dictionary<string, object> AddOrUpdateEntityFields(Dictionary<string, object> fields) {

			if (fields == null) {
				throw new ArgumentNullException(nameof(fields));
			}

			Dictionary<string, object> updated = this.TryUpdateEntityFields(fields);

			if (updated != null) {
				return updated;
			}

			throw new NotSupportedException("Creating issues from a field dictionary is not supported by the GitHub connector - use 'TryAddEntity'.");
		}

		public WorkItemIdentity[] TryDeleteEntities(WorkItemIdentity[] keysToDelete) {
			throw new NotSupportedException("GitHub issues cannot be deleted via the REST API - set the state to 'Discontinued' instead.");
		}

		public WorkItemIdentity[] Massupdate(ExpressionTree filter, Dictionary<string, object> fields) {
			throw new NotSupportedException("Mass updates are not supported by the GitHub connector.");
		}

		public WorkItemIdentity[] MassupdateByKeys(WorkItemIdentity[] keysToUpdate, Dictionary<string, object> fields) {
			throw new NotSupportedException("Mass updates are not supported by the GitHub connector.");
		}

		public WorkItemIdentity[] MassupdateBySearchExpression(string searchExpression, Dictionary<string, object> fields) {
			throw new NotSupportedException("Mass updates are not supported by the GitHub connector.");
		}

		public bool TryUpdateKey(WorkItemIdentity currentKey, WorkItemIdentity newKey) {
			throw new NotSupportedException("Issue numbers are assigned by GitHub and cannot be changed.");
		}

		#endregion

		public void Dispose() {
			_Client.Dispose();
		}

	}

}
