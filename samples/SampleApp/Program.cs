using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddTransient<IServiceA, ServiceA>();
            serviceCollection.AddTransient<IServiceB, ServiceB>();
            var provider = serviceCollection.BuildServiceProvider(new ServiceProviderOptions() { InjectDiagnosticFrames = true});
            try { provider.GetService<IServiceA>();} catch { }
            try { provider.GetService<IServiceA>(); } catch { }
            try { provider.GetService<IServiceA>(); } catch { }


            Thread.Sleep(5000); try { provider.GetService<IServiceA>(); } catch { }
        }
    }

    internal class ServiceA: IServiceA
    {
        public ServiceA(IServiceB serviceB)
        {
        }
    }

    interface IServiceB
    {
    }

    class ServiceB : IServiceB
    {
        public ServiceB()
        {        }
    }

    interface IServiceA
    {
    }
}
