using CircularBuffer;

namespace TargetLock;

public class Prediction
{
    public readonly CircularBuffer<(int x, int y)> MouseStates = new(9);
    private bool _hasMouseStates;

    private readonly double _correction;

    public Prediction(double correction)
    {
        _correction = correction;
    }

    public (int deltaX, int deltaY) HandlePredictions(int deltaX, int deltaY)
    {
        if (!_hasMouseStates)
        {
            var count = MouseStates.Count();
            if (count >= 9)
            {
                _hasMouseStates = true;
            }
        }

        if (_hasMouseStates)
        {
            var xArray = MouseStates.Select(state => state.x).ToArray();
            var yArray = MouseStates.Select(state => state.y).ToArray();

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

    private bool SameSign(int[] values, int length, int value, int offset = 0)
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
        MouseStates.Clear();
        _hasMouseStates = false;
    }
}