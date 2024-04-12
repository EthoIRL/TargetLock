using CircularBuffer;

namespace TargetLock;

public class Prediction
{
    public readonly CircularBuffer<(int x, int y)> MouseStates = new(20);

    private readonly double _correction;
    
    public Prediction(double correction)
    {
        _correction = correction;
    }
    
    public (int deltaX, int deltaY) HandlePredictions(int deltaX, int deltaY)
    {
        var count = MouseStates.Count();
        if (count >= 20)
        {
            if (MouseStates[0].x == deltaX && MouseStates[1].x == deltaX && MouseStates[2].x == deltaX)
            {
                var good = true;
                for (int i = 3; i < 20; i++)
                {
                    if (MouseStates[i].x == -deltaX)
                    {
                        good = false;
                    }
                }
                    
                if (good)
                {
                    deltaX = (int) Math.Floor(deltaX * _correction);
                }
            }
                
            if (MouseStates[0].y == deltaY && MouseStates[1].y == deltaY && MouseStates[2].y == deltaY)
            {
                var good = true;
                for (int i = 2; i < 20; i++)
                {
                    if (MouseStates[i].y == -deltaY)
                    {
                        good = false;
                    }
                }
                    
                if (good)
                {
                    deltaY = (int) Math.Floor(deltaY * _correction);
                }
            }
        }

        return (deltaX, deltaY);
    }
}