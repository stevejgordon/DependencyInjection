// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Extensions.DependencyInjection.Tests
{
    internal interface IMainScopedService
    {
    }

    internal interface IScopedService1
    {
    }
    internal interface IScopedService2
    {
    }
    internal interface IScopedService3
    {
    }

    internal interface IEntity
    {

    }

    internal class ScopedServiceImpl : IScopedService1, IScopedService2, IScopedService3
    {
    }

    internal class MainScopedService : IMainScopedService
    {
        public MainScopedService(IScopedService1 scopedService1, IScopedService2 scopedService2, IScopedService3 scopedService3)
        {
        }
    }

    internal class Entity1: IEntity
    {
        public Entity1(IMainScopedService mainScopedService)
        {
        }
    }

    internal class Entity2: IEntity
    {
        public Entity2(IMainScopedService mainScopedService)
        {
        }
    }

    internal class Entity3: IEntity
    {
        public Entity3(IMainScopedService mainScopedService)
        {
        }
    }

    internal class Entity4: IEntity
    {
        public Entity4(IMainScopedService mainScopedService)
        {
        }
    }

    internal class Entity5: IEntity
    {
        public Entity5(IMainScopedService mainScopedService)
        {
        }
    }

    internal interface IEntityManager
    {
    }

    internal class EntityManager: IEntityManager
    {
        public EntityManager(IEnumerable<IEntity> entities)
        {
        }
    }

    internal interface ICommand<T>
    {
    }

    internal class Command1<T> : ICommand<T>
    {
        public Command1(IEntityManager entityManager)
        {
        }
    }
    internal class Command2<T> : ICommand<T>
    {
        public Command2(IEntityManager entityManager)
        {
        }
    }
    internal class Command3<T> : ICommand<T>
    {
        public Command3(IEntityManager entityManager)
        {
        }
    }
    internal class Command4<T> : ICommand<T>
    {
        public Command4(IEntityManager entityManager)
        {
        }
    }

    internal interface ICommandManager<T>
    {

    }

    internal class CommandManger<T>: ICommandManager<T>
    {
        public CommandManger(IEnumerable<ICommand<T>> commands)
        {

        }
    }
    internal class AllCommandMangers
    {
        public AllCommandMangers(
            ICommandManager<string> manager1,
            ICommandManager<int> manager2,
            ICommandManager<bool> manager3,
            ICommandManager<double> manager4,
            ICommandManager<string> manager5,
            ICommandManager<int> manager6,
            ICommandManager<bool> manager7,
            ICommandManager<double> manager8,
            ICommandManager<string> manager9,
            ICommandManager<int> manager10,
            ICommandManager<bool> manager11,
            ICommandManager<double> manager12)
        {

        }
    }

    internal static class WideTreeTestData
    {
        public static void Register(IServiceCollection collection)
        {
            collection.AddScoped<IScopedService1, ScopedServiceImpl>();
            collection.AddScoped<IScopedService2, ScopedServiceImpl>();
            collection.AddScoped<IScopedService3, ScopedServiceImpl>();
            collection.AddScoped<IMainScopedService, MainScopedService>();
            collection.AddScoped<IEntity, Entity1>();
            collection.AddScoped<IEntity, Entity2>();
            collection.AddScoped<IEntity, Entity3>();
            collection.AddScoped<IEntity, Entity4>();
            collection.AddScoped<IEntity, Entity5>();
            collection.AddScoped<IEntityManager, EntityManager>();
            collection.AddScoped(typeof(ICommand<>), typeof(Command1<>));
            collection.AddScoped(typeof(ICommand<>), typeof(Command2<>));
            collection.AddScoped(typeof(ICommand<>), typeof(Command3<>));
            collection.AddScoped(typeof(ICommand<>), typeof(Command4<>));
            collection.AddScoped(typeof(ICommandManager<>), typeof(CommandManger<>));
            collection.AddScoped<AllCommandMangers>();
        }
    }
}
