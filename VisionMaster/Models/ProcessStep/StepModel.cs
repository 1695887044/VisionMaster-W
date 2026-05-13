using GongSolutions.Wpf.DragDrop;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Media;
using UI.Events;
using VisionMaster.EventModel;

namespace VisionMaster.Models
{
    public abstract class StepModel:BindableBase
    {
        public Guid StepID { get; set; } = Guid.NewGuid();

        public string Icon { get; init; }
        public string PluginName { get; set; }

        public string PluginTypeName { get; init; }


        public string StepName
        {
            get => field;
            set
            {
                string oldName = field;
                if (SetProperty(ref field, value))
                {
                    if (!string.IsNullOrEmpty(oldName) && oldName != value)
                    {
                        GlobalEventBus.Publish(new StepRenamedMessage(oldName, value));
                    }
                }
            }
        }
        public string Description
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public int SortId { get; set; }

        public bool IsEnabled { get; set; } = true;

        public StepState State
        {
            get => field;
            set => SetProperty(ref field, value);
        }
        public Dictionary<string, object> InputValues { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, LinkReference> LinkedSources { get; set; } = new();

        public StepModel(string icon,string pluginName, string pluginTypeName, string stepName =null)
        {
            Icon=icon;
            PluginName=pluginName;
            this.PluginTypeName = pluginTypeName;
            StepName = stepName == null ? pluginName: stepName;
            Description = pluginName;
        }

    }
    public class ActionStep : StepModel
    {
        public ActionStep(string icon, string pluginName, string pluginTypeName, string stepName = null) : base(icon, pluginName, pluginTypeName, stepName)
        {

        }
    }



}
