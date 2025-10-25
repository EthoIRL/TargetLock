namespace TargetLock.Capture;

public abstract class GenericCapturer(int outputWidth, int outputHeight)
{
    public readonly int OutputWidth = outputWidth;
    public readonly int OutputHeight = outputHeight;

    public abstract void StartCapture();
}