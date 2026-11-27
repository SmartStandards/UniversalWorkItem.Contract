using Collaboration.WorkTracking.Connectors.GitHubIssues;
using System;
using System.Collections.Generic;
using System.Text;

namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// Connector configuration (the board setup), exposed to the configuration UI via UJMW.
  /// Secrets are write-only: they are never returned to the caller.
  /// </summary>
  public interface IConnectorConfigurationService {

    /// <summary>
    /// Lists the configured connector boards (secrets masked).
    /// </summary>
    ConnectorConfigurationDto[] GetConnectorConfigurations();

    /// <summary>
    /// Verifies the given settings by connecting to the target system (nothing is saved).
    /// Returns an empty string on success, otherwise the error message.
    /// </summary>
    string TestConnectorConfiguration(ConnectorConfigurationDto configuration);

    /// <summary>
    /// Adds or updates a connector board and (re-)registers it immediately. Returns the board id.
    /// </summary>
    string SaveConnectorConfiguration(ConnectorConfigurationDto configuration);

    /// <summary>
    /// Removes a connector board.
    /// </summary>
    bool DeleteConnectorConfiguration(string boardId);

  }

  /// <summary>
  /// Default implementation on top of the <see cref="ConnectorConfigurationStore"/> and the <see cref="BoardRegistry"/>.
  /// </summary>
  public class ConnectorConfigurationService : IConnectorConfigurationService {

    private readonly ConnectorConfigurationStore _Store;
    private readonly BoardRegistry _Registry;

    public ConnectorConfigurationService(ConnectorConfigurationStore store, BoardRegistry registry) {

      if (store == null) {
        throw new ArgumentNullException(nameof(store));
      }

      if (registry == null) {
        throw new ArgumentNullException(nameof(registry));
      }

      _Store = store;
      _Registry = registry;
    }

    /// <summary>
    /// Registers all stored connector boards. Boards whose connector cannot be created are skipped
    /// (the returned messages describe the problems).
    /// </summary>
    public static string[] RegisterStoredBoards(ConnectorConfigurationStore store, BoardRegistry registry) {

      List<string> problems = new List<string>();

      foreach (ConnectorConfiguration configuration in store.LoadAll()) {

        try {
          RegisterBoard(registry, configuration);
        }
        catch (ArgumentException ex) {
          problems.Add("Board '" + configuration.BoardId + "': " + ex.Message);
        }

      }

      return problems.ToArray();
    }

    /// <summary>
    /// Lists the configured connector boards (secrets masked).
    /// </summary>
    public ConnectorConfigurationDto[] GetConnectorConfigurations() {

      List<ConnectorConfigurationDto> result = new List<ConnectorConfigurationDto>();

      foreach (ConnectorConfiguration configuration in _Store.LoadAll()) {

        ConnectorConfigurationDto dto = new ConnectorConfigurationDto();
        dto.BoardId = configuration.BoardId;
        dto.Title = configuration.Title;
        dto.ConnectorType = configuration.ConnectorType;
        dto.Owner = configuration.Owner;
        dto.RepositoryName = configuration.RepositoryName;
        dto.IsReadOnly = configuration.IsReadOnly;
        dto.MaxIssues = configuration.MaxIssues;
        dto.AccessToken = string.Empty;
        dto.HasAccessToken = !string.IsNullOrEmpty(configuration.AccessToken);

        result.Add(dto);
      }

      return result.ToArray();
    }

    /// <summary>
    /// Verifies the given settings by connecting to the target system (nothing is saved).
    /// </summary>
    public string TestConnectorConfiguration(ConnectorConfigurationDto configuration) {

      try {

        ConnectorConfiguration effective = this.Merge(configuration);

        using (GitHubIssuesWorkItemRepository repository = CreateGitHubRepository(effective)) {
          repository.TestConnection();
        }

        return string.Empty;
      }
      catch (ArgumentException ex) {
        return ex.Message;
      }
      catch (InvalidOperationException ex) {
        return ex.Message;
      }
      catch (System.Net.Http.HttpRequestException ex) {
        return "GitHub is not reachable: " + ex.Message;
      }

    }

    /// <summary>
    /// Adds or updates a connector board and (re-)registers it immediately.
    /// </summary>
    public string SaveConnectorConfiguration(ConnectorConfigurationDto configuration) {

      ConnectorConfiguration effective = this.Merge(configuration);

      RegisterBoard(_Registry, effective);
      _Store.Save(effective);

      return effective.BoardId;
    }

    /// <summary>
    /// Removes a connector board.
    /// </summary>
    public bool DeleteConnectorConfiguration(string boardId) {

      bool deleted = _Store.Delete(boardId);

      if (deleted) {
        _Registry.Unregister(boardId);
      }

      return deleted;
    }

    /// <summary>
    /// Combines the settings of the UI with the stored ones (the secret is write-only, see <see cref="ConnectorConfigurationDto"/>).
    /// </summary>
    private ConnectorConfiguration Merge(ConnectorConfigurationDto dto) {

      if (dto == null) {
        throw new ArgumentException("No configuration was provided.");
      }

      if (!string.Equals(dto.ConnectorType, ConnectorTypes.GitHubIssues, StringComparison.OrdinalIgnoreCase)) {
        throw new ArgumentException("Unsupported connector type '" + dto.ConnectorType + "'.");
      }

      string owner = Trim(dto.Owner);
      string repositoryName = Trim(dto.RepositoryName);

      if (owner.Length == 0 || repositoryName.Length == 0) {
        throw new ArgumentException("Owner and repository must be specified.");
      }

      string boardId = Trim(dto.BoardId);

      if (boardId.Length == 0) {
        boardId = BuildBoardId(owner, repositoryName);
      }

      ConnectorConfiguration stored = this.FindStored(boardId);

      ConnectorConfiguration result = new ConnectorConfiguration();
      result.BoardId = boardId;
      result.ConnectorType = ConnectorTypes.GitHubIssues;
      result.Owner = owner;
      result.RepositoryName = repositoryName;
      result.IsReadOnly = dto.IsReadOnly;
      result.MaxIssues = dto.MaxIssues;

      if (result.MaxIssues <= 0) {
        result.MaxIssues = 500;
      }

      result.Title = Trim(dto.Title);

      if (result.Title.Length == 0) {
        result.Title = "GitHub: " + owner + "/" + repositoryName;
      }

      string accessToken = Trim(dto.AccessToken);

      if (dto.RemoveAccessToken) {
        result.AccessToken = string.Empty;
      }
      else if (accessToken.Length > 0) {
        result.AccessToken = accessToken;
      }
      else if (stored != null) {
        result.AccessToken = stored.AccessToken;
      }

      return result;
    }

    private ConnectorConfiguration FindStored(string boardId) {

      foreach (ConnectorConfiguration candidate in _Store.LoadAll()) {

        if (string.Equals(candidate.BoardId, boardId, StringComparison.OrdinalIgnoreCase)) {
          return candidate;
        }

      }

      return null;
    }

    private static void RegisterBoard(BoardRegistry registry, ConnectorConfiguration configuration) {

      GitHubIssuesWorkItemRepository repository = CreateGitHubRepository(configuration);

      registry.Register(configuration.BoardId, configuration.Title, configuration.IsReadOnly, repository);
    }

    private static GitHubIssuesWorkItemRepository CreateGitHubRepository(ConnectorConfiguration configuration) {

      string accessToken = null;

      if (!string.IsNullOrEmpty(configuration.AccessToken)) {
        accessToken = configuration.AccessToken;
      }

      return new GitHubIssuesWorkItemRepository(
        configuration.Owner, configuration.RepositoryName, accessToken, configuration.MaxIssues
      );
    }

    /// <summary>
    /// Builds a route-safe board id like 'github-owner-repo'.
    /// </summary>
    private static string BuildBoardId(string owner, string repositoryName) {

      StringBuilder builder = new StringBuilder("github-");

      foreach (char character in (owner + "-" + repositoryName).ToLowerInvariant()) {

        if (char.IsLetterOrDigit(character) || character == '-' || character == '_') {
          builder.Append(character);
        }
        else {
          builder.Append('-');
        }

      }

      return builder.ToString();
    }

    private static string Trim(string value) {

      if (value == null) {
        return string.Empty;
      }

      return value.Trim();
    }

  }

}
