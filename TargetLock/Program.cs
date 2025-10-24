using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
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

    private static readonly int CenterMouseX = Resolution.width / 2;
    private static readonly int CenterMouseY = Resolution.height / 2;

    private const byte RedTolerance = 130;
    private const byte GreenTolerance = 105;
    private const byte BlueMinimum = 230;

    private const byte BlueThreshold = 175;

    private static readonly bool UsePrediction = false;
    private static readonly Prediction Predictor = new(10, 9);

    private static readonly int StridePixels = Resolution.width * 4;

    private static readonly Socket Socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private static readonly IPAddress Broadcast = IPAddress.Parse("192.168.68.68");
    private static readonly IPEndPoint EndPoint = new(Broadcast, 7483);
    
    // PD Controller adjust to liking (Smoothness v. Jitter)
    private const double Kp = 0.7;
    private const double Kd = 0.3;
    
    private static double _lastErrorX;
    private static double _lastErrorY;

    private static double _globalX;
    private static double _globalY;

    private static readonly Vector256<short> BlueThresholdVec = Vector256.Create((short)BlueThreshold);
    private static readonly Vector256<short> GreenToleranceVec = Vector256.Create((short)GreenTolerance);
    private static readonly Vector256<short> RedToleranceVec = Vector256.Create((short)RedTolerance);
    private static readonly Vector256<short> BlueMinimumVec = Vector256.Create((short)BlueMinimum);

    private static readonly Vector256<byte> BlueShuffleMask = Vector256.Create(
        0, 4, 8, 12, 16, 20, 24, 28,
        0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,
        0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,
        0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF);
    
    private static readonly Vector256<byte> GreenShuffleMask = Vector256.Create(
        1, 5, 9, 13, 17, 21, 25, 29,
        0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,
        0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,
        0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF);
    
    private static readonly Vector256<byte> RedShuffleMask = Vector256.Create(
        2, 6, 10, 14, 18, 22, 26, 30,
        0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,
        0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,
        0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF);

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
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        ScreenCapturer.StartCapture(0, 0, Resolution.width, Resolution.height);
    }

    private const int HeightStep = 5;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void HandleImage()
    {
        unsafe
        {
            for (int y = Resolution.height - 1; y >= 0; y -= HeightStep)
            {
                byte* currentLine = (byte*)(ScreenCapturer.GpuImage.DataPointer + y * ScreenCapturer.GpuImage.RowPitch);
                
                for (int x = 0; x <= StridePixels - 32; x += 32)
                {
                    Vector256<byte> pixelData = Avx.LoadAlignedVector256(currentLine + x);
                
                    if (!IsBlueAvx2(pixelData))
                    {
                        continue;
                    }

                    for (int i = HeightStep; i >= 0; i--)
                    {
                        byte* offsetLine = currentLine + i * ScreenCapturer.GpuImage.RowPitch;
                        
                        for (int x2 = 0; x2 <= StridePixels - 32; x2 += 32)
                        {
                            Vector256<byte> pixelData2 = Avx.LoadAlignedVector256(offsetLine + x2);
                        
                            if (!IsBlueAvx2(pixelData2))
                            {
                                continue;
                            }
                            
                            int x3 = 0;
                            if (x2 > 0)
                            {
                                x3 = x2 - 32;
                            }
                            
                            for (; x3 <= StridePixels; x3 += 4)
                            {
                                if (IsBlue(offsetLine[x3 + 2], offsetLine[x3 + 1], offsetLine[x3]))
                                {
                                    HandleMovements(x3 >> 2, y + i);
                                    return;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        if (UsePrediction)
        {
            Predictor.Reset();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void HandleMovements(int offsetX, int offsetY)
    {
        double deltaX = offsetX - CenterMouseX;
        double deltaY = offsetY + 1 - CenterMouseY;
        
        if (UsePrediction)
        {
            var predictions = Predictor.HandlePredictions(deltaX, deltaY);

            Predictor.AddPosition(deltaX, deltaY);

            deltaX = predictions.deltaX;
            deltaY = predictions.deltaY;
        }
        
        double dErrorX = deltaX - _lastErrorX;
        double dErrorY = deltaY - _lastErrorY;
        
        double moveX = Kp * deltaX + Kd * dErrorX;
        double moveY = Kp * deltaY + Kd * dErrorY;

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

        _globalX = moveX;
        _globalY = moveY;
        
        _lastErrorX = deltaX;
        _lastErrorY = deltaY;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool IsBlueAvx2(Vector256<byte> pixelData)
    {
        Vector128<byte> blueLow = Avx2.Shuffle(pixelData, BlueShuffleMask).GetLower();
        Vector128<byte> greenLow = Avx2.Shuffle(pixelData, GreenShuffleMask).GetLower();
        Vector128<byte> redLow = Avx2.Shuffle(pixelData, RedShuffleMask).GetLower();
                    
        Vector256<short> blue16 = Avx2.ConvertToVector256Int16(blueLow);
        Vector256<short> green16 = Avx2.ConvertToVector256Int16(greenLow);
        Vector256<short> red16 = Avx2.ConvertToVector256Int16(redLow);
                    
        Vector256<short> blueMinusGreen = Avx2.Subtract(blue16, green16);
        Vector256<short> blueMinusRed = Avx2.Subtract(blue16, red16);
        Vector256<short> cmp1 = Avx2.CompareGreaterThan(blueMinusGreen, BlueThresholdVec);
        Vector256<short> cmp2 = Avx2.CompareGreaterThan(blueMinusRed, BlueThresholdVec);
        Vector256<short> cmpCombined1 = Avx2.And(cmp1, cmp2);
                    
        Vector256<short> cmpGreenLe = Avx2.CompareGreaterThan(GreenToleranceVec, green16);
        Vector256<short> cmpRedLe = Avx2.CompareGreaterThan(RedToleranceVec, red16);
        Vector256<short> cmpBlueGe = Avx2.CompareGreaterThan(blue16, BlueMinimumVec);
                    
        Vector256<short> cmpCombined2 = Avx2.And(Avx2.And(cmpGreenLe, cmpRedLe), cmpBlueGe);
                    
        Vector256<short> finalCmp = Avx2.Or(cmpCombined1, cmpCombined2);
        
        return !Avx.TestZ(finalCmp, finalCmp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool IsBlue(byte red, byte green, byte blue)
    {
        return blue - green > BlueThreshold && blue - red > BlueThreshold ||
               green <= GreenTolerance && red <= RedTolerance && blue >= BlueMinimum;
    }

    private static byte[] PreparePacket(short deltaX, short deltaY)
    {
        return
        [
            (byte) (deltaX & 0xFF), (byte) (deltaX >> 8), (byte) (deltaY & 0xFF), (byte) (deltaY >> 8)
        ];
    }
}
#pragma warning restore CA1416