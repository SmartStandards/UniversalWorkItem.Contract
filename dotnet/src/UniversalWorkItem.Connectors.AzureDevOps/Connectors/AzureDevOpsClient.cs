using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;

#if NET5_0_OR_GREATER

namespace Collaboration.WorkTracking.Connectors {

  /// <summary>
  /// Simple synchronous HttpClient wrapper for Azure DevOps REST API using PAT.
  /// </summary>
  internal sealed class AzureDevOpsClient : IDisposable {
    private readonly HttpClient _HttpClient;
    private readonly string _BaseUrl; // e.g. https://dev.azure.com/MyOrg

    /// <summary>
    /// Ctor.
    /// </summary>
    public AzureDevOpsClient(string baseUrl, string personalAccessToken) {
      if (string.IsNullOrWhiteSpace(baseUrl)) {
        throw new ArgumentException("baseUrl must not be empty.");
      }
      if (string.IsNullOrWhiteSpace(personalAccessToken)) {
        throw new ArgumentException("personalAccessToken must not be empty.");
      }

      _BaseUrl = baseUrl.TrimEnd('/');

      HttpClientHandler handler = new HttpClientHandler();
      _HttpClient = new HttpClient(handler);
      string pat = ":" + personalAccessToken;
      string patB64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(pat));
      _HttpClient.DefaultRequestHeaders.Clear();
      _HttpClient.DefaultRequestHeaders.Add("Authorization", "Basic " + patB64);
    }

    /// <summary>
    /// Builds a full API url for a project-scoped path.
    /// </summary>
    public string BuildProjectApiUrl(string project, string relative) {
      string projectSan = project.Trim('/').Trim();
      string relativeSan = relative.TrimStart('/');
      return _BaseUrl + "/" + projectSan + "/_apis/" + relativeSan;
    }

    /// <summary>
    /// Builds an org-scoped API url.
    /// </summary>
    public string BuildOrgApiUrl(string relative) {
      string relativeSan = relative.TrimStart('/');
      return _BaseUrl + "/_apis/" + relativeSan;
    }

    /// <summary>
    /// Performs a GET and returns JSON.
    /// </summary>
    public JObject GetJson(string url) {
      HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
      HttpResponseMessage resp = _HttpClient.Send(req);
      string payload = resp.Content.ReadAsStringAsync().Result;
      if (resp.IsSuccessStatusCode) {
        return JObject.Parse(payload);
      }

      throw new InvalidOperationException("GET " + url + " failed: " + (int)resp.StatusCode + " " + resp.ReasonPhrase + " - " + payload);
    }

    /// <summary>
    /// Performs a POST with JSON and returns JSON.
    /// </summary>
    public JObject PostJson(string url, object body) {
      string json = JsonConvert.SerializeObject(body);
      HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
      req.Content = new StringContent(json, Encoding.UTF8, "application/json");
      HttpResponseMessage resp = _HttpClient.Send(req);
      string payload = resp.Content.ReadAsStringAsync().Result;
      if (resp.IsSuccessStatusCode) {
        return JObject.Parse(payload);
      }

      throw new InvalidOperationException("POST " + url + " failed: " + (int)resp.StatusCode + " " + resp.ReasonPhrase + " - " + payload);
    }

    /// <summary>
    /// Performs a PATCH (JSON Patch) and returns JSON.
    /// </summary>
    public JObject PatchJson(string url, JArray patchDoc) {
      HttpRequestMessage req = new HttpRequestMessage(new HttpMethod("PATCH"), url);
      req.Content = new StringContent(patchDoc.ToString(Formatting.None), Encoding.UTF8, "application/json-patch+json");
      HttpResponseMessage resp = _HttpClient.Send(req);
      string payload = resp.Content.ReadAsStringAsync().Result;
      if (resp.IsSuccessStatusCode) {
        return JObject.Parse(payload);
      }

      throw new InvalidOperationException("PATCH " + url + " failed: " + (int)resp.StatusCode + " " + resp.ReasonPhrase + " - " + payload);
    }

    /// <summary>
    /// Performs a DELETE and returns whether it succeeded.
    /// </summary>
    public bool Delete(string url) {
      HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Delete, url);
      HttpResponseMessage resp = _HttpClient.Send(req);
      if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.NotFound) {
        return true;
      }

      string payload = resp.Content.ReadAsStringAsync().Result;
      throw new InvalidOperationException("DELETE " + url + " failed: " + (int)resp.StatusCode + " " + resp.ReasonPhrase + " - " + payload);
    }

    public void Dispose() {
      _HttpClient.Dispose();
    }
  }

}

#endif