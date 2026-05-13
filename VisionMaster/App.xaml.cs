using Core.Interfaces;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Windows;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views;
using VisionMaster.Views.DialogViews;

namespace VisionMaster
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : PrismApplication
    {
        protected override Window CreateShell()
        {
            var thinFont = this.Resources["FA.Light"];
            this.Resources["Icon"] = thinFont; 
            App.Current.DispatcherUnhandledException += (s, e) => MessageBox.Show(e.Exception.Message);
            TaskScheduler.UnobservedTaskException += (s, e) => MessageBox.Show(e.Exception.Message);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            };
            return Container.Resolve<Shell>();

        }
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<SolutionService>();
            containerRegistry.RegisterSingleton<FlowCompiler>();
            containerRegistry.RegisterSingleton<IPluginProvider, PluginProvider>();
            containerRegistry.RegisterSingleton<IFlowEngine, FlowEngineService>();
            containerRegistry.RegisterSingleton<IRuntimeManager, RuntimeManager>();
            containerRegistry.RegisterSingleton<IExecutionContext, Services.ExecutionContext>();
            containerRegistry.RegisterSingleton<WorkspaceContext>();
            containerRegistry.Register<IReadOnlyWorkspaceContext>(c => c.Resolve<WorkspaceContext>());
            containerRegistry.Register<IWorkspaceManager>(c => c.Resolve<WorkspaceContext>());
            containerRegistry.RegisterSingleton<ILogService, LogService>();
            containerRegistry.RegisterForNavigation<LogView,LogViewModel>();
            containerRegistry.RegisterForNavigation<ProcessView, ProcessViewModel>();
            containerRegistry.RegisterForNavigation<ToolView, ToolViewModel>();
            containerRegistry.RegisterDialog<VariableBindingView, VariableBindingViewModel>("DataBindView");
            containerRegistry.RegisterDialog<GlobalVariableView, GlobalVariableManagerViewModel>("GlobalVariable");
            containerRegistry.RegisterDialog<ConditionEditorView, ConditionEditorViewModel>("ConditionEditor");
            containerRegistry.RegisterForNavigation<Shell, ShellViewModel>();
            var pluginService = containerRegistry.GetContainer().Resolve<PluginService>();
            pluginService.InitPlugins();

        }
    }

}
