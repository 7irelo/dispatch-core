namespace DispatchCore.Core.Models;

public sealed record JobEnvelope(Job Job, CancellationToken CancellationToken);
