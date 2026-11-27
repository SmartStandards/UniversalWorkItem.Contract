using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Collaboration.WorkTracking.Connectors.GitHubIssues {

  /// <summary>
  /// Minimal synchronous client for the GitHub REST API (issues of ONE repository).
  /// The access token (PAT) is optional - without a token only public repositories can be read
  /// (and the GitHub rate limit is very low).
  /// </summary>
  internal sealed class GitHubIssuesClient : IDisposable {

    private const string _ApiBaseUrl = "https://api.github.com/";
    private const string _ApiVersion = "2022-11-28";

    private readonly HttpClient _HttpClient;
    private readonly string _RepositoryPath;

    public GitHubIssuesClient(string owner, string repository, string accessToken) {

      if (string.IsNullOrWhiteSpace(owner)) {
        throw new ArgumentException("The repository owner must not be empty.", nameof(owner));
      }

      if (string.IsNullOrWhiteSpace(repository)) {
        throw new ArgumentException("The repository name must not be empty.", nameof(repository));
      }

#if !NETCOREAPP
      ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
#endif

      _RepositoryPath = "repos/" + Uri.EscapeDataString(owner.Trim()) + "/" + Uri.EscapeDataString(repository.Trim());

      _HttpClient = new HttpClient();
      _HttpClient.BaseAddress = new Uri(_ApiBaseUrl);
      _HttpClient.Timeout = TimeSpan.FromSeconds(30);
      _HttpClient.DefaultRequestHeaders.Add("User-Agent", "UniversalWorkItem-GitHubIssuesConnector");
      _HttpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
      _HttpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", _ApiVersion);

      if (!string.IsNullOrWhiteSpace(accessToken)) {
        _HttpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken.Trim());
      }

    }

    /// <summary>
    /// Loads the repository metadata (used to verify the connection).
    /// </summary>
    public JObject GetRepository() {

      string payload = this.Send(HttpMethod.Get, _RepositoryPath, null, false);

      return JObject.Parse(payload);
    }

    /// <summary>
    /// Loads one page of issues (open and closed, newest first).
    /// NOTE: GitHub also returns pull requests here (they have a 'pull_request' property).
    /// </summary>
    public JArray GetIssuePage(int page, int perPage) {

      string url = _RepositoryPath + "/issues?state=all&sort=created&direction=desc&per_page=" + perPage + "&page=" + page;
      string payload = this.Send(HttpMethod.Get, url, null, false);

      return JArray.Parse(payload);
    }

    /// <summary>
    /// Loads a single issue; returns null if it does not exist.
    /// </summary>
    public JObject TryGetIssue(int number) {

      string payload = this.Send(HttpMethod.Get, _RepositoryPath + "/issues/" + number, null, true);

      if (payload == null) {
        return null;
      }

      return JObject.Parse(payload);
    }

    /// <summary>
    /// Creates a new issue and returns it.
    /// </summary>
    public JObject CreateIssue(JObject body) {

      string payload = this.Send(HttpMethod.Post, _RepositoryPath + "/issues", body, false);

      return JObject.Parse(payload);
    }

    /// <summary>
    /// Updates the given properties of an issue and returns the updated issue.
    /// </summary>
    public JObject PatchIssue(int number, JObject body) {

      string payload = this.Send(new HttpMethod("PATCH"), _RepositoryPath + "/issues/" + number, body, false);

      return JObject.Parse(payload);
    }

    private string Send(HttpMethod method, string relativeUrl, JObject body, bool allowNotFound) {

      using (HttpRequestMessage request = new HttpRequestMessage(method, relativeUrl)) {

        if (body != null) {
          request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        }

        using (HttpResponseMessage response = _HttpClient.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult()) {

          string payload = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();

          if (response.IsSuccessStatusCode) {
            return payload;
          }

          if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) {
            return null;
          }

          throw new InvalidOperationException(
            "GitHub " + method.Method + " '" + relativeUrl + "' failed: " + (int)response.StatusCode + " " + GetErrorMessage(payload)
          );
        }

      }

    }

    private static string GetErrorMessage(string payload) {

      if (string.IsNullOrWhiteSpace(payload)) {
        return string.Empty;
      }

      try {

        JObject error = JObject.Parse(payload);
        JToken message = error["message"];

        if (message != null) {
          return message.ToString();
        }

      }
      catch (JsonReaderException) {
        // not a JSON payload - the raw text is reported below
      }

      if (payload.Length > 300) {
        return payload.Substring(0, 300);
      }

      return payload;
    }

    public void Dispose() {
      _HttpClient.Dispose();
    }

  }

}
