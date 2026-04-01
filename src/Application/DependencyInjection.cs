using System.Reflection;
using CleanArchitecture.Application.Common.Behaviours;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAutoMapper(cfg =>
            cfg.AddMaps(Assembly.GetExecutingAssembly()));

        builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        builder.Services.AddScoped<PerformanceContext>();

        builder.Services.AddLiteBus(liteBus =>
        {
            var asm = Assembly.GetExecutingAssembly();
            liteBus.AddCommandModule(m => m.RegisterFromAssembly(asm));
            liteBus.AddQueryModule(m => m.RegisterFromAssembly(asm));
            liteBus.AddEventModule(m => m.RegisterFromAssembly(asm));
        });
    }
}
