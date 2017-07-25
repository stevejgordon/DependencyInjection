// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    public abstract class GeneratedServiceProvierBase: IServiceProvider, IServiceScopeFactory
    {
        public abstract object GetService(Type serviceType);

        protected List<object> Disposables = new List<object>();

        protected IServiceScopeFactory ScopeFactory => this;

        public abstract IServiceScope CreateScope();
    }

    public static class PrecompiledServiceProviderExtensions
    {
        public static IServiceProvider BuildPrecompiledServiceProvider(this IServiceCollection serviceCollection)
        {
            SourceCodeBuilder builder = new SourceCodeBuilder();
            File.WriteAllText("d:\\DI.cs", builder.Generate(serviceCollection));

            return serviceCollection.BuildServiceProvider();
        }
    }

    internal class SourceCodeBuilder : CallSiteVisitor<CodeWriter, string>
    {

        private int i = 0;

        private string VarName()
        {
            return "s_" + (i++);
        }

        protected override string VisitTransient(TransientCallSite transientCallSite, CodeWriter argument)
        {
            var name = VarName();
            argument.WriteVariableDeclaration("var", name,
                VisitCallSite(transientCallSite.ServiceCallSite, argument));
            argument.WriteLine($"if ({name} is IDisposable)");
            using (argument.BuildScope())
            {
                argument.WriteLine($"lock (Disposables) Disposables.Add({name});");
            }

            return name;
        }

        protected override string VisitConstructor(ConstructorCallSite constructorCallSite, CodeWriter argument)
        {
            return $"new {FormatTypeName(constructorCallSite.ImplementationType)}({string.Join(", \r\n", constructorCallSite.ParameterCallSites.Select(c => VisitCallSite(c, argument)))})";
        }

        protected override string VisitSingleton(SingletonCallSite singletonCallSite, CodeWriter argument)
        {
            return VisitCallSite(singletonCallSite.ServiceCallSite, argument);
        }

        protected override string VisitScoped(ScopedCallSite scopedCallSite, CodeWriter argument)
        {
            return VisitCallSite(scopedCallSite.ServiceCallSite, argument);
        }

        protected override string VisitConstant(ConstantCallSite constantCallSite, CodeWriter argument)
        {
            return $"Contants_" + GenerateId(constantCallSite.ServiceType);
        }

        private string GenerateId(Type serviceType)
        {
            return Regex.Replace(FormatTypeName(serviceType), "[^a-zA-Z0-9]", "_");
        }

        protected override string VisitCreateInstance(CreateInstanceCallSite createInstanceCallSite, CodeWriter argument)
        {
            if (createInstanceCallSite.ImplementationType.IsPublic)
            {
                return $"new {FormatTypeName(createInstanceCallSite.ImplementationType)}()";
            }
            else
            {
                return $"Activator.CreateInstance<{FormatTypeName(createInstanceCallSite.ImplementationType)}>()";
            }
        }

        protected override string VisitServiceProvider(ServiceProviderCallSite serviceProviderCallSite, CodeWriter argument)
        {
            return "this.ServiceProvider";
        }

        protected override string VisitServiceScopeFactory(ServiceScopeFactoryCallSite serviceScopeFactoryCallSite, CodeWriter argument)
        {
            return "this.ScopeFactory";
        }

        protected override string VisitIEnumerable(IEnumerableCallSite enumerableCallSite, CodeWriter argument)
        {
            var l = new List<string>();
            foreach (var serviceCallSite in enumerableCallSite.ServiceCallSites)
            {
                l.Add(VisitCallSite(serviceCallSite, argument));
            }

            var varName = VarName();
            argument
                .Write("var ")
                .Write(varName)
                .Write(" = new ")
                .Write(FormatTypeName(enumerableCallSite.ItemType))
                .Write(" []");
            if (enumerableCallSite.ServiceCallSites.Any())
            {
                using (argument.BuildScope())
                {
                    foreach (var ll in l)
                    {
                        argument
                            .Write(ll)
                            .WriteLine(",");
                    }
                }
                argument.WriteLine(";");
            }
            else
            {
                argument.WriteLine(" {};");
            }
            return varName;
        }

        protected override string VisitFactory(FactoryCallSite factoryCallSite, CodeWriter argument)
        {
            return $"Factory_" + GenerateId(factoryCallSite.ServiceType) + "(this.ServiceProvider)";
        }

        public string Generate(IEnumerable<ServiceDescriptor> descriptors)
        {
            var factory = new CallSiteFactory(descriptors, false);
            var writer = new CodeWriter();
            var allCallSites = descriptors.Where(d => !d.ServiceType.IsGenericTypeDefinition)
                .Select(descriptor =>
                    (Descriptor: descriptor,
                     CallSite: factory.CreateCallSite(descriptor.ServiceType, new HashSet<Type>())));

            writer.WriteUsing("System");
            writer.WriteUsing("Microsoft.Extensions.DependencyModel");

            using (writer.BuildClassDeclaration(
                new[] { "public" },
                "GeneratedServiceProvider",
                "GeneratedServiceProviderBase",
                new [] { "IServiceProvider" }))
            {
                GenerateFields(allCallSites, writer);

                GenerateInitialization(allCallSites, writer);

                GenerateMethods(allCallSites, writer);

                GenerateGetService(allCallSites, writer);

            }
            return writer.GenerateCode();
        }

        private void GenerateGetService(IEnumerable<(ServiceDescriptor Descriptor, IServiceCallSite CallSite)> allCallSites, CodeWriter writer)
        {
            using (writer.BuildMethodDeclaration(
                "public",
                "object",
                "GetService",
                new[]
                {
                    new KeyValuePair<string, string>("Type", "type")
                }))
            {
                foreach (var calSite in allCallSites)
                {
                    writer.Write("if (");
                    WriteMatchType(writer, "type", calSite.CallSite.ServiceType);
                    writer.WriteLine(")");

                    using (writer.BuildScope())
                    {
                        writer.Write("return ")
                              .WriteMethodInvocation(GenerateId(calSite.CallSite.ServiceType));
                    }
                }
            }
        }

        private void GenerateMethods(IEnumerable<(ServiceDescriptor Descriptor, IServiceCallSite CallSite)> allCallSites, CodeWriter writer)
        {
            foreach (var callSite in allCallSites)
            {
                using (writer.BuildMethodDeclaration(
                    "private",
                    "object",
                    GenerateId(callSite.CallSite.ServiceType),
                    Enumerable.Empty<KeyValuePair<string, string>>()))
                {
                    var expression = VisitCallSite(callSite.CallSite, writer);
                    writer.Write("return ")
                          .Write(expression)
                          .WriteLine(";");
                }
            }
        }

        private void GenerateInitialization(IEnumerable<(ServiceDescriptor Descriptor, IServiceCallSite CallSite)> allCallSites, CodeWriter writer)
        {
            using (writer.BuildMethodDeclaration("public", "void", "Initialize", new[]
            {
                new KeyValuePair<string, string>("IEnumerable<ServiceDescriptor>", "descriptors")
            }))
            {
                writer.WriteLine("foreach (var descriptor in descriptors)");
                using (writer.BuildScope())
                {
                    foreach (var calSite in allCallSites)
                    {
                        var service = calSite.Descriptor;
                        if (service.ImplementationFactory == null &&
                            service.ImplementationInstance == null)
                        {
                            continue;
                        }

                        writer.Write("if (");
                        WriteMatchType(writer, "descriptor.ServiceType", calSite.CallSite.ServiceType);
                        writer.WriteLine(")");

                        using (writer.BuildScope())
                        {
                            if (service.ImplementationFactory != null)
                            {
                                string factoryName = "Factory_" + GenerateId(service.ServiceType);
                                writer.WriteStartAssignment(factoryName)
                                    .Write("descriptor.ImplementationFactory")
                                    .Write(";");
                            }
                            else if (service.ImplementationInstance != null)
                            {
                                var constantName = "Contants_" + GenerateId(service.ServiceType);
                                writer.WriteStartAssignment(constantName);
                                if (service.ServiceType.IsPublic)
                                {
                                    writer.Write("(")
                                        .Write(FormatTypeName(service.ServiceType))
                                        .Write(") ");
                                }

                                writer.Write("descriptor.ImplementationInstance;");
                            }
                        }
                    }
                }
            }
        }

        private void GenerateFields(IEnumerable<(ServiceDescriptor Descriptor, IServiceCallSite CallSite)> allCallSites, CodeWriter writer)
        {
            foreach (var calSite in allCallSites)
            {
                var service = calSite.Descriptor;
                if (service.ImplementationFactory != null)
                {
                    string factoryName = "Factory_" + GenerateId(service.ServiceType);
                    writer.WriteField(new[] { "private" }, "Func<IServiceProvider, object>", factoryName);
                }
                else if (service.ImplementationInstance != null)
                {
                    var constantName = "Contants_" + GenerateId(service.ServiceType);
                    writer.WriteField(new[] { "private" }, FormatTypeName(service.ServiceType), constantName);
                }
            }
        }

        public void WriteMatchType(CodeWriter writer, string prop, Type type)
        {
            writer.Write(prop);

            if (type.IsPublic)
            {
                writer.Write(" == typeof(")
                      .Write(FormatTypeName(type))
                      .Write(")");
            }
            else
            {
                writer.Write(".FullName == ")
                      .WriteStringLiteral(FormatTypeName(type));
            }
        }

        private string FormatTypeName(Type type)
        {
            if (type.IsGenericType)
            {

                var typeDefeninition = type.FullName;
                var unmangledName = typeDefeninition.Substring(0, typeDefeninition.IndexOf("`", StringComparison.Ordinal));
                return unmangledName + "<" + string.Join(",", type.GetGenericArguments().Select(FormatTypeName) ) + ">";
            }

            return type.FullName;
        }
    }

    internal class CallSiteExpressionBuilder : CallSiteVisitor<ParameterExpression, Expression>
    {
        private static readonly MethodInfo CaptureDisposableMethodInfo = GetMethodInfo<Func<ServiceProvider, object, object>>((a, b) => a.CaptureDisposable(b));
        private static readonly MethodInfo TryGetValueMethodInfo = GetMethodInfo<Func<IDictionary<object, object>, object, object, bool>>((a, b, c) => a.TryGetValue(b, out c));
        private static readonly MethodInfo AddMethodInfo = GetMethodInfo<Action<IDictionary<object, object>, object, object>>((a, b, c) => a.Add(b, c));
        private static readonly MethodInfo MonitorEnterMethodInfo = GetMethodInfo<Action<object, bool>>((lockObj, lockTaken) => Monitor.Enter(lockObj, ref lockTaken));
        private static readonly MethodInfo MonitorExitMethodInfo = GetMethodInfo<Action<object>>(lockObj => Monitor.Exit(lockObj));
        private static readonly MethodInfo CallSiteRuntimeResolverResolve =
            GetMethodInfo<Func<CallSiteRuntimeResolver, IServiceCallSite, ServiceProvider, object>>((r, c, p) => r.Resolve(c, p));

        private static readonly ParameterExpression ProviderParameter = Expression.Parameter(typeof(ServiceProvider));

        private static readonly ParameterExpression ResolvedServices = Expression.Variable(typeof(IDictionary<object, object>),
            ProviderParameter.Name + "resolvedServices");
        private static readonly BinaryExpression ResolvedServicesVariableAssignment =
            Expression.Assign(ResolvedServices,
                Expression.Property(ProviderParameter, nameof(ServiceProvider.ResolvedServices)));

        private static readonly ParameterExpression CaptureDisposableParameter = Expression.Parameter(typeof(object));
        private static readonly LambdaExpression CaptureDisposable = Expression.Lambda(
                    Expression.Call(ProviderParameter, CaptureDisposableMethodInfo, CaptureDisposableParameter),
                    CaptureDisposableParameter);

        private readonly CallSiteRuntimeResolver _runtimeResolver;
        private bool _requiresResolvedServices;

        public CallSiteExpressionBuilder(CallSiteRuntimeResolver runtimeResolver)
        {
            if (runtimeResolver == null)
            {
                throw new ArgumentNullException(nameof(runtimeResolver));
            }
            _runtimeResolver = runtimeResolver;
        }

        public Func<ServiceProvider, object> Build(IServiceCallSite callSite)
        {
            if (callSite is SingletonCallSite)
            {
                // If root call site is singleton we can return Func calling
                // _runtimeResolver.Resolve directly and avoid Expression generation
                return (provider) => _runtimeResolver.Resolve(callSite, provider);
            }
            return BuildExpression(callSite).Compile();
        }

        private Expression<Func<ServiceProvider, object>> BuildExpression(IServiceCallSite callSite)
        {
            var serviceExpression = VisitCallSite(callSite, ProviderParameter);

            var body = new List<Expression>();
            if (_requiresResolvedServices)
            {
                body.Add(ResolvedServicesVariableAssignment);
                serviceExpression = Lock(serviceExpression, ResolvedServices);
            }

            body.Add(serviceExpression);

            var variables = _requiresResolvedServices
                ? new[] { ResolvedServices }
                : Enumerable.Empty<ParameterExpression>();

            return Expression.Lambda<Func<ServiceProvider, object>>(
                Expression.Block(variables, body),
                ProviderParameter);
        }

        protected override Expression VisitSingleton(SingletonCallSite singletonCallSite, ParameterExpression provider)
        {
            // Call to CallSiteRuntimeResolver.Resolve is being returned here
            // because in the current use case singleton service was already resolved and cached
            // to dictionary so there is no need to generate full tree at this point.

            return Expression.Call(
                Expression.Constant(_runtimeResolver),
                CallSiteRuntimeResolverResolve,
                Expression.Constant(singletonCallSite, typeof(IServiceCallSite)),
                provider);
        }

        protected override Expression VisitConstant(ConstantCallSite constantCallSite, ParameterExpression provider)
        {
            return Expression.Constant(constantCallSite.DefaultValue);
        }

        protected override Expression VisitCreateInstance(CreateInstanceCallSite createInstanceCallSite, ParameterExpression provider)
        {
            return Expression.New(createInstanceCallSite.ImplementationType);
        }

        protected override Expression VisitServiceProvider(ServiceProviderCallSite serviceProviderCallSite, ParameterExpression provider)
        {
            return provider;
        }

        protected override Expression VisitServiceScopeFactory(ServiceScopeFactoryCallSite serviceScopeFactoryCallSite, ParameterExpression provider)
        {
            return Expression.New(typeof(ServiceScopeFactory).GetTypeInfo()
                    .DeclaredConstructors
                    .Single(),
                provider);
        }

        protected override Expression VisitFactory(FactoryCallSite factoryCallSite, ParameterExpression provider)
        {
            return Expression.Invoke(Expression.Constant(factoryCallSite.Factory), provider);
        }

        protected override Expression VisitIEnumerable(IEnumerableCallSite callSite, ParameterExpression provider)
        {
            return Expression.NewArrayInit(
                callSite.ItemType,
                callSite.ServiceCallSites.Select(cs =>
                    Convert(
                        VisitCallSite(cs, provider),
                        callSite.ItemType)));
        }

        protected override Expression VisitTransient(TransientCallSite callSite, ParameterExpression provider)
        {
            var implType = callSite.ServiceCallSite.ImplementationType;
            // Elide calls to GetCaptureDisposable if the implemenation type isn't disposable
            return TryCaptureDisposible(
                implType,
                provider,
                VisitCallSite(callSite.ServiceCallSite, provider));
        }

        private Expression TryCaptureDisposible(Type implType, ParameterExpression provider, Expression service)
        {

            if (implType != null &&
                !typeof(IDisposable).GetTypeInfo().IsAssignableFrom(implType.GetTypeInfo()))
            {
                return service;
            }

            return Expression.Invoke(GetCaptureDisposable(provider),
                service);
        }

        protected override Expression VisitConstructor(ConstructorCallSite callSite, ParameterExpression provider)
        {
            var parameters = callSite.ConstructorInfo.GetParameters();
            return Expression.New(
                callSite.ConstructorInfo,
                callSite.ParameterCallSites.Select((c, index) =>
                        Convert(VisitCallSite(c, provider), parameters[index].ParameterType)));
        }

        private static Expression Convert(Expression expression, Type type)
        {
            // Don't convert if the expression is already assignable
            if (type.GetTypeInfo().IsAssignableFrom(expression.Type.GetTypeInfo()))
            {
                return expression;
            }

            return Expression.Convert(expression, type);
        }

        protected override Expression VisitScoped(ScopedCallSite callSite, ParameterExpression provider)
        {
            var keyExpression = Expression.Constant(
                callSite.CacheKey,
                typeof(object));

            var resolvedVariable = Expression.Variable(typeof(object), "resolved");

            var resolvedServices = GetResolvedServices(provider);

            var tryGetValueExpression = Expression.Call(
                resolvedServices,
                TryGetValueMethodInfo,
                keyExpression,
                resolvedVariable);

            var service = VisitCallSite(callSite.ServiceCallSite, provider);
            var captureDisposible = TryCaptureDisposible(callSite.ImplementationType, provider, service);

            var assignExpression = Expression.Assign(
                resolvedVariable,
                captureDisposible);

            var addValueExpression = Expression.Call(
                resolvedServices,
                AddMethodInfo,
                keyExpression,
                resolvedVariable);

            var blockExpression = Expression.Block(
                typeof(object),
                new[] {
                    resolvedVariable
                },
                Expression.IfThen(
                    Expression.Not(tryGetValueExpression),
                    Expression.Block(
                        assignExpression,
                        addValueExpression)),
                resolvedVariable);

            return blockExpression;
        }

        private static MethodInfo GetMethodInfo<T>(Expression<T> expr)
        {
            var mc = (MethodCallExpression)expr.Body;
            return mc.Method;
        }

        public Expression GetCaptureDisposable(ParameterExpression provider)
        {
            if (provider != ProviderParameter)
            {
                throw new NotSupportedException("GetCaptureDisposable call is supported only for main provider");
            }
            return CaptureDisposable;
        }

        public Expression GetResolvedServices(ParameterExpression provider)
        {
            if (provider != ProviderParameter)
            {
                throw new NotSupportedException("GetResolvedServices call is supported only for main provider");
            }
            _requiresResolvedServices = true;
            return ResolvedServices;
        }

        private static Expression Lock(Expression body, Expression syncVariable)
        {
            // The C# compiler would copy the lock object to guard against mutation.
            // We don't, since we know the lock object is readonly.
            var lockWasTaken = Expression.Variable(typeof(bool), "lockWasTaken");

            var monitorEnter = Expression.Call(MonitorEnterMethodInfo, syncVariable, lockWasTaken);
            var monitorExit = Expression.Call(MonitorExitMethodInfo, syncVariable);

            var tryBody = Expression.Block(monitorEnter, body);
            var finallyBody = Expression.IfThen(lockWasTaken, monitorExit);

            return Expression.Block(
                typeof(object),
                new[] { lockWasTaken },
                Expression.TryFinally(tryBody, finallyBody));
        }
    }
}