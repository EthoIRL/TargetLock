using CircularBuffer;

namespace TargetLock;

public class Prediction
{
    private readonly CircularBuffer<double> _x;
    private readonly CircularBuffer<double> _y;

    private bool _hasMouseStates;

    private readonly double _correction;

    public Prediction(double correction, int trackedStates)
    {
        _correction = correction;
        _y = new(trackedStates);
        _x = new(trackedStates);
    }

    private int _totalPredictions;
    
    public (double deltaX, double deltaY) HandlePredictions(double deltaX, double deltaY)
    {
        if (!_hasMouseStates)
        {
            if (_totalPredictions >= 9)
            {
                _hasMouseStates = true;
            }
            
            _totalPredictions++;
        }

        _x.PushFront(deltaX);
        _y.PushFront(deltaY);
        
        if (_hasMouseStates)
        {
            var xAverage = _x.Average();
            var yAverage = _y.Average();

            var overCorrectedX = !SameSign(_x, 3, deltaX);
            var overCorrectedY = !SameSign(_y, 3, deltaY);

            var predictionX = xAverage * (overCorrectedX ? -_correction : _correction);
            var predictionY = yAverage * (overCorrectedY ? -_correction : _correction);

            if (overCorrectedX)
            {
                predictionX = deltaX;
            }

            if (overCorrectedY)
            {
                predictionY = deltaY;
            }

            if (deltaX == 0)
            {
                predictionX = 0;
            }

            if (deltaY == 0)
            {
                predictionY = 0;
            }

            return (predictionX, predictionY);
        }

        return (deltaX, deltaY);
    }

    private bool SameSign(IEnumerable<double> values, int length, double value, int offset = 0)
    {
        int i = 0;
        foreach (var internalValue in values)
        {
            if (offset > 0 && i < offset)
            {
                i++;

                continue;
            }

            if (internalValue > 0 != value > 0)
            {
                return false;
            } 

            i++;

            if (i >= offset + length)
            {
                break;
            }
        }

        return true;
    }

    public void Reset()
    {
        _x.Clear();
        _y.Clear();
        
        _totalPredictions = 0;
        _hasMouseStates = false;
    }
}