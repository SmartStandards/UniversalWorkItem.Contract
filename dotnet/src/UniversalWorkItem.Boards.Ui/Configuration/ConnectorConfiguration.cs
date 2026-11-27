namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// Well-known connector types which can be configured via the configuration UI.
  /// </summary>
  public static class ConnectorTypes {

    public const string GitHubIssues = "GitHubIssues";

  }

  /// <summary>
  /// The persisted configuration of one connector board (server-side only - contains the secret).
  /// </summary>
  public class ConnectorConfiguration {

    private string _BoardId = string.Empty;
    private string _Title = string.Empty;
    private string _ConnectorType = ConnectorTypes.GitHubIssues;
    private string _Owner = string.Empty;
    private string _RepositoryName = string.Empty;
    private string _AccessToken = string.Empty;
    private bool _IsReadOnly = false;
    private int _MaxIssues = 500;

    public string BoardId {
      get {
        return _BoardId;
      }
      set {
        _BoardId = value;
      }
    }

    public string Title {
      get {
        return _Title;
      }
      set {
        _Title = value;
      }
    }

    public string ConnectorType {
      get {
        return _ConnectorType;
      }
      set {
        _ConnectorType = value;
      }
    }

    /// <summary>
    /// GitHub: the user or organization owning the repository.
    /// </summary>
    public string Owner {
      get {
        return _Owner;
      }
      set {
        _Owner = value;
      }
    }

    /// <summary>
    /// GitHub: the repository name.
    /// </summary>
    public string RepositoryName {
      get {
        return _RepositoryName;
      }
      set {
        _RepositoryName = value;
      }
    }

    /// <summary>
    /// The secret (e.g. GitHub PAT). Never returned to the UI.
    /// </summary>
    public string AccessToken {
      get {
        return _AccessToken;
      }
      set {
        _AccessToken = value;
      }
    }

    public bool IsReadOnly {
      get {
        return _IsReadOnly;
      }
      set {
        _IsReadOnly = value;
      }
    }

    /// <summary>
    /// Upper limit of issues which are loaded (newest first).
    /// </summary>
    public int MaxIssues {
      get {
        return _MaxIssues;
      }
      set {
        _MaxIssues = value;
      }
    }

  }

  /// <summary>
  /// Connector configuration as exchanged with the UI: the secret is write-only
  /// (reading returns an empty token and 'HasAccessToken'; saving an empty token keeps the stored one).
  /// </summary>
  public class ConnectorConfigurationDto {

    private string _BoardId = string.Empty;
    private string _Title = string.Empty;
    private string _ConnectorType = ConnectorTypes.GitHubIssues;
    private string _Owner = string.Empty;
    private string _RepositoryName = string.Empty;
    private string _AccessToken = string.Empty;
    private bool _HasAccessToken = false;
    private bool _RemoveAccessToken = false;
    private bool _IsReadOnly = false;
    private int _MaxIssues = 500;

    /// <summary>
    /// Identifier of the board; empty = derived from the connector settings (new configuration).
    /// </summary>
    public string BoardId {
      get {
        return _BoardId;
      }
      set {
        _BoardId = value;
      }
    }

    public string Title {
      get {
        return _Title;
      }
      set {
        _Title = value;
      }
    }

    public string ConnectorType {
      get {
        return _ConnectorType;
      }
      set {
        _ConnectorType = value;
      }
    }

    public string Owner {
      get {
        return _Owner;
      }
      set {
        _Owner = value;
      }
    }

    public string RepositoryName {
      get {
        return _RepositoryName;
      }
      set {
        _RepositoryName = value;
      }
    }

    /// <summary>
    /// Write-only: a new secret (empty = keep the stored secret).
    /// </summary>
    public string AccessToken {
      get {
        return _AccessToken;
      }
      set {
        _AccessToken = value;
      }
    }

    /// <summary>
    /// Read-only information: true, if a secret is stored.
    /// </summary>
    public bool HasAccessToken {
      get {
        return _HasAccessToken;
      }
      set {
        _HasAccessToken = value;
      }
    }

    /// <summary>
    /// Write-only: true = delete the stored secret (anonymous access).
    /// </summary>
    public bool RemoveAccessToken {
      get {
        return _RemoveAccessToken;
      }
      set {
        _RemoveAccessToken = value;
      }
    }

    public bool IsReadOnly {
      get {
        return _IsReadOnly;
      }
      set {
        _IsReadOnly = value;
      }
    }

    public int MaxIssues {
      get {
        return _MaxIssues;
      }
      set {
        _MaxIssues = value;
      }
    }

  }

}
