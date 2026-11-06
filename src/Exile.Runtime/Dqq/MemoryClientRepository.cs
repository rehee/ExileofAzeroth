using DQQ.Repositories;
using ReheeCmf.Entities;
using DQQ.Helper;
using ReheeCmf.Helpers;
using ReheeCmf.Responses;
using System.Collections.Concurrent;

namespace Exile.Runtime.Dqq;

/// <summary>
/// 替代浏览器 IndexedDB 的进程内存储，实现 DQQ 的 IClientRepository（DQQ 源码零改动）。
/// 在线模式下只保存本机状态（模式、token、当前角色等），业务数据在 DQQ 服务器上。
/// 每次读写都做 JSON 深拷贝，语义与 IndexedDB 一致（取出的是副本）。
/// </summary>
public sealed class MemoryClientRepository : IClientRepository, IClientRepositoryMigration
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, string>> stores = new();

    private ConcurrentDictionary<Guid, string> Store<T>() => stores.GetOrAdd(typeof(T), _ => new());

    private static T? Clone<T>(string json) where T : class => json.FromJson<T>();

    public Task<ContentResponse<bool>> Create<TContainer>(TContainer? container, Guid? id = null)
        where TContainer : EntityBase<Guid>, IClientEntityContainer
    {
        var result = new ContentResponse<bool>();
        if (container == null) return Task.FromResult(result);
        if (container.Id == Guid.Empty && id != null) container.Id = id.Value;
        Store<TContainer>()[container.Id] = container.ToJson();
        result.SetSuccess(true);
        return Task.FromResult(result);
    }

    public async Task<ContentResponse<bool>> CreateOrUpdate<TContainer>(TContainer? container, Func<TContainer, Task<ContentResponse<bool>>> update, Guid? id = null)
        where TContainer : EntityBase<Guid>, IClientEntityContainer
    {
        var targetId = container?.Id ?? id ?? Guid.NewGuid();
        var existing = await Find<TContainer>(targetId);
        if (existing == null)
        {
            if (container == null) return new ContentResponse<bool>();
            container.Id = targetId;
            return await Create(container, targetId);
        }
        var r = await update(existing);
        if (!r.Success) return r;
        Store<TContainer>()[existing.Id] = existing.ToJson();
        return true.SuccessResponse();
    }

    public Task<ContentResponse<bool>> Delete<TContainer>(Guid? id)
        where TContainer : EntityBase<Guid>, IClientEntityContainer
    {
        var result = new ContentResponse<bool>();
        Store<TContainer>().TryRemove(id ?? Guid.Empty, out _);
        result.SetSuccess(true);
        return Task.FromResult(result);
    }

    public Task<TContainer?> Find<TContainer>(Guid? id)
        where TContainer : EntityBase<Guid>, IClientEntityContainer
    {
        if (id == null || !Store<TContainer>().TryGetValue(id.Value, out var json)) return Task.FromResult<TContainer?>(null);
        return Task.FromResult(Clone<TContainer>(json));
    }

    public Task<IEnumerable<TContainer>> Read<TContainer>()
        where TContainer : EntityBase<Guid>, IClientEntityContainer
    {
        IEnumerable<TContainer> list = Store<TContainer>().Values.Select(Clone<TContainer>).OfType<TContainer>().ToArray();
        return Task.FromResult(list);
    }

    public async Task<ContentResponse<bool>> Update<TContainer>(Guid? id, Func<TContainer, Task<ContentResponse<bool>>> update)
        where TContainer : EntityBase<Guid>, IClientEntityContainer
    {
        var container = await Find<TContainer>(id);
        if (container == null) return new ContentResponse<bool>();
        var r = await update(container);
        if (!r.Success) return r;
        Store<TContainer>()[container.Id] = container.ToJson();
        return true.SuccessResponse();
    }

    public Task MigrateAsync() => Task.CompletedTask;
}
