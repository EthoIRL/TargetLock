using CircularBuffer;

namespace TargetLock;

public class Prediction
{
    private readonly CircularBuffer<(double x, double y)> _mouseStates = new(9);
    private bool _hasMouseStates;

    private readonly double _correction;

    public Prediction(double correction)
    {
        _correction = correction;
    }

    public (double deltaX, double deltaY) HandlePredictions(double deltaX, double deltaY)
    {
        _mouseStates.PushFront((deltaX, deltaY));
        
        if (!_hasMouseStates)
        {
            var count = _mouseStates.Count();
            if (count >= 9)
            {
                _hasMouseStates = true;
            }
        }

        if (_hasMouseStates)
        {
            var xArray = _mouseStates.Select(state => state.x).ToArray();
            var yArray = _mouseStates.Select(state => state.y).ToArray();

            if (SameSign(xArray, 3, deltaX))
            {
                deltaX = (int) Math.Floor(deltaX * _correction);
            }

            if (SameSign(yArray, 3, deltaY))
            {
                deltaY = (int) Math.Floor(deltaY * _correction);
            }
        }

        return (deltaX, deltaY);
    }

    private bool SameSign(double[] values, int length, double value, int offset = 0)
    {
        for (int i = offset; i < length; i++)
        {
            if (values[i] > 0 != value > 0)
            {
                return false;
            }
        }

        return true;
    }

    public void Reset()
    {
        _mouseStates.Clear();
        _hasMouseStates = false;
    }
}