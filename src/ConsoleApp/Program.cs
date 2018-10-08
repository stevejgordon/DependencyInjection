using System;
using Microsoft.Extensions.DependencyInjection;

namespace ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddScoped<IMyThingA, MyThingA>();

            serviceCollection.AddScoped<MyThingB>();

            var serviceProvider = serviceCollection.BuildServiceProvider();

            for (var i = 0; i < 10; i++)
            {
                serviceProvider.GetService<IMyThingA>();
            }

            Console.WriteLine("Hello World!");

            Console.ReadLine();
        }
    }

    class MyThingA : IMyThingA
    {
        private readonly MyThingB _thingB;

        public MyThingA(MyThingB thingB)
        {
            _thingB = thingB;
        }

        public string SayHello()
        {
            return _thingB.Hello();
        }
    }

    class MyThingB
    {
        public string Hello()
        {
            return "Hi";
        }
    }
}
