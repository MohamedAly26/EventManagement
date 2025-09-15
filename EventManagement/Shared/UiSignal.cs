public sealed class UiSignal
{
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
}
