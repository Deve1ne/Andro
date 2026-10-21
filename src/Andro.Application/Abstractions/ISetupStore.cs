using Andro.Application.Setup;

namespace Andro.Application.Abstractions;

public interface ISetupStore
{
    Task<SetupState> LoadAsync();
    Task SaveAsync(SetupState state);
}
