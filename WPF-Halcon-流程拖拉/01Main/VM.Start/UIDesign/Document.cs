using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ICSharpCode.WpfDesign;
using ICSharpCode.WpfDesign.Designer;
using ICSharpCode.WpfDesign.Designer.Services;
using ICSharpCode.WpfDesign.Designer.Xaml;
using ICSharpCode.WpfDesign.XamlDom;

namespace VM.Start.UIDesign
{
    public class Document : INotifyPropertyChanged
    {
        public Document(string tempName, string text)
        {
            this.tempName = tempName;
            this.Text = text;
            this.IsDirty = false;
        }

        public Document(string filePath)
        {
            this.filePath = filePath;
            this.ReloadFile();
        }

        public string Text
        {
            get { return this.text; }
            set
            {
                if (this.text != value)
                {
                    this.text = value;
                    this.IsDirty = true;
                    this.RaisePropertyChanged("Text");
                }
            }
        }

        public DocumentMode Mode
        {
            get { return this.mode; }
            set
            {
                if (!this.mode.Equals(value))
                {
                    this.mode = value;
                    if (this.InDesignMode)
                    {
                        this.UpdateDesign();
                    }
                    else
                    {
                        this.UpdateXaml();
                        if (this.DesignContext.Services.Selection.PrimarySelection != null)
                        {
                            DesignItem primarySelection = this.DesignContext
                                .Services
                                .Selection
                                .PrimarySelection;
                            int lineNumber = (
                                (PositionXmlElement)
                                    ((XamlDesignItem)primarySelection).XamlObject.XmlElement
                            ).LineNumber;
                        }
                    }
                    this.RaisePropertyChanged("Mode");
                    this.RaisePropertyChanged("InXamlMode");
                    this.RaisePropertyChanged("InDesignMode");
                }
            }
        }

        public bool InXamlMode
        {
            get { return this.Mode == DocumentMode.Xaml; }
        }

        public bool InDesignMode
        {
            get { return this.Mode == DocumentMode.Design; }
        }

        public string FilePath
        {
            get { return this.filePath; }
            private set
            {
                this.filePath = value;
                this.RaisePropertyChanged("FilePath");
                this.RaisePropertyChanged("FileName");
                this.RaisePropertyChanged("Title");
                this.RaisePropertyChanged("Name");
            }
        }

        public bool IsDirty
        {
            get { return this.isDirty; }
            private set
            {
                this.isDirty = value;
                this.RaisePropertyChanged("IsDirty");
                this.RaisePropertyChanged("Name");
                this.RaisePropertyChanged("Title");
            }
        }

        public XamlElementLineInfo XamlElementLineInfo
        {
            get { return this.xamlElementLineInfo; }
            private set
            {
                this.xamlElementLineInfo = value;
                this.RaisePropertyChanged("XamlElementLineInfo");
            }
        }

        public string FileName
        {
            get
            {
                string result;
                if (this.FilePath == null)
                {
                    result = null;
                }
                else
                {
                    result = Path.GetFileName(this.FilePath);
                }
                return result;
            }
        }

        public string Name
        {
            get { return this.FileName ?? this.tempName; }
        }

        public string Title
        {
            get { return this.IsDirty ? (this.Name + "*") : this.Name; }
        }

        public DesignSurface DesignSurface
        {
            get
            {
                if (this.designSurface == null)
                {
                    this.designSurface = new DesignSurface();
                }
                return this.designSurface;
            }
        }

        public DesignContext DesignContext
        {
            get { return this.designSurface.DesignContext; }
        }

        public UndoService UndoService
        {
            get { return this.DesignContext.Services.GetService<UndoService>(); }
        }

        public ISelectionService SelectionService
        {
            get
            {
                ISelectionService result;
                if (this.InDesignMode)
                {
                    result = this.DesignContext.Services.Selection;
                }
                else
                {
                    result = null;
                }
                return result;
            }
        }

        public XamlErrorService XamlErrorService
        {
            get
            {
                XamlErrorService result;
                if (this.DesignContext != null)
                {
                    result = this.DesignContext.Services.GetService<XamlErrorService>();
                }
                else
                {
                    result = null;
                }
                return result;
            }
        }

        public IOutlineNode OutlineRoot
        {
            get { return this.outlineRoot; }
            set
            {
                this.outlineRoot = value;
                this.RaisePropertyChanged("OutlineRoot");
            }
        }

        private void ReloadFile()
        {
            this.Text = File.ReadAllText(this.FilePath);
            this.UpdateDesign();
            this.IsDirty = false;
        }

        public void Save()
        {
            if (this.InDesignMode)
            {
                this.UpdateXaml();
            }
            File.WriteAllText(this.FilePath, this.Text);
            this.IsDirty = false;
        }

        public void SaveAs(string filePath)
        {
            this.FilePath = filePath;
            this.Save();
        }

        public void Refresh()
        {
            this.UpdateXaml();
            this.UpdateDesign();
        }

        public void UpdateXaml()
        {
            StringBuilder stringBuilder = new StringBuilder();
            using (XamlXmlWriter xamlXmlWriter = new XamlXmlWriter(stringBuilder))
            {
                this.DesignSurface.SaveDesigner(xamlXmlWriter);
                Dictionary<XamlElementLineInfo, XamlElementLineInfo> source;
                this.Text = XamlFormatter.Format(stringBuilder.ToString(), out source);
                if (this.DesignSurface.DesignContext.Services.Selection.PrimarySelection != null)
                {
                    DesignItem primarySelection = this.DesignSurface
                        .DesignContext
                        .Services
                        .Selection
                        .PrimarySelection;
                    int line = (
                        (PositionXmlElement)((XamlDesignItem)primarySelection).XamlObject.XmlElement
                    ).LineNumber;
                    int pos = ((XamlDesignItem)primarySelection)
                        .XamlObject
                        .PositionXmlElement
                        .LinePosition;
                    this.XamlElementLineInfo = source
                        .FirstOrDefault(
                            (KeyValuePair<XamlElementLineInfo, XamlElementLineInfo> x) =>
                                x.Key.LineNumber == line && x.Key.LinePosition == pos
                        )
                        .Value;
                }
            }
        }

        public void UpdateDesign()
        {
            this.OutlineRoot = null;
            using (XmlReader xmlReader = XmlReader.Create(new StringReader(this.Text)))
            {
                this.DesignSurface.LoadDesigner(xmlReader, new XamlLoadSettings());
            }
            if (this.DesignContext.RootItem != null)
            {
                this.OutlineRoot = this.DesignContext.RootItem.CreateOutlineNode();
                this.UndoService.UndoStackChanged += this.UndoService_UndoStackChanged;
            }
            this.RaisePropertyChanged("SelectionService");
            this.RaisePropertyChanged("XamlErrorService");
        }

        public void UndoService_UndoStackChanged(object sender, EventArgs e)
        {
            this.IsDirty = true;
            if (this.InXamlMode)
            {
                this.UpdateXaml();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged(string name)
        {
            if (this.PropertyChanged != null)
            {
                this.PropertyChanged(this, new PropertyChangedEventArgs(name));
            }
        }

        [NonSerialized]
        private string tempName;

        [NonSerialized]
        private DesignSurface designSurface;

        [NonSerialized]
        private string text;

        [NonSerialized]
        private DocumentMode mode;

        [NonSerialized]
        private string filePath;

        [NonSerialized]
        private bool isDirty;

        [NonSerialized]
        public XamlElementLineInfo xamlElementLineInfo;

        [NonSerialized]
        private IOutlineNode outlineRoot;
    }
}
