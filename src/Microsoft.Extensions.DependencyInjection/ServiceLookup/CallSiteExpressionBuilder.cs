// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal class ExpressionBuilderContext
    {
        public ParameterExpression ServiceProviderParameter { get; set; }
        public bool RequiresResolvedServices { get; set; }
    }

    internal class DiagnosticCallSiteExpressionBuilder : CallSiteExpressionBuilder
    {
        private static readonly MethodInfo InvokeLambda = typeof(Func<ServiceProvider, object>).GetMethod("Invoke");
        private static readonly MethodInfo ResolveMethod = typeof(ServiceProvider).GetMethod(nameof(ServiceProvider.Resolve), BindingFlags.Static | BindingFlags.NonPublic);


        public DiagnosticCallSiteExpressionBuilder(CallSiteRuntimeResolver runtimeResolver) : base(runtimeResolver)
        {
        }

        protected override Expression VisitCreateInstance(CreateInstanceCallSite callSite, ExpressionBuilderContext provider)
        {
            return Wrap(base.VisitCreateInstance, callSite, provider, "Create_" + CleanName(callSite.ImplementationType.Name), callSite.ImplementationType);
        }

        protected override Expression VisitConstructor(ConstructorCallSite callSite, ExpressionBuilderContext provider)
        {
            return Wrap(base.VisitConstructor, callSite, provider, "Create_" + CleanName(callSite.ImplementationType.Name), callSite.ImplementationType);
        }

        protected override Expression VisitEdgeCallSite(IServiceCallSite callSite, ExpressionBuilderContext provider)
        {
            return Wrap(base.VisitEdgeCallSite, callSite, provider, "DO_Resolve_"+ CleanName(callSite.ServiceType.Name), callSite.ServiceType);
        }

        private string CleanName(string typeName)
        {
            var chars = new char[typeName.Length];
            for (int i = 0; i < typeName.Length; i++)
            {
                chars[i] = char.IsLetterOrDigit(typeName[i]) ? typeName[i] : '_';
            }

            return new string(chars);
        }

        private Expression Wrap<T>(Func<T, ExpressionBuilderContext, Expression> func, T serviceCallSite, ExpressionBuilderContext provider, string name, Type type)
        {
            var context = new ExpressionBuilderContext()
            {
                ServiceProviderParameter = provider.ServiceProviderParameter
            };

            var expression = func(serviceCallSite, context);
            var lambda = BuildLambda(expression, context, name).Compile();

            return Expression.Call(ResolveMethod.MakeGenericMethod(type), Expression.Constant(lambda), ProviderParameter);
        }
    }

    internal class CallSiteExpressionBuilder : CallSiteVisitor<ExpressionBuilderContext, Expression>
    {
        private static readonly MethodInfo CaptureDisposableMethodInfo = GetMethodInfo<Func<ServiceProvider, object, object>>((a, b) => a.CaptureDisposable(b));
        private static readonly MethodInfo TryGetValueMethodInfo = GetMethodInfo<Func<IDictionary<object, object>, object, object, bool>>((a, b, c) => a.TryGetValue(b, out c));
        private static readonly MethodInfo AddMethodInfo = GetMethodInfo<Action<IDictionary<object, object>, object, object>>((a, b, c) => a.Add(b, c));
        private static readonly MethodInfo MonitorEnterMethodInfo = GetMethodInfo<Action<object, bool>>((lockObj, lockTaken) => Monitor.Enter(lockObj, ref lockTaken));
        private static readonly MethodInfo MonitorExitMethodInfo = GetMethodInfo<Action<object>>(lockObj => Monitor.Exit(lockObj));
        private static readonly MethodInfo CallSiteRuntimeResolverResolve =
            GetMethodInfo<Func<CallSiteRuntimeResolver, IServiceCallSite, ServiceProvider, object>>((r, c, p) => r.Resolve(c, p));

        protected static readonly ParameterExpression ProviderParameter = Expression.Parameter(typeof(ServiceProvider));

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

        public CallSiteExpressionBuilder(CallSiteRuntimeResolver runtimeResolver)
        {
            _runtimeResolver = runtimeResolver;
        }

        public Func<ServiceProvider, object> Build(IServiceCallSite callSite)
        {
            if (callSite is SingletonCallSite)
            {
                // If root call site is singleton we can return Func calling
                // _runtimeResolver.Resolve directly and avoid Expression generation
                return provider => _runtimeResolver.Resolve(callSite, provider);
            }

            var context = new ExpressionBuilderContext()
            {
                ServiceProviderParameter = ProviderParameter
            };
            return BuildLambda(VisitEdgeCallSite(callSite, context), context).Compile();
        }

        protected Expression<Func<ServiceProvider, object>> BuildLambda(Expression serviceExpression, ExpressionBuilderContext context, string name = "Resolve")
        {
            var body = new List<Expression>();
            if (context.RequiresResolvedServices)
            {
                body.Add(ResolvedServicesVariableAssignment);
                serviceExpression = Lock(serviceExpression, ResolvedServices);
            }

            body.Add(serviceExpression);

            var variables = context.RequiresResolvedServices
                ? new[] { ResolvedServices }
                : Enumerable.Empty<ParameterExpression>();

            return Expression.Lambda<Func<ServiceProvider, object>>(
                Expression.Block(variables, body),
                name,
                new [] { ProviderParameter });
        }

        protected override Expression VisitSingleton(SingletonCallSite singletonCallSite, ExpressionBuilderContext provider)
        {
            // Call to CallSiteRuntimeResolver.Resolve is being returned here
            // because in the current use case singleton service was already resolved and cached
            // to dictionary so there is no need to generate full tree at this point.

            return Expression.Call(
                Expression.Constant(_runtimeResolver),
                CallSiteRuntimeResolverResolve,
                Expression.Constant(singletonCallSite, typeof(IServiceCallSite)),
                provider.ServiceProviderParameter);
        }

        protected override Expression VisitConstant(ConstantCallSite constantCallSite, ExpressionBuilderContext provider)
        {
            return Expression.Constant(constantCallSite.DefaultValue);
        }

        protected override Expression VisitCreateInstance(CreateInstanceCallSite createInstanceCallSite, ExpressionBuilderContext provider)
        {
            return Expression.New(createInstanceCallSite.ImplementationType);
        }

        protected override Expression VisitServiceProvider(ServiceProviderCallSite serviceProviderCallSite, ExpressionBuilderContext provider)
        {
            return provider.ServiceProviderParameter;
        }

        protected override Expression VisitServiceScopeFactory(ServiceScopeFactoryCallSite serviceScopeFactoryCallSite, ExpressionBuilderContext provider)
        {
            return Expression.New(typeof(ServiceScopeFactory).GetTypeInfo()
                    .DeclaredConstructors
                    .Single(),
                provider.ServiceProviderParameter);
        }

        protected override Expression VisitFactory(FactoryCallSite factoryCallSite, ExpressionBuilderContext provider)
        {
            return Expression.Invoke(Expression.Constant(factoryCallSite.Factory), provider.ServiceProviderParameter);
        }

        protected override Expression VisitIEnumerable(IEnumerableCallSite callSite, ExpressionBuilderContext provider)
        {
            return Expression.NewArrayInit(
                callSite.ItemType,
                callSite.ServiceCallSites.Select(cs =>
                    Convert(
                        VisitEdgeCallSite(cs, provider),
                        callSite.ItemType)));
        }

        protected override Expression VisitTransient(TransientCallSite callSite, ExpressionBuilderContext provider)
        {
            var implType = callSite.ServiceCallSite.ImplementationType;
            // Elide calls to GetCaptureDisposable if the implemenation type isn't disposable
            return TryCaptureDisposible(
                implType,
                provider.ServiceProviderParameter,
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

        protected override Expression VisitConstructor(ConstructorCallSite callSite, ExpressionBuilderContext provider)
        {
            var parameters = callSite.ConstructorInfo.GetParameters();
            var expression = Expression.New(
                callSite.ConstructorInfo,
                callSite.ParameterCallSites.Select((c, index) =>
                        Convert(VisitEdgeCallSite(c, provider), parameters[index].ParameterType)));

            return expression;
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

        protected override Expression VisitScoped(ScopedCallSite callSite, ExpressionBuilderContext provider)
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
            var captureDisposible = TryCaptureDisposible(callSite.ImplementationType, provider.ServiceProviderParameter, service);

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

        protected virtual Expression VisitEdgeCallSite(IServiceCallSite callSite, ExpressionBuilderContext provider)
        {
            return VisitCallSite(callSite, provider);
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

        public Expression GetResolvedServices(ExpressionBuilderContext provider)
        {
            if (provider.ServiceProviderParameter != ProviderParameter)
            {
                throw new NotSupportedException("GetResolvedServices call is supported only for main provider");
            }
            provider.RequiresResolvedServices = true;
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