using Microsoft.JSInterop;

namespace OBBX.Shared.Services;

public class JsConsole
{
    private readonly IJSRuntime JsRuntime;

    /// <summary>
    /// Initializes a new instance of the JsConsole class with the provided IJSRuntime.
    /// </summary>
    /// <param name="jSRuntime">The IJSRuntime instance to use for JavaScript interop</param>
    public JsConsole(IJSRuntime jSRuntime)
    {
        this.JsRuntime = jSRuntime;
    }

    /// <summary>
    /// Logs a message to the browser's JavaScript console.
    /// </summary>
    /// <param name="message">The message to log</param>
    /// <returns></returns>
    public async Task LogAsync(string message)
    {
        await this.JsRuntime.InvokeVoidAsync("console.log", message);
    }
}