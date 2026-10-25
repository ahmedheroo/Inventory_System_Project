signalr.min.js — the ASP.NET Core SignalR JavaScript client.

Package : @microsoft/signalr 8.0.29 (browser bundle, dist/browser/signalr.min.js)
Source  : https://www.npmjs.com/package/@microsoft/signalr
Licence : MIT — https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt
Fetched : npm pack @microsoft/signalr@8.0.29, then copied dist/browser/signalr.min.js unchanged.

It is the one file in the console that was not written here, and it is vendored rather
than referenced from a CDN so the console loads offline and cannot change under us.

The major version matches the ASP.NET Core version the services are built against
(net8.0), which is what the hub speaks.
