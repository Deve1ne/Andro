using Andro.Application.Paths;
using Andro.Domain.Prerequisites;

namespace Andro.Application.Abstractions;

public interface IPrerequisiteChecker
{
    Task<PrerequisiteReport> CheckAsync(AndroPaths paths, CancellationToken ct);
}
