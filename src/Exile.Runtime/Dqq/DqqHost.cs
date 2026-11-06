using DQQ.Components.Stages.Actors.Characters;
using DQQ.Repositories;
using DQQ.Services.ActorServices;
using DQQ.Web;
using DQQ.Web.Enums;
using DQQ.Web.Services.DQQAuthServices;
using DQQ.Web.Services.Requests;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using ReheeCmf.Commons.DTOs;
using DQQ.Helper;
using ReheeCmf.Helpers;
using ReheeCmf.Requests;

namespace Exile.Runtime.Dqq;

/// <summary>
/// 在普通 .NET 进程中承载 DQQ.Web.Component 的 service（D14、D15）。
/// 使用 DQQ 原版 ComponentInit.AddServices 注册，只替换浏览器相关的依赖（存储、导航、JS）。
/// </summary>
public sealed class DqqHost : IAsyncDisposable
{
    private readonly ServiceProvider root;
    private readonly AsyncServiceScope scope;
    public IServiceProvider Services => scope.ServiceProvider;

    private DqqHost(ServiceProvider root)
    {
        this.root = root;
        scope = root.CreateAsyncScope();
    }

    public static DqqHost Create(string apiBaseUrl)
    {
        // DQQ 对象池扫描已加载的程序集，先确保 Core 和 Web.Component 都已加载。
        _ = typeof(DQQ.Components.Stages.Maps.Map).Assembly.GetTypes();
        _ = typeof(ComponentInit).Assembly.GetTypes();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServices(apiBaseUrl, isWasm: false);

        // 替换浏览器依赖（后注册的覆盖先注册的）。
        services.AddSingleton<MemoryClientRepository>();
        services.AddSingleton<IClientRepository>(sp => sp.GetRequiredService<MemoryClientRepository>());
        services.AddSingleton<IClientRepositoryReader>(sp => sp.GetRequiredService<MemoryClientRepository>());
        services.AddSingleton<IClientRepositoryMigration>(sp => sp.GetRequiredService<MemoryClientRepository>());
        services.AddSingleton<NavigationManager, RuntimeNavigationManager>();
        services.AddSingleton<IJSRuntime, NoBrowserJsRuntime>();

        return new DqqHost(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false }));
    }

    /// <summary>在线模式登录（同 DQQ 登录页的 Api/Token/Login 调用）。</summary>
    public async Task LoginOnline(string user, string password)
    {
        var auth = Services.GetRequiredService<IDQQAuth>();
        await auth.SetAuth(null, EnumPlayMode.Online);
        var http = Services.GetRequiredService<RequestClient<DQQGetHttpClient>>();
        var result = await http.Request<TokenDTO>(HttpMethod.Post, "Api/Token/Login",
            new LoginDTO { Username = user, Password = password, KeepLogin = true }.ToJson(), ignoreToken: true);
        if (!result.Success || result.Content == null)
            throw new InvalidOperationException($"DQQ 在线登录失败（状态 {result.Status}）。");
        await auth.SetAuth(result.Content, EnumPlayMode.Online);
    }

    public async Task<CharacterDTO[]> GetCharacters()
    {
        var service = Services.GetRequiredService<ICharacterService>();
        return (await service.GetAllCharacterDTOs()).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await root.DisposeAsync();
    }
}
