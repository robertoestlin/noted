# Build

Targets `net9.0-windows`. WSL's `dotnet` is 8.x and won't build it — use the Windows SDK:

```
'/mnt/c/Program Files/dotnet/dotnet.exe' build Noted.csproj -c Debug
```
