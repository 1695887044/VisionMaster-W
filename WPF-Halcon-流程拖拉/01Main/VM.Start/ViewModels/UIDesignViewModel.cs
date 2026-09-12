using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Xaml;
using System.Xml;
using ICSharpCode.WpfDesign.Designer.PropertyGrid;
using ICSharpCode.WpfDesign.Designer.Services;
using ICSharpCode.WpfDesign.Designer.Xaml;
using ICSharpCode.WpfDesign.XamlDom;
using Microsoft.Win32;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Dialogs.Views;
using VM.Start.Services;
using VM.Start.UIDesign;
using XamlXmlWriter = ICSharpCode.WpfDesign.XamlDom.XamlXmlWriter;

namespace VM.Start.ViewModels
{
    [Serializable]
    public class UIDesignViewModel : NotifyPropertyBase
    {
        #region Singleton


        //private static readonly CameraSetViewModel _instance = new CameraSetViewModel();
        public UIDesignViewModel()
        {
            this.Documents = new ObservableCollection<Document>();
            this.RecentFiles = new ObservableCollection<string>();
            this.Views = new Dictionary<object, FrameworkElement>();
            this.LoadSettings();
        }

        public static UIDesignViewModel Ins
        {
            get
            {
                if (Solution.Ins.UIDesignViewModel == null)
                {
                    Solution.Ins.UIDesignViewModel = new UIDesignViewModel();
                }
                return Solution.Ins.UIDesignViewModel;
            }
        }

        #endregion


        public IPropertyGrid PropertyGrid { get; internal set; }

        public ObservableCollection<Document> Documents { get; private set; }

        public ObservableCollection<string> RecentFiles { get; private set; }

        public Dictionary<object, FrameworkElement> Views { get; private set; }

        public Document CurrentDocument
        {
            get { return this.currentDocument; }
            set
            {
                this.currentDocument = value;
                this.RaisePropertyChanged("CurrentDocument");
                this.RaisePropertyChanged("Title");
            }
        }

        public string Title
        {
            get
            {
                string result;
                if (this.CurrentDocument != null)
                {
                    result = this.CurrentDocument.Title + " - Xaml Designer";
                }
                else
                {
                    result = "Xaml Designer";
                }
                return result;
            }
        }

        public bool IsUseUIDesign
        {
            get { return this._IsUseUIDesign; }
            set
            {
                this._IsUseUIDesign = value;
                this.RaisePropertyChanged("IsUseUIDesign");
            }
        }

        public CommandBase LoadedCommand
        {
            get
            {
                if (this._LoadedCommand == null)
                {
                    this._LoadedCommand = new CommandBase(
                        delegate(object obj)
                        {
                            this.IsUseUIDesign = Solution.Ins.IsUseUIDesign;
                            if (this.init_flag)
                            {
                                this.init_flag = false;
                                this.New();
                            }
                            this.LoadUIDesign();
                        }
                    );
                }
                return this._LoadedCommand;
            }
        }

        public CommandBase NavOperateCommand
        {
            get
            {
                if (this._NavOperateCommand == null)
                {
                    this._NavOperateCommand = new CommandBase(
                        delegate(object obj)
                        {
                            string text = obj as string;
                            if (text != null)
                            {
                                if (text == "Preview")
                                {
                                    try
                                    {
                                        StringBuilder stringBuilder = new StringBuilder();
                                        XmlWriter writer = XmlWriter.Create(
                                            new StringWriter(stringBuilder)
                                        );
                                        UIDesignViewModel.Ins.CurrentDocument.DesignSurface.SaveDesigner(
                                            writer
                                        );
                                        string s = stringBuilder.ToString();
                                        XmlReader reader = XmlReader.Create(new StringReader(s));
                                        object obj2 = System.Windows.Markup.XamlReader.Load(reader);
                                        Window window = obj2 as Window;
                                        if (window == null)
                                        {
                                            window = new Window();
                                            window.Content = obj2;
                                        }
                                        window.Show();
                                        return;
                                    }
                                    catch (Exception)
                                    {
                                        MessageView.Ins.MessageBoxShow(
                                            "预览出错！",
                                            eMsgType.Warn,
                                            MessageBoxButton.OK,
                                            true
                                        );
                                        return;
                                    }
                                }
                                else if (text == "Redo")
                                {
                                    ApplicationCommands.Redo.Execute(null, null);
                                }
                                else if (text == "Undo")
                                {
                                    ApplicationCommands.Undo.Execute(null, null);
                                }
                                else if (text == "Save")
                                {
                                    this.SaveCurrentDocument();
                                }
                                else if (text == "Import")
                                {
                                    this.Open();
                                }
                                else if (text == "Export")
                                {
                                    this.SaveCurrentDocumentAs();
                                }
                            }
                        }
                    );
                }
                return this._NavOperateCommand;
            }
        }

        private void LoadSettings() { }

        public void SaveSettings() { }

        public static void ReportException(Exception x)
        {
            MessageBox.Show(x.ToString());
        }

        public void JumpToError(XamlError error)
        {
            if (this.CurrentDocument != null)
            {
                (this.Views[this.CurrentDocument] as DocumentView).JumpToError(error);
            }
        }

        public bool CanRefresh()
        {
            return this.CurrentDocument != null;
        }

        public void Refresh()
        {
            this.CurrentDocument.Refresh();
        }

        private bool IsSomethingDirty
        {
            get
            {
                foreach (Document document in UIDesignViewModel.Ins.Documents)
                {
                    if (document.IsDirty)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public void New()
        {
            this.Documents.Clear();
            Document item = new Document(
                "主界面",
                File.ReadAllText(FilePaths.UIDesignTemplateFilePath)
            );
            this.Documents.Add(item);
            this.CurrentDocument = item;
        }

        public void Open()
        {
            string text = this.AskOpenFileName();
            if (text != null)
            {
                this.Open(text);
            }
        }

        public string AskOpenFileName()
        {
            if (this.openFileDialog == null)
            {
                this.openFileDialog = new OpenFileDialog();
                this.openFileDialog.Filter = "Xaml Documents (*.xaml)|*.xaml";
            }
            string result;
            if (this.openFileDialog.ShowDialog().Value)
            {
                result = this.openFileDialog.FileName;
            }
            else
            {
                result = null;
            }
            return result;
        }

        public string AskSaveFileName(string initName)
        {
            if (this.saveFileDialog == null)
            {
                this.saveFileDialog = new SaveFileDialog();
                this.saveFileDialog.Filter = "Xaml Documents (*.xaml)|*.xaml";
            }
            this.saveFileDialog.FileName = initName;
            string result;
            if (this.saveFileDialog.ShowDialog().Value)
            {
                result = this.saveFileDialog.FileName;
            }
            else
            {
                result = null;
            }
            return result;
        }

        public void LoadUIDesign()
        {
            if (!string.IsNullOrEmpty(Solution.Ins.UIDesignText))
            {
                string uidesignText = Solution.Ins.UIDesignText;
                using (XmlReader xmlReader = XmlReader.Create(new StringReader(uidesignText)))
                {
                    this.CurrentDocument.DesignSurface.LoadDesigner(
                        xmlReader,
                        new XamlLoadSettings()
                    );
                    if (this.CurrentDocument == null)
                    {
                        this.CurrentDocument = new Document(
                            "主界面",
                            File.ReadAllText(FilePaths.UIDesignTemplateFilePath)
                        );
                    }
                    Solution.Ins.UIDesignText = uidesignText;
                    this.CurrentDocument.Text = uidesignText;
                    this.CurrentDocument.Refresh();
                }
            }
        }

        public void Open(string path)
        {
            path = Path.GetFullPath(path);
            if (this.RecentFiles.Contains(path))
            {
                this.RecentFiles.Remove(path);
            }
            this.RecentFiles.Insert(0, path);
            Stream stream = new FileStream(path, FileMode.Open);
            using (StreamReader streamReader = new StreamReader(stream))
            {
                string text = streamReader.ReadToEnd();
                using (XmlReader xmlReader = XmlReader.Create(new StringReader(text)))
                {
                    if (this.CurrentDocument == null)
                    {
                        this.CurrentDocument = new Document(
                            "主界面",
                            File.ReadAllText(FilePaths.UIDesignTemplateFilePath)
                        );
                    }
                    this.CurrentDocument.DesignSurface.LoadDesigner(
                        xmlReader,
                        new XamlLoadSettings()
                    );
                    Solution.Ins.UIDesignText = text;
                    this.CurrentDocument.Text = text;
                    this.CurrentDocument.Refresh();
                }
            }
        }

        public bool Save(Document doc)
        {
            if (doc.IsDirty)
            {
                if (doc.FilePath == null)
                {
                    return this.SaveAs(doc);
                }
                doc.Save();
            }
            return true;
        }

        public bool SaveAs(Document doc)
        {
            string initName = doc.FileName ?? (doc.Name + ".xaml");
            string text = this.AskSaveFileName(initName);
            bool result;
            if (text != null)
            {
                doc.SaveAs(text);
                result = true;
            }
            else
            {
                result = false;
            }
            return result;
        }

        public bool SaveAll()
        {
            foreach (Document doc in this.Documents)
            {
                if (!this.Save(doc))
                {
                    return false;
                }
            }
            return true;
        }

        public bool Close(Document doc)
        {
            if (doc.IsDirty)
            {
                MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show(
                    "Save \"" + doc.Name + "\" ?",
                    "Xaml Designer",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );
                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    if (!this.Save(doc))
                    {
                        return false;
                    }
                }
                else if (messageBoxResult == MessageBoxResult.Cancel)
                {
                    return false;
                }
            }
            this.Documents.Remove(doc);
            this.Views.Remove(doc);
            return true;
        }

        public bool CloseAll()
        {
            foreach (Document doc in this.Documents.ToArray<Document>())
            {
                if (!this.Close(doc))
                {
                    return false;
                }
            }
            return true;
        }

        public bool PrepareExit()
        {
            if (this.IsSomethingDirty)
            {
                MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show(
                    "Save All?",
                    "Xaml Designer",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );
                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    if (!this.SaveAll())
                    {
                        return false;
                    }
                }
                else if (messageBoxResult == MessageBoxResult.Cancel)
                {
                    return false;
                }
            }
            return true;
        }

        public bool SaveCurrentDocument()
        {
            bool result;
            try
            {
                var txt = this.CurrentDocument.Text;
                StringBuilder stringBuilder = new StringBuilder();
                using (XamlXmlWriter xamlXmlWriter = new XamlXmlWriter(stringBuilder))
                {
                    if (this.CurrentDocument != null && this.CurrentDocument.DesignSurface != null)
                    {
                        this.CurrentDocument.DesignSurface.SaveDesigner(xamlXmlWriter);
                    }
                }
                Solution.Ins.UIDesignText = stringBuilder.ToString();

                result = true;
            }
            catch (Exception)
            {
                result = false;
            }
            return result;
        }

        public void SaveCurrentDocumentAs()
        {
            this.SaveAs(this.CurrentDocument);
        }

        public void CloseCurrentDocument()
        {
            this.Close(this.CurrentDocument);
        }

        public const string ApplicationTitle = "Xaml Designer";

        [NonSerialized]
        private Document currentDocument;

        [NonSerialized]
        public bool _IsUseUIDesign = false;

        [NonSerialized]
        private OpenFileDialog openFileDialog;

        [NonSerialized]
        private SaveFileDialog saveFileDialog;

        [NonSerialized]
        private bool init_flag = true;

        [NonSerialized]
        private CommandBase _LoadedCommand;

        [NonSerialized]
        private CommandBase _NavOperateCommand;
    }
}
