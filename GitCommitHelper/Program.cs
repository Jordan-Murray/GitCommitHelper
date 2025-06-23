using GitCommitHelper.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;

ConsoleHelper.SetConsoleToFullScreen();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var model = config["OpenAI:Model"] ?? throw new InvalidOperationException("OpenAI:Model config is missing.");
    return new LlmService(model);
});
builder.Services.AddTransient<App>();

var host = builder.Build();

// Run the app
var app = host.Services.GetRequiredService<App>();
await app.RunAsync();

static class ConsoleHelper
{
    public static void SetConsoleToFullScreen()
    {
        IntPtr hConsole = GetConsoleWindow();
        ShowWindow(hConsole, SW_MAXIMIZE);
        Console.SetBufferSize(Console.LargestWindowWidth, Console.LargestWindowHeight);
        Console.SetWindowSize(Console.LargestWindowWidth, Console.LargestWindowHeight);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_MAXIMIZE = 3;
}