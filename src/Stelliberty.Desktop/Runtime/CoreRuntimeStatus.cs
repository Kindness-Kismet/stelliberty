using Stelliberty.Application.Runtime;

namespace Stelliberty.Desktop.Runtime;

internal sealed record CoreRuntimeStatus(CoreSnapshot Snapshot, long CoreGeneration);

internal sealed record CoreOperationResult(bool IsSuccess, string Message, CoreRuntimeStatus Status);
