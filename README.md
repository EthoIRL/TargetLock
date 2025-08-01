# Target Lock
General purpose color aimbot. Meant to be used in conjunction with [**Helious**](https://github.com/StrateimTech/Helious) for remote mouse movement injection.

* Optimized for low latency calculations (~0.004ms ± 0.01ms / Capture & Calculation, with ~2% GPU utilization on a 4070 Ti Super)
* Combining both [Helious](https://github.com/StrateimTech/Helious)' (anti recoil), and Target Lock aimbot recoil is effectively (+Horizontal) gone at least when locked on.

## Showcase
![showcase.gif](showcase.gif)
_Showcased using OBS with an overlapping debug window_

## Features
* Slowdown [_Makes large snapping movements less obvious, but maintains speed when on target._]
* Smoothing [_Reduces movement speed statically to feel more legitimate_]
* Prediction [_Attempts to predict future movements (Not great, no ground truth)_]

## Known issues
* **Mouse acceleration must be turned off in windows (Enhance Pointer Precision), will cause overshooting/undershooting if enabled.**
* .NET causing high latency per image calculation during the first few minutes of startup.
* AOT compiling does NOT work with SharpDX.
* DirectX can only outputs the monitor's refresh rate (e.g. 144hz monitor, +240hz game)

## Battlebit issues
* Playing long periods (_Alt tabbing? idk what causes this_) BattleBit causes GPU utilization to max; timings get thrown off everything is wack causing weird jitter within the Aimbot. This seems to be an independent issue with BattleBit itself. **You can fix it by setting fps to 30 and then back to your regular frame rate**, gpu utilization will go down.
* Locks onto USA arm patches when on specific maps.

## Requirements
* CPU must have SSE4.2/AVX2 instruction set for SIMD-256 bit acceleration
  * If acceleration is not present see [here](https://github.com/EthoIRL/TargetLock/tree/6dd86d36c963a64d6ac32bc474083d27cc8e9d88)

## Helious setup
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

## Non-helious setup
You'll need to write your own mouse injector/mover, replace all occurrences of PreparePacket
```rust
byte[] PreparePacket(short deltaX, short deltaY)
```

## BBR Game Configuration
* Change ``Enemy Color`` to ``(0, 0, 255)`` (BLUE) in Gameplay -> Enemy Color.
* Change ``Enemy Icon Size`` to ``4.0`` in Gameplay -> Enemy Color (This is below Friendly Color & Icon size; blame BattleBit).
* Anti aliasing may cause movement jitter feel free to experiment.

## Requirements
* Windows 10 (11 is untested)
* Dedicated GPU (Integrated untested)
* Modified (High Performance UDP Server module) [Helious](https://github.com/StrateimTech/Helious) instance running a RPI4 or RPI5 connected via LAN.
* .NET 9

## Required Dependencies
* SharpDX w/ D3D11 & D2D
* CircularBuffer