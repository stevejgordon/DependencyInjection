// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Extensions.DependencyInjection.Tests
{
    public class ServiceProviderDiagnosticTests
    {
        [Fact]
        public void StackTraceContainsResolutionSteps()
        {
            // Arrange

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddTransient<IFoo, Foo>();
            serviceCollection.AddTransient<IBar, Bar>();

            var provider = new ServiceProvider(serviceCollection, new ServiceProviderOptions()
            {
                InjectDiagnosticFrames = true
            });
            var callSite = provider.CallSiteFactory.CreateCallSite(typeof(IFoo), new HashSet<Type>());
            var compiledCallSite = provider.CallSiteExpressionBuilder.Build(callSite);


            // Act + Assert
            var exception = Assert.Throws<Exception>(() => compiledCallSite(provider));
            Assert.Contains("Resolve<Microsoft.Extensions.DependencyInjection.Tests.IFoo>", exception.ToString());
            Assert.Contains("Create<Microsoft.Extensions.DependencyInjection.Tests.Foo>", exception.StackTrace);
            Assert.Contains("Resolve<Microsoft.Extensions.DependencyInjection.Tests.IBar>", exception.StackTrace);
            Assert.Contains("Create<Microsoft.Extensions.DependencyInjection.Tests.Bar>", exception.StackTrace);
        }

        private interface IFoo
        {
        }

        private class Foo : IFoo
        {
            public Foo(IBar bar)
            {
            }
        }

        private interface IBar
        {
        }

        private class Bar : IBar
        {
            public Bar()
            {
                throw new Exception();
            }
        }
    }
}
