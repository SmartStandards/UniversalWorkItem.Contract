using Microsoft.AspNetCore.DataProtection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// Persists the connector configurations as JSON file (default: '%LOCALAPPDATA%/UWIboard/connectors.json').
  /// Secrets are encrypted via ASP.NET Core DataProtection before they are written to disk.
  /// </summary>
  public class ConnectorConfigurationStore {

    private const string _ProtectorPurpose = "UWIboard.ConnectorSecrets.v1";

    private readonly string _FilePath;
    private readonly IDataProtector _Protector;
    private readonly object _Sync = new object();

    public ConnectorConfigurationStore(string filePath, IDataProtectionProvider dataProtectionProvider) {

      if (string.IsNullOrWhiteSpace(filePath)) {
        throw new ArgumentException("The file path must not be empty.", nameof(filePath));
      }

      if (dataProtectionProvider == null) {
        throw new ArgumentNullException(nameof(dataProtectionProvider));
      }

      _FilePath = filePath;
      _Protector = dataProtectionProvider.CreateProtector(_ProtectorPurpose);
    }

    /// <summary>
    /// Returns the default location of the configuration file within the local user profile.
    /// </summary>
    public static string GetDefaultFilePath() {

      string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

      return Path.Combine(baseDirectory, "UWIboard", "connectors.json");
    }

    public string FilePath {
      get {
        return _FilePath;
      }
    }

    /// <summary>
    /// Loads all configurations (secrets decrypted). Returns an empty array if there is no file yet.
    /// </summary>
    public ConnectorConfiguration[] LoadAll() {

      lock (_Sync) {

        if (!File.Exists(_FilePath)) {
          return new ConnectorConfiguration[0];
        }

        string json = File.ReadAllText(_FilePath);
        List<ConnectorConfiguration> stored = JsonConvert.DeserializeObject<List<ConnectorConfiguration>>(json);

        if (stored == null) {
          return new ConnectorConfiguration[0];
        }

        foreach (ConnectorConfiguration configuration in stored) {
          configuration.AccessToken = this.Unprotect(configuration.AccessToken);
        }

        return stored.ToArray();
      }

    }

    /// <summary>
    /// Adds or replaces the configuration with the same board id.
    /// </summary>
    public void Save(ConnectorConfiguration configuration) {

      if (configuration == null) {
        throw new ArgumentNullException(nameof(configuration));
      }

      lock (_Sync) {

        List<ConnectorConfiguration> all = this.LoadAll().Where(
          (candidate) => !string.Equals(candidate.BoardId, configuration.BoardId, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        all.Add(configuration);

        this.WriteAll(all);
      }

    }

    /// <summary>
    /// Removes the configuration with the given board id. Returns false if there was none.
    /// </summary>
    public bool Delete(string boardId) {

      lock (_Sync) {

        List<ConnectorConfiguration> all = this.LoadAll().ToList();
        int removedCount = all.RemoveAll(
          (candidate) => string.Equals(candidate.BoardId, boardId, StringComparison.OrdinalIgnoreCase)
        );

        if (removedCount == 0) {
          return false;
        }

        this.WriteAll(all);

        return true;
      }

    }

    private void WriteAll(List<ConnectorConfiguration> configurations) {

      List<ConnectorConfiguration> protectedCopies = new List<ConnectorConfiguration>();

      foreach (ConnectorConfiguration configuration in configurations) {

        ConnectorConfiguration copy = new ConnectorConfiguration();
        copy.BoardId = configuration.BoardId;
        copy.Title = configuration.Title;
        copy.ConnectorType = configuration.ConnectorType;
        copy.Owner = configuration.Owner;
        copy.RepositoryName = configuration.RepositoryName;
        copy.IsReadOnly = configuration.IsReadOnly;
        copy.MaxIssues = configuration.MaxIssues;
        copy.AccessToken = this.Protect(configuration.AccessToken);

        protectedCopies.Add(copy);
      }

      string directory = Path.GetDirectoryName(_FilePath);

      if (!string.IsNullOrEmpty(directory)) {
        Directory.CreateDirectory(directory);
      }

      string json = JsonConvert.SerializeObject(protectedCopies.OrderBy((configuration) => configuration.BoardId).ToArray(), Formatting.Indented);

      File.WriteAllText(_FilePath, json);
    }

    private string Protect(string secret) {

      if (string.IsNullOrEmpty(secret)) {
        return string.Empty;
      }

      return _Protector.Protect(secret);
    }

    private string Unprotect(string protectedSecret) {

      if (string.IsNullOrEmpty(protectedSecret)) {
        return string.Empty;
      }

      return _Protector.Unprotect(protectedSecret);
    }

  }

}
