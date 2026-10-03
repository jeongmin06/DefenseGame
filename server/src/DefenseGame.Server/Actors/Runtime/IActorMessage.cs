namespace DefenseGame.Server.Actors.Runtime;

public interface IActorMessage
{
    ValueTask RunAsync();
    void Cancel();
    void Fail(Exception exception);
}
