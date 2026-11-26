namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    private static string? PlotScope(PlaygroundSpoilerMode mode)
        => mode == PlaygroundSpoilerMode.Full ? null
            : "避免主动揭示用户没有问到的关键反转和结局。用户问到的剧情照常展开讨论。";
}
