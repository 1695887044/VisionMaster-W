// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using G.Common.Transitionable;

namespace G.Windows.Main.Commands;

public class TranslationCloseWindowCommand : CloseWindowCommand
{
    public override async void Execute(object parameter)
    {
        if (parameter is Window window)
        {
            var r = await this.ShowDialogMessage(window);
            if (r != true)
                return;

            if (window is ITransitionHostable hostable)
            {
                var task = hostable.Close(window);
                await task.ContinueWith(x =>
                  {
                      SystemCommands.CloseWindow(window);
                  });
            }
        }
    }
}

