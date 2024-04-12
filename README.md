## Target Lock
General purpose color aimbot (with the intent for BattleBit Remastered). Meant to be used in conjunction with [**Helious**](https://github.com/StrateimTech/Helious) for remote mouse movement injection.

* Optimized for high performance/low latency calculations (~0.4ms / all calculations, Please note that this took up to ~3% gpu utilization on a 3060 Ti, it is CPU bound the entire time).
* Prioritize performance throughput & latency at the expense of (high cpu usage).
* Debug window (OpenCV / EmguCV, **WILL** decrease performance due to it's waitkey requirements).

## Basic Features
* Auto fire
* Custom fov
* Slowdown (Makes snapping less obvious; but maintains speed when on target)
* Smoothing (Slowdown but all the time)
* Basic Prediction (Motion acceleration)

## Known issues
* .NET causing high latency per image calculation during the first few minutes of startup (R2R is being used however it still seems to be an issue).
* Playing long periods (_Alt tabbing? idk what causes this_) BattleBit causes GPU utilization to max; timings get thrown off everything is wack causing weird jitter within the Aimbot. This seems to be an independent issue with BattleBit itself. **You can fix it by setting fps to 30 and then back to your regular frame rate**, gpu utilization will go down.
* Magnified scopes in BattleBit jitter if _**WaitForNewFrame**_ is off.
* AOT compiling does NOT work with SharpDX.
* Locks onto USA arm patch, really annoying can't really do anything about it easily. (Might switch to Pink or Purple later on).

# Setup
Make sure to point to your remote [Helious](https://github.com/StrateimTech/Helious) installation.
By default the port is 7483.
```c#
private static readonly IPAddress Broadcast = IPAddress.Parse("192.168.0.190");
```
All configuration settings are variables, I will not be implementing a console argument configurator I am lazy.

Publish for [R2R](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run) benefits.
``
dotnet publish -c Release -o publish
``
# Game Configuration
* Change ``Enemy Color`` to ``(0, 0, 255)`` (BLUE) in Gameplay -> Enemy Color.
* Change ``Enemy Icon Size`` to ``4.0`` in Gameplay -> Enemy Color (This is below Friendly Color & Icon size; blame BattleBit).
* Anti aliasing may cause movement jitter feel free to experiment. 