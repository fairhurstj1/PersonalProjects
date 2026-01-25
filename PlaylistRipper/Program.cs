using System;
using System.IO;
using System.Threading.Tasks;
using PlaylistRipper.Core;
using PlaylistRipper.Models;

class Program
{
    static async Task Main()
    {
        var app = new App(
            new ConsoleUi(),
            new YtDlpClient(new ProcessRunner()),
            new DiskService(),
            new ZipOffloader(),
            new SessionStoreFacade()
        );

        await app.RunAsync();
    }
}
