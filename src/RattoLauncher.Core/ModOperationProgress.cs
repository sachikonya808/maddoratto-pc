namespace RattoLauncher.Core;

public readonly record struct ModOperationProgress(string Stage, int Done, int Total)
{
    public double? Fraction => Total > 0 ? (double)Done / Total : null;
}
