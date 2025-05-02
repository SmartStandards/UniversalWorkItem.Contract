
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
      System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormMain));
      button1 = new System.Windows.Forms.Button();
      listView1 = new System.Windows.Forms.ListView();
      Origin = new System.Windows.Forms.ColumnHeader();
      Id = new System.Windows.Forms.ColumnHeader();
      Title = new System.Windows.Forms.ColumnHeader();
      this.SuspendLayout();
      // 
      // button1
      // 
      button1.Location = new System.Drawing.Point(12, 12);
      button1.Name = "button1";
      button1.Size = new System.Drawing.Size(187, 62);
      button1.TabIndex = 0;
      button1.Text = "Load WorkItems";
      button1.UseVisualStyleBackColor = true;
      button1.Click += this.button1_Click_1;
      // 
      // listView1
      // 
      listView1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
      listView1.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { Origin, Id, Title });
      listView1.FullRowSelect = true;
      listView1.GridLines = true;
      listView1.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
      listView1.Location = new System.Drawing.Point(12, 80);
      listView1.Name = "listView1";
      listView1.Size = new System.Drawing.Size(850, 320);
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
      // FormMain
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(874, 412);
      this.Controls.Add(listView1);
      this.Controls.Add(button1);
      this.Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
      this.Name = "FormMain";
      this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
      this.Text = "Universal-WorkItem Repository Connector (by Smart Standards)";
      this.ResumeLayout(false);
    }

    #endregion

    private System.Windows.Forms.Button button1;
    private System.Windows.Forms.ListView listView1;
    private System.Windows.Forms.ColumnHeader Origin;
    private System.Windows.Forms.ColumnHeader Id;
    private System.Windows.Forms.ColumnHeader Title;
  }
}

