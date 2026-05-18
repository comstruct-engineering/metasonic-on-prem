namespace METASONIC_WEBSERVICE_DEMO
{
    partial class Form1
    {
        /// <summary>
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        /// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Vom Windows Form-Designer generierter Code

        /// <summary>
        /// Erforderliche Methode für die Designerunterstützung.
        /// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
        /// </summary>
        private void InitializeComponent()
        {
            this.label1 = new System.Windows.Forms.Label();
            this.txt_selectedQid = new System.Windows.Forms.TextBox();
            this.txt_LoadedDocID = new System.Windows.Forms.TextBox();
            this.btn_SaveDoc = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.txt_OldQidValue = new System.Windows.Forms.TextBox();
            this.txt_newQidValue = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.btn_LoadQid = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(13, 9);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(150, 30);
            this.label1.TabIndex = 0;
            this.label1.Text = "DocumentID";
            // 
            // txt_selectedQid
            // 
            this.txt_selectedQid.Location = new System.Drawing.Point(658, 19);
            this.txt_selectedQid.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.txt_selectedQid.Name = "txt_selectedQid";
            this.txt_selectedQid.Size = new System.Drawing.Size(134, 26);
            this.txt_selectedQid.TabIndex = 1;
            // 
            // txt_LoadedDocID
            // 
            this.txt_LoadedDocID.Location = new System.Drawing.Point(111, 6);
            this.txt_LoadedDocID.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.txt_LoadedDocID.Name = "txt_LoadedDocID";
            this.txt_LoadedDocID.Size = new System.Drawing.Size(206, 26);
            this.txt_LoadedDocID.TabIndex = 1;
            // 
            // btn_SaveDoc
            // 
            this.btn_SaveDoc.Location = new System.Drawing.Point(547, 127);
            this.btn_SaveDoc.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btn_SaveDoc.Name = "btn_SaveDoc";
            this.btn_SaveDoc.Size = new System.Drawing.Size(245, 33);
            this.btn_SaveDoc.TabIndex = 2;
            this.btn_SaveDoc.Text = "Dokument speichern";
            this.btn_SaveDoc.UseVisualStyleBackColor = true;
            this.btn_SaveDoc.Click += new System.EventHandler(this.btn_SaveDoc_Click);
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(325, 6);
            this.button1.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(191, 33);
            this.button1.TabIndex = 2;
            this.button1.Text = "Dokument laden";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Location = new System.Drawing.Point(2, 44);
            this.dataGridView1.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.ReadOnly = true;
            this.dataGridView1.RowHeadersWidth = 62;
            this.dataGridView1.Size = new System.Drawing.Size(514, 581);
            this.dataGridView1.TabIndex = 3;
            this.dataGridView1.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridView1_CellClick);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(554, 19);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(96, 20);
            this.label2.TabIndex = 0;
            this.label2.Text = "Qualifizierer:";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(554, 56);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(126, 30);
            this.label3.TabIndex = 0;
            this.label3.Text = "Alter Wert:";
            // 
            // txt_OldQidValue
            // 
            this.txt_OldQidValue.Location = new System.Drawing.Point(658, 56);
            this.txt_OldQidValue.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.txt_OldQidValue.Name = "txt_OldQidValue";
            this.txt_OldQidValue.Size = new System.Drawing.Size(387, 26);
            this.txt_OldQidValue.TabIndex = 1;
            // 
            // txt_newQidValue
            // 
            this.txt_newQidValue.Location = new System.Drawing.Point(658, 92);
            this.txt_newQidValue.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.txt_newQidValue.Name = "txt_newQidValue";
            this.txt_newQidValue.Size = new System.Drawing.Size(387, 26);
            this.txt_newQidValue.TabIndex = 1;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(554, 92);
            this.label4.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(141, 30);
            this.label4.TabIndex = 0;
            this.label4.Text = "Neuer Wert:";
            // 
            // btn_LoadQid
            // 
            this.btn_LoadQid.Location = new System.Drawing.Point(800, 19);
            this.btn_LoadQid.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.btn_LoadQid.Name = "btn_LoadQid";
            this.btn_LoadQid.Size = new System.Drawing.Size(245, 33);
            this.btn_LoadQid.TabIndex = 2;
            this.btn_LoadQid.Text = "Gewählte Qid Laden";
            this.btn_LoadQid.UseVisualStyleBackColor = true;
            this.btn_LoadQid.Click += new System.EventHandler(this.btn_LoadQid_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 634);
            this.Controls.Add(this.dataGridView1);
            this.Controls.Add(this.txt_newQidValue);
            this.Controls.Add(this.txt_OldQidValue);
            this.Controls.Add(this.txt_selectedQid);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txt_LoadedDocID);
            this.Controls.Add(this.btn_LoadQid);
            this.Controls.Add(this.btn_SaveDoc);
            this.Controls.Add(this.button1);
            this.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.Name = "Form1";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txt_selectedQid;
        private System.Windows.Forms.TextBox txt_LoadedDocID;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Button btn_SaveDoc;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txt_OldQidValue;
        private System.Windows.Forms.TextBox txt_newQidValue;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button btn_LoadQid;
    }
}

