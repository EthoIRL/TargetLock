namespace TargetLock;

public class PidController
{
    private double _kp;
    private double _ki;
    private double _kd;
    private double _integral;
    private double _previousError;
    
    public PidController(double kp, double ki, double kd)
    {
        _kp = kp;
        _ki = ki;
        _kd = kd;
        _integral = 0;
        _previousError = 0;
    }
    
    public double Calculate(double error)
    {
        _integral += error;
        
        double derivative = error - _previousError;
        double output = _kp * error + _ki * _integral + _kd * derivative;
        
        _previousError = error;
        
        return output;
    }

    public void Reset()
    {
        _integral = 0;
        _previousError = 0;
    }
}