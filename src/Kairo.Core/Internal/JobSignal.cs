namespace Kairo.Internal;

/// <summary>
/// Wakes idle workers when a due job is created in this process, so they do not wait for the next poll.
/// </summary>
internal sealed class JobSignal
{
    private readonly Lock _lock = new Lock();
    private TaskCompletionSource _next = CreateNotification();

    /// <summary>
    /// A task that completes on the next <see cref="Notify"/>. Take it before looking for jobs,
    /// so a notification that arrives during the lookup is not missed.
    /// </summary>
    public Task NextNotification
    {
        get
        {
            lock (_lock)
            {
                return _next.Task;
            }
        }
    }

    public void Notify()
    {
        TaskCompletionSource current;
        lock (_lock)
        {
            current = _next;
            _next = CreateNotification();
        }

        current.TrySetResult();
    }

    private static TaskCompletionSource CreateNotification()
    {
        // Waiting workers continue on the thread pool, not inside Notify.
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
