using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
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
        var decryptionMethod = (string encStr) => { return (new UTF8Encoding()).GetString(Convert.FromBase64String(Enumerable.Range(0, encStr.Length / 2).Select(i => encStr.Substring(i * 2, 2)).Select(x => (char)Convert.ToInt32(x, 16)).Aggregate(new StringBuilder(), (x, y) => x.Append(y)).ToString())); };
        string fileFullName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartStandards\\UniversalWorkItemTestApp\\uistate.json");
        if (!File.Exists(fileFullName))
          return;
        string rawJson = System.IO.File.ReadAllText(fileFullName, System.Text.Encoding.UTF8);
        var fields = JsonConvert.DeserializeObject<Dictionary<string, object>>(rawJson);

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
        var encryptionMethod = (string plaintext) => { return Convert.ToBase64String((new UTF8Encoding()).GetBytes(plaintext)).ToCharArray().Select(x => String.Format("{0:X}", (int)x)).Aggregate(new StringBuilder(), (x, y) => x.Append(y)).ToString(); };
        var fields = new Dictionary<string, object>();

        //fields["authorizeUrl"] = txtAuthorizeUrl.Text;
        //fields["redirectUrl"] = txtRedirectUrl.Text;
        //fields["grantType"] = txtGrantType.Text;
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


    private void button1_Click_1(object sender, EventArgs e) {













    }

  }

}
