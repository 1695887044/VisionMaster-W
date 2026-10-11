using System.Runtime.CompilerServices;

namespace G.Services.Operation;

public interface IOperationService
{
    void Log<T>(string title, string message = null, OperationType operationType = OperationType.Default, bool result = true, [CallerMemberName] string methodName = null);
}