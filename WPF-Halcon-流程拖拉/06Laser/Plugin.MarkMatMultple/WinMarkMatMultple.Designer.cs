
namespace Plugin.MarkMatMultple
{
    partial class WinMarkMatMultple
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WinMarkMatMultple));
            this.axMMMark_11 = new AxMMMark_1Lib.AxMMMark_1();
            ((System.ComponentModel.ISupportInitialize)(this.axMMMark_11)).BeginInit();
            this.SuspendLayout();
            // 
            // axMMMark_11
            // 
            this.axMMMark_11.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.axMMMark_11.Enabled = true;
            this.axMMMark_11.Location = new System.Drawing.Point(3, 3);
            this.axMMMark_11.Name = "axMMMark_11";
            this.axMMMark_11.OcxState = ((System.Windows.Forms.AxHost.State)(resources.GetObject("axMMMark_11.OcxState")));
            this.axMMMark_11.Size = new System.Drawing.Size(144, 144);
            this.axMMMark_11.TabIndex = 0;
            // 
            // UCMarkMatMultple
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.axMMMark_11);
            this.Name = "UCMarkMatMultple";
            ((System.ComponentModel.ISupportInitialize)(this.axMMMark_11)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private AxMMMark_1Lib.AxMMMark_1 axMMMark_11;
    }
}
