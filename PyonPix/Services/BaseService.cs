using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using PyonPix.Config;

namespace PyonPix.Services;

public abstract class BaseService(Configuration config, IServiceContext services) : IService {
    protected readonly Configuration Config = config;
    protected readonly IServiceContext Services = services;

    public abstract Task Initialize();

    public virtual void Update(IFramework framework) { }

    public virtual Task Dispose() => Task.CompletedTask;
}
