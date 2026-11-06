using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Exile.Runtime.Dqq;

/// <summary>DQQ.Web.Component 的部分 service 依赖 Blazor 的 NavigationManager；runtime 里没有页面，只记录跳转。</summary>
public sealed class RuntimeNavigationManager : NavigationManager
{
    public RuntimeNavigationManager() => Initialize("http://exile.runtime/", "http://exile.runtime/");

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Console.WriteLine($"[nav] DQQ 请求跳转 {uri}（runtime 中忽略）");
        Uri = ToAbsoluteUri(uri).ToString();
    }
}

/// <summary>runtime 没有浏览器 JS。任何调用都明确报错，便于发现哪些 service 依赖浏览器。</summary>
public sealed class NoBrowserJsRuntime : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => throw new InvalidOperationException($"Exile runtime 没有浏览器 JS，不能调用 '{identifier}'。");

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => throw new InvalidOperationException($"Exile runtime 没有浏览器 JS，不能调用 '{identifier}'。");
}
