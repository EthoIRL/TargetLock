using CircularBuffer;

namespace TargetLock;

public class Prediction
{
    private readonly CircularBuffer<(double, double)> _positions;
    private readonly CircularBuffer<(double, double)> _predictions;

    private readonly double _interp;

    public Prediction(double interp, int trackedStates)
    {
        _interp = interp;
        _positions = new(trackedStates);
        _predictions = new(trackedStates);
    }
    
    public (double deltaX, double deltaY) HandlePredictions(double deltaX, double deltaY)
    {
        if (_positions.Size < 2)
        {
            return (deltaX, deltaY);
        }
        
        var (relativeX, relativeY) = SmoothedVelocity();
        
        if (_predictions.Size > 1)
        {
            var (predX, predY) = _predictions[0];
        
            relativeX -= predX;
            relativeY -= predY;
        }
        
        AddPrediction(relativeX, relativeY);
        
        double predictedX = deltaX + relativeX * _interp;
        double predictedY = deltaY + relativeY * _interp;
        
        return (predictedX, predictedY);
    }

    public void Reset()
    {
        if (_positions.IsEmpty)
        {
            _positions.Clear();
            _predictions.Clear();
        }   
    }

    public void AddPosition(double x, double y)
    {
        _positions.PushFront((x, y));
    }

    private void AddPrediction(double x, double y)
    {
        _predictions.PushFront((x, y));
    }
    
    private (double x, double y) SmoothedVelocity()
    {
        int count = Math.Min(_positions.Size - 1, 9);
        double sumX = 0, sumY = 0;

        for (int i = 0; i < count; i++)
        {
            var (currX, currY) = _positions[i];
            var (prevX, prevY) = _positions[i + 1];
            sumX += currX - prevX;
            sumY += currY - prevY;
        }

        return (sumX / count, sumY / count);
    }
}