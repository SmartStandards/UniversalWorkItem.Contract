
namespace UniversalWorkItem.TestApp {

  partial class FormMain {
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing) {
      if (disposing && (components != null)) {
        components.Dispose();
      }
      base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent() {
      System.Data.Fuse.ExpressionTree expressionTree1 = new System.Data.Fuse.ExpressionTree();
      System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormMain));
      _BtnConnect = new System.Windows.Forms.Button();
      listView1 = new System.Windows.Forms.ListView();
      Origin = new System.Windows.Forms.ColumnHeader();
      Id = new System.Windows.Forms.ColumnHeader();
      Title = new System.Windows.Forms.ColumnHeader();
      _FuseTable = new System.Data.Fuse.FuseTable();
      _TxtOrg = new System.Windows.Forms.TextBox();
      _TxtPAT = new System.Windows.Forms.TextBox();
      _TxtTeamProject = new System.Windows.Forms.TextBox();
      this.SuspendLayout();
      // 
      // _BtnConnect
      // 
      _BtnConnect.Location = new System.Drawing.Point(12, 12);
      _BtnConnect.Name = "_BtnConnect";
      _BtnConnect.Size = new System.Drawing.Size(118, 84);
      _BtnConnect.TabIndex = 0;
      _BtnConnect.Text = "Connect";
      _BtnConnect.UseVisualStyleBackColor = true;
      _BtnConnect.Click += this.BtnConnect_Click;
      // 
      // listView1
      // 
      listView1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
      listView1.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { Origin, Id, Title });
      listView1.FullRowSelect = true;
      listView1.GridLines = true;
      listView1.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
      listView1.Location = new System.Drawing.Point(12, 102);
      listView1.Name = "listView1";
      listView1.Size = new System.Drawing.Size(992, 593);
      listView1.TabIndex = 1;
      listView1.UseCompatibleStateImageBehavior = false;
      listView1.View = System.Windows.Forms.View.Details;
      // 
      // Origin
      // 
      Origin.Text = "Origin";
      // 
      // Id
      // 
      Id.Text = "Id";
      // 
      // Title
      // 
      Title.Text = "Title";
      // 
      // _FuseTable
      // 
      _FuseTable.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
      _FuseTable.EntitySchema = null;
      expressionTree1.MatchAll = true;
      expressionTree1.Negate = false;
      expressionTree1.SubTree = null;
      _FuseTable.ExpressionTree = expressionTree1;
      _FuseTable.Limit = 100;
      _FuseTable.Location = new System.Drawing.Point(12, 102);
      _FuseTable.Name = "_FuseTable";
      _FuseTable.Size = new System.Drawing.Size(992, 593);
      _FuseTable.Skip = 0;
      _FuseTable.SortedBy = null;
      _FuseTable.TabIndex = 2;
      // 
      // _TxtOrg
      // 
      _TxtOrg.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
      _TxtOrg.Location = new System.Drawing.Point(136, 15);
      _TxtOrg.Name = "_TxtOrg";
      _TxtOrg.Size = new System.Drawing.Size(868, 23);
      _TxtOrg.TabIndex = 3;
      _TxtOrg.Text = "https://dev.azure.com/YOUR-ORG-NAME";
      // 
      // _TxtPAT
      // 
      _TxtPAT.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
      _TxtPAT.Location = new System.Drawing.Point(136, 73);
      _TxtPAT.Name = "_TxtPAT";
      _TxtPAT.Size = new System.Drawing.Size(868, 23);
      _TxtPAT.TabIndex = 3;
      _TxtPAT.Text = "PERSONAL-ACCESS-TOKEN";
      // 
      // _TxtTeamProject
      // 
      _TxtTeamProject.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
      _TxtTeamProject.Location = new System.Drawing.Point(136, 44);
      _TxtTeamProject.Name = "_TxtTeamProject";
      _TxtTeamProject.Size = new System.Drawing.Size(868, 23);
      _TxtTeamProject.TabIndex = 3;
      _TxtTeamProject.Text = "YOUT-TEAM-PROJECT";
      // 
      // FormMain
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(1016, 707);
      this.Controls.Add(_TxtPAT);
      this.Controls.Add(_TxtTeamProject);
      this.Controls.Add(_TxtOrg);
      this.Controls.Add(_FuseTable);
      this.Controls.Add(listView1);
      this.Controls.Add(_BtnConnect);
      this.Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
      this.Name = "FormMain";
      this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
      this.Text = "Universal-WorkItem Repository Connector (by Smart Standards)";
      this.ResumeLayout(false);
      this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.Button _BtnConnect;
    private System.Windows.Forms.ListView listView1;
    private System.Windows.Forms.ColumnHeader Origin;
    private System.Windows.Forms.ColumnHeader Id;
    private System.Windows.Forms.ColumnHeader Title;
    private System.Data.Fuse.FuseTable _FuseTable;
    private System.Windows.Forms.TextBox _TxtOrg;
    private System.Windows.Forms.TextBox _TxtPAT;
    private System.Windows.Forms.TextBox _TxtTeamProject;
  }
}

