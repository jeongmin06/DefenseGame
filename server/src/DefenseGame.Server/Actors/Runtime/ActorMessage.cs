namespace DefenseGame.Server.Actors.Runtime;

public sealed class ActorMessage<TActor> : IActorMessage where TActor : class
{
    private readonly TActor _actor;
    private readonly Func<TActor, CancellationToken, ValueTask> _action;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _registration;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ActorMessage(TActor actor, Func<TActor, CancellationToken, ValueTask> action,
        CancellationToken requestToken, CancellationToken stoppingToken)
    {
        _actor = actor;
        _action = action;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(requestToken, stoppingToken);
        _registration = _cancellation.Token.Register(() => _completion.TrySetCanceled(_cancellation.Token));
    }

    public Task Completion => _completion.Task;

    public async ValueTask RunAsync()
    {
        try
        {
            _cancellation.Token.ThrowIfCancellationRequested();
            await _action(_actor, _cancellation.Token);
            _completion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            _completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
        }
        finally
        {
            _registration.Dispose();
            _cancellation.Dispose();
        }
    }

    public void Fail(Exception exception) => _completion.TrySetException(exception);

    // Called only for messages that will never be executed.
    public void Cancel()
    {
        _completion.TrySetCanceled();
        _registration.Dispose();
        _cancellation.Dispose();
    }
}
