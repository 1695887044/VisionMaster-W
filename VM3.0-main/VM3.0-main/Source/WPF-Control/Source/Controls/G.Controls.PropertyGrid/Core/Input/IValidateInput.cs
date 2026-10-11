// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Controls.PropertyGrid
{
    public interface IValidateInput
    {
        event InputValidationErrorEventHandler InputValidationError;
        bool CommitInput();
    }
}

