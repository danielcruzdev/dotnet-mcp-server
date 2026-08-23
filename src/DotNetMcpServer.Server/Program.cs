using DotNetMcpServer.Server.Hosting;

// The transport is chosen before anything is built, because the two hosts differ in more than
// one registration: one owns stdout as a protocol channel, the other binds a socket. stdio
// stays the default, so every client configuration written before this existed keeps working.
switch (ServerTransportSelection.Resolve(args))
{
    case ServerTransport.Http:
        await HttpServerHost.RunAsync(args);
        break;

    default:
        await StdioServerHost.RunAsync(args);
        break;
}
