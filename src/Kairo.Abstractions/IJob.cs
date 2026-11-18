namespace Kairo;

/// <summary>
/// A background job without a payload. Kairo creates the job from dependency injection,
/// in a new scope for every run.
/// </summary>
/// <remarks>
/// Delivery is at-least-once: a job can run again after a crash or a lost lease,
/// so the work it does should be idempotent.
/// </remarks>
public interface IJob
{
    Task ExecuteAsync(JobContext context, CancellationToken cancellationToken);
}

/// <summary>
/// A background job that receives a payload. The payload is stored as JSON.
/// </summary>
/// <typeparam name="TPayload">The payload type. It must be serializable with System.Text.Json.</typeparam>
public interface IJob<in TPayload>
{
    Task ExecuteAsync(TPayload payload, JobContext context, CancellationToken cancellationToken);
}
