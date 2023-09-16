﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetCorePal.Extensions.DistributedTransactions.CAP.SourceGenerators
{
    [Generator]
    public class CAPSubscriberSourceGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.RootNamespace",
                out var rootNamespace);
            if (rootNamespace == null)
            {
                return;
            }
            var compilation = context.Compilation;
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (syntaxTree.TryGetText(out var sourceText) &&
                    !sourceText.ToString().Contains("IIntegrationEventHandler"))
                {
                    continue;
                }
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                if (semanticModel == null)
                {
                    continue;
                }

                var typeDeclarationSyntaxs =
                    syntaxTree.GetRoot().DescendantNodesAndSelf().OfType<TypeDeclarationSyntax>();
                foreach (var tds in typeDeclarationSyntaxs)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(tds);
                    if (!(symbol is INamedTypeSymbol)) return;
                    INamedTypeSymbol namedTypeSymbol = (INamedTypeSymbol)symbol;
                    if (!namedTypeSymbol.IsImplicitClass && namedTypeSymbol.AllInterfaces.Any(p => p.Name == "IIntegrationEventHandler"))
                    {
                        Generate(context, namedTypeSymbol, rootNamespace);
                    }
                }
            }
        }

        private void Generate(GeneratorExecutionContext context, INamedTypeSymbol eventHandlerTypeSymbol, string rootNamespace)
        {
            string className = eventHandlerTypeSymbol.Name;
            var attr = eventHandlerTypeSymbol.GetAttributes().FirstOrDefault(p => p.AttributeClass!.Name == "IntegrationEventConsumerAttribute");

            //根据dbContextType继承的接口IIntegrationEventHandle<TIntegrationEvent> 推断出TIntegrationEvent类型
            var typeArgument = eventHandlerTypeSymbol.AllInterfaces.FirstOrDefault(p => p.Name == "IIntegrationEventHandler")?.TypeArguments.FirstOrDefault();

            if (typeArgument == null)
            {
                return;
            }
            var eventName = attr?.ConstructorArguments[0].Value?.ToString();
            if (string.IsNullOrWhiteSpace(eventName))
            {
                eventName = typeArgument.Name;
            }
            var groupName = attr?.ConstructorArguments[1].Value?.ToString();
            if (!string.IsNullOrEmpty(groupName))
            {
                groupName = $@", Group = ""{groupName}""";
            }

            string source = $@"// <auto-generated/>
using DotNetCore.CAP;
using NetCorePal.Extensions.Repository;
using {eventHandlerTypeSymbol.ContainingNamespace};
namespace {rootNamespace}.Subscribers
{{
    public class {className}AsyncSubscriber : ICapSubscribe
    {{
        readonly ITransactionUnitOfWork _unitOfWork;
        readonly {className} _handler;

        public {className}AsyncSubscriber(ITransactionUnitOfWork unitOfWork, {className} handler)
        {{
            _unitOfWork = unitOfWork;
            _handler = handler;
        }}

        [CapSubscribe(""{eventName}""{groupName})]
        public Task ProcessAsync({typeArgument?.ContainingNamespace}.{typeArgument?.Name} message, CancellationToken cancellationToken)
        {{
            using (var transaction = _unitOfWork.BeginTransaction())
            {{
                try
                {{
                    return _handler.HandleAsync(message, cancellationToken);
                }}
                catch
                {{
                    transaction.Rollback();
                    throw;
                }}
            }}
        }}
    }}
}}
";
            context.AddSource($"{className}AsyncSubscriber.g.cs", source);
        }


        public void Initialize(GeneratorInitializationContext context)
        {
            // Method intentionally left empty.
        }
    }
}
