using CircularBuffer;

namespace TargetLock;

public class Prediction
{
    public readonly CircularBuffer<(int x, int y)> MouseStates = new(20);
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
            if (count >= 20)
            {
                _hasMouseStates = true;
            }
        }

        if (_hasMouseStates)
        {
            var xArray = MouseStates.Select(x => x.Item1).ToArray();
            var yArray = MouseStates.Select(y => y.Item2).ToArray();

            if (deltaX == 0)
            {
                if (Math.Abs(xArray[1]) == 1 && xArray[2] == xArray[1])
                {
                    deltaX = xArray[1];
                }
            }
            
            if (deltaY == 0)
            {
                if (Math.Abs(yArray[1]) == 1 && yArray[2] == yArray[1])
                {
                    deltaY = yArray[1];
                }
            }

            if (SameValue(xArray, 3, deltaX))
            {
                deltaX = (int) Math.Floor(deltaX * _correction);
            }

            if (SameValue(yArray, 3, deltaY))
            {
                deltaY = (int) Math.Floor(deltaY * _correction);
            }
        }

        return (deltaX, deltaY);
    }


    private bool SameValue(int[] values, int length, int value, int offset = 0)
    {
        for (int i = offset; i < length; i++)
        {
            if (values[i] != value)
            {
                return false;
            }
        }

        return true;
    }
}