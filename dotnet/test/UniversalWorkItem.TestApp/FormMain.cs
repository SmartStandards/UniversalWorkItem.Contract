using Collaboration.WorkTracking;
using Collaboration.WorkTracking.Connectors.AzureDevOps;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Fuse;
using System.Data.Fuse.Convenience;
using System.Data.ModelDescription;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UniversalWorkItem.TestApp {

  public partial class FormMain : Form {

    public FormMain() {
      this.InitializeComponent();

      this.FormClosing += this.FormMain_FormClosing;
      this.LoadFields();
    }

    private void FormMain_FormClosing(object sender, FormClosingEventArgs e) {
      this.SaveFields();
    }

    #region " Save/Load UI Inputs "

    private void LoadFields() {
      try {
        Func<string,string> decryptionMethod = (string encStr) => { return (new UTF8Encoding()).GetString(Convert.FromBase64String(Enumerable.Range(0, encStr.Length / 2).Select(i => encStr.Substring(i * 2, 2)).Select(x => (char)Convert.ToInt32(x, 16)).Aggregate(new StringBuilder(), (x, y) => x.Append(y)).ToString())); };
               
        string fileFullName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartStandards\\UniversalWorkItemTestApp\\uistate.json");
        if (!File.Exists(fileFullName))
          return;
        string rawJson = System.IO.File.ReadAllText(fileFullName, System.Text.Encoding.UTF8);
        var fields = JsonConvert.DeserializeObject<Dictionary<string, object>>(rawJson);
        object org;

        fields.TryGetValue("AzureDevOps_Organization", out org);
        _TxtOrg.Text = org as string ?? string.Empty;

        fields.TryGetValue("AzureDevOps_PAT", out org);
        _TxtPAT.Text = org as string ?? string.Empty;

        fields.TryGetValue("AzureDevOps_TeamProject", out org);
        _TxtTeamProject.Text = org as string ?? string.Empty;

        //txtAuthorizeUrl.Text = (string)fields["authorizeUrl"];
        //txtRedirectUrl.Text = (string)fields["redirectUrl"];
        //txtGrantType.Text = (string)fields["grantType"];
        //txtState.Text = (string)fields["state"];
        //txtScopeToRequest.Text = (string)fields["scopeToRequest"];
        //txtClientId.Text = decryptionMethod.Invoke((string)fields["clientId"]);
        //txtClientSecret.Text = decryptionMethod.Invoke((string)fields["clientSecret"]);
        //txtLoginHint.Text = (string)fields["loginHint"];
        //txtRetrievalUrl.Text = (string)fields["retrievalUrl"];
        //txtRetrievalCode.Text = (string)fields["retrievalCode"];
        //cckUseHttpGetToRetrieve.Checked = (bool)fields["useHttpGetToRetrieve"];
        /////////////////////////////////////////////////////////
        //txtIntrospectionUrl.Text = (string)fields["introspectionUrl"];
        /////////////////////////////////////////////////////////
        //txtCurrentToken.Text = (string)fields["currentTokenRaw"];
        //txtTokenContent.Text = (string)fields["currentTokenContent"];
      }
      catch {
      }
    }

    private void SaveFields() {
      try {
        Func<string, string> encryptionMethod = (string plaintext) => { return Convert.ToBase64String((new UTF8Encoding()).GetBytes(plaintext)).ToCharArray().Select(x => String.Format("{0:X}", (int)x)).Aggregate(new StringBuilder(), (x, y) => x.Append(y)).ToString(); };
        var fields = new Dictionary<string, object>();

        fields["AzureDevOps_Organization"] = _TxtOrg.Text;
        fields["AzureDevOps_PAT"] = _TxtPAT.Text;
        fields["AzureDevOps_TeamProject"] = _TxtTeamProject.Text;
        //fields["state"] = txtState.Text;
        //fields["scopeToRequest"] = txtScopeToRequest.Text;
        //fields["clientId"] = encryptionMethod.Invoke(txtClientId.Text);
        //fields["clientSecret"] = encryptionMethod.Invoke(txtClientSecret.Text);
        //fields["loginHint"] = txtLoginHint.Text;
        //fields["retrievalUrl"] = txtRetrievalUrl.Text;
        //fields["retrievalCode"] = txtRetrievalCode.Text;
        //fields["useHttpGetToRetrieve"] = cckUseHttpGetToRetrieve.Checked;
        /////////////////////////////////////////////////////////
        //fields["introspectionUrl"] = txtIntrospectionUrl.Text;
        /////////////////////////////////////////////////////////
        //fields["currentTokenRaw"] = txtCurrentToken.Text;
        //fields["currentTokenContent"] = txtTokenContent.Text;

        string rawJson = JsonConvert.SerializeObject(fields, Formatting.Indented);
        string fileFullName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartStandards\\UniversalWorkItemTestApp\\uistate.json");
        Directory.CreateDirectory(Path.GetDirectoryName(fileFullName));
        System.IO.File.WriteAllText(fileFullName, rawJson, System.Text.Encoding.UTF8);

      }
      catch {
      }
    }

    #endregion

    private SchemaRoot _SchemaRoot = null;
    private IRepository<WorkItem, WorkItemIdentity> _Repository = null;

    private void BtnConnect_Click(object sender, EventArgs e) {
      this.SaveFields();

      _SchemaRoot = ModelReader.GetSchema(new Type[] { typeof(WorkItem) }, true);

      _Repository = new AzureDevOpsWorkItemRepository(
        originBaseUrl: _TxtOrg.Text,
        personalAccessToken: _TxtPAT.Text,
        storageArea: _TxtTeamProject.Text,
        defaultWorkItemType: AzureDevOpsWorkItemRepository.KnownWorkItemTypes.Bug
      );

      _FuseTable.EntitySchema = _SchemaRoot.Entities.Where((e) => e.Name == nameof(WorkItem)).Single();
      _FuseTable.ExpressionTree = new ExpressionTree();
      _FuseTable.SortedBy = new string[] { nameof(WorkItem.ManualReorderedPriority), nameof(WorkItem.OriginRelatedId) };
      _FuseTable.Skip = 0;
      _FuseTable.Limit = 100;

      _FuseTable.BindToRepository(_Repository);

    }

  }

}
