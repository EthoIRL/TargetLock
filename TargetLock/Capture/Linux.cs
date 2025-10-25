namespace TargetLock.Capture;

public class Linux(int outputWidth, int outputHeight) : GenericCapturer(outputWidth, outputHeight)
{
    public override void StartCapture()
    {
        throw new NotImplementedException();
    }
}