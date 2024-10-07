using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace TargetLock;

#pragma warning disable CA1416
class Program
{
    // (500, 300)
    private static readonly (int width, int height) Resolution = (250, 100);

    private static readonly bool Slowdown = false;
    private static readonly int SlowRadius = 50;
    private static readonly double SlowSpeed = 0.2;

    private static readonly double SlowDivisorX = 1;
    private static readonly double SlowDivisorY = 1;

    private static readonly int CenterMouseX = Resolution.width / 2;
    private static readonly int CenterMouseY = Resolution.height / 2;

    private const byte RedTolerance = 130;
    private const byte GreenTolerance = 105;
    private const byte BlueMinimum = 230;

    private const byte BlueThreshold = 175;

    private static readonly bool UsePrediction = false;
    private static readonly Prediction Predictor = new(1.2, 9);

    private static readonly int StridePixels = Resolution.width * 4;

    private static readonly Socket Socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private static readonly IPAddress Broadcast = IPAddress.Parse("192.168.68.54");
    private static readonly IPEndPoint EndPoint = new(Broadcast, 7483);

    private static double _globalX;
    private static double _globalY;

    [SupportedOSPlatform("windows")]
    static void Main(string[] args)
    {
        Socket.Connect(EndPoint);

        new Thread(() =>
        {
            while (true)
            {
                if (_globalX != 0 || _globalY != 0)
                {
                    Socket.Send(PreparePacket((short) _globalX, (short) _globalY));
                    _globalX = 0;
                    _globalY = 0;
                }
            }
        }).Start();
        ScreenCapturer.StartCapture(0, 0, Resolution.width, Resolution.height);
    }

    private const int HeightStep = 5;
    
    public static void HandleImage(ref bool compute)
    {
        (int x, int y, double distance) closest = (0, -Int32.MaxValue, Double.MaxValue);

        unsafe
        {
            for (int y = Resolution.height - 1; y >= 0; y -= HeightStep)
            {
                if (compute)
                {
                    break;
                }

                byte* currentLine = (byte*) (ScreenCapturer.GpuImage.DataPointer + y * ScreenCapturer.GpuImage.RowPitch);

                for (int x = 0; x <= StridePixels; x += 4)
                {
                    if (compute)
                    {
                        break;
                    }

                    byte red = currentLine[x + 2];
                    byte green = currentLine[x + 1];
                    byte blue = currentLine[x];

                    var isBlue = IsBlue(red, green, blue);

                    if (!isBlue)
                    {
                        continue;
                    }

                    for (int i = HeightStep; i >= 0; i--)
                    {
                        if (compute)
                        {
                            break;
                        }

                        byte* offsetLine = (byte*) (ScreenCapturer.GpuImage.DataPointer + (y + i) * ScreenCapturer.GpuImage.RowPitch);

                        for (int x2 = StridePixels / 2; x2 >= 0; x2 -= 4)
                        {
                            if (IsBlue(offsetLine[x2 + 2], offsetLine[x2 + 1], offsetLine[x2]))
                            {
                                closest = (x2 >> 2, y + i, 1);
                                compute = true;

                                break;
                            }
                        }

                        for (int x2 = StridePixels / 2; x2 <= StridePixels; x2 += 4)
                        {
                            if (IsBlue(offsetLine[x2 + 2], offsetLine[x2 + 1], offsetLine[x2]))
                            {
                                closest = (x2 >> 2, y + i, 1);
                                compute = true;

                                break;
                            }
                        }
                    }
                }
            }
        }

        if (compute && closest.y != -Int32.MaxValue)
        {
            double deltaX = closest.x - CenterMouseX;
            double deltaY = closest.y - CenterMouseY + 1;

            deltaX /= SlowDivisorX;
            deltaY /= SlowDivisorY;

            if (Slowdown)
            {
                if (Math.Abs(deltaX) > SlowRadius)
                {
                    deltaX = (int) Math.Floor(deltaX * SlowSpeed);
                }

                if (Math.Abs(deltaY) > SlowRadius)
                {
                    deltaY = (int) Math.Floor(deltaY * SlowSpeed);
                }
            }

            if (UsePrediction)
            {
                if (Math.Abs(deltaX) > 50 || Math.Abs(deltaY) > 50)
                {
                    Predictor.Reset();
                }

                var predictions = Predictor.HandlePredictions(deltaX, deltaY);

                // Task.Run(() =>
                // {
                //     Console.WriteLine($"DeltaX: {deltaX} | DeltaY: {deltaY}");
                //     Console.WriteLine($"PDeltaX: {predictions.deltaX} | PDeltaY: {predictions.deltaY}");
                // });

                deltaX = predictions.deltaX;
                deltaY = predictions.deltaY;
            }

            _globalX = deltaX;
            _globalY = deltaY;
        }
        else
        {
            if (UsePrediction)
            {
                Predictor.Reset();
            }
        }
    }

    private static bool IsBlue(byte red, byte green, byte blue)
    {
        return green <= GreenTolerance && red <= RedTolerance && blue >= BlueMinimum || blue - green > BlueThreshold && blue - red > BlueThreshold;
    }

    private static byte[] PreparePacket(short deltaX, short deltaY)
    {
        return new[]
        {
            (byte) (deltaX & 0xFF), (byte) (deltaX >> 8), (byte) (deltaY & 0xFF), (byte) (deltaY >> 8)
        };
    }
}
#pragma warning restore CA1416